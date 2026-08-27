using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Models;
using NCS.CBT.Services;
using NCS.CBT.ViewModels;
using System.Security.Cryptography;

namespace NCS.CBT.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    // Nigeria WAT = UTC+1, no DST
    private static readonly TimeZoneInfo _wat = GetWat();
    private static TimeZoneInfo GetWat()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("W. Central Africa Standard Time"); }
    }
    private static DateTime? ToUtc(DateTime? local) =>
        local.HasValue ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Value, DateTimeKind.Unspecified), _wat) : null;
    private static DateTime? ToWat(DateTime? utc) =>
        utc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(utc.Value, _wat) : null;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EmailService _emailService;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;

    public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, EmailService emailService, IWebHostEnvironment env, IConfiguration configuration)
    {
        _context = context;
        _userManager = userManager;
        _emailService = emailService;
        _env = env;
        _configuration = configuration;
    }

    private static string GenerateAccessCode()
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        return (BitConverter.ToUInt32(bytes) % 1_000_000).ToString("D6");
    }

    public async Task<IActionResult> Dashboard()
    {
        var totalStudents = await _userManager.GetUsersInRoleAsync("Student");
        var activeSessions = await _context.ExamSessions
            .Where(s => s.IsActive && !s.IsSubmitted)
            .CountAsync();
        var completedSessions = await _context.ExamSessions
            .Where(s => s.IsSubmitted)
            .CountAsync();
        var totalExams = await _context.Exams.CountAsync();

        var recent = await _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Where(s => s.IsSubmitted)
            .OrderByDescending(s => s.EndTime)
            .Take(10)
            .Select(s => new RecentSessionViewModel
            {
                StudentName = s.Student.FullName,
                StudentNumber = s.Student.StudentNumber ?? "",
                ExamTitle = s.Exam.Title,
                Score = s.Score,
                Total = s.TotalQuestions,
                SubmittedAt = s.EndTime ?? s.StartTime
            })
            .ToListAsync();

        var vm = new AdminDashboardViewModel
        {
            TotalStudents = totalStudents.Count,
            TotalExams = totalExams,
            ActiveSessions = activeSessions,
            CompletedExams = completedSessions,
            RecentSessions = recent
        };

        return View(vm);
    }

    // ===== EXAMS =====
    public async Task<IActionResult> Exams()
    {
        var exams = await _context.Exams
            .Include(e => e.Questions)
            .Include(e => e.ExamSessions)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
        return View(exams);
    }

    [HttpGet]
    public IActionResult CreateExam()
    {
        return View(new CreateExamViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateExam(CreateExamViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User);
        var exam = new Exam
        {
            Title = model.Title,
            Description = model.Description,
            DurationMinutes = model.DurationMinutes,
            IsActive = model.IsActive,
            CreatedById = user?.Id ?? "system",
            ScheduledStart = ToUtc(model.ScheduledStart),
            ScheduledEnd   = ToUtc(model.ScheduledEnd)
        };
        _context.Exams.Add(exam);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Exam created successfully.";
        return RedirectToAction("Questions", new { examId = exam.Id });
    }

    [HttpGet]
    public async Task<IActionResult> EditExam(int id)
    {
        var exam = await _context.Exams.Include(e => e.Questions).FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null) return NotFound();

        var vm = new EditExamViewModel
        {
            Id = exam.Id,
            Title = exam.Title,
            Description = exam.Description,
            DurationMinutes = exam.DurationMinutes,
            IsActive = exam.IsActive,
            QuestionCount = exam.Questions.Count,
            ScheduledStart = ToWat(exam.ScheduledStart),
            ScheduledEnd   = ToWat(exam.ScheduledEnd)
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditExam(EditExamViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var exam = await _context.Exams.FindAsync(model.Id);
        if (exam == null) return NotFound();

        exam.Title = model.Title;
        exam.Description = model.Description;
        exam.DurationMinutes = model.DurationMinutes;
        exam.IsActive = model.IsActive;
        exam.ScheduledStart = ToUtc(model.ScheduledStart);
        exam.ScheduledEnd   = ToUtc(model.ScheduledEnd);

        await _context.SaveChangesAsync();
        TempData["Success"] = "Exam updated successfully.";
        return RedirectToAction("Exams");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExam(int id)
    {
        var exam = await _context.Exams.FindAsync(id);
        if (exam == null) return NotFound();

        _context.Exams.Remove(exam);
        await _context.SaveChangesAsync();
        TempData["Success"] = "Exam deleted successfully.";
        return RedirectToAction("Exams");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleExamStatus(int id)
    {
        var exam = await _context.Exams.FindAsync(id);
        if (exam == null) return NotFound();

        exam.IsActive = !exam.IsActive;
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Exam {(exam.IsActive ? "activated" : "deactivated")} successfully.";
        return RedirectToAction("Exams");
    }

    // ===== QUESTIONS =====
    public async Task<IActionResult> Questions(int examId)
    {
        var exam = await _context.Exams
            .Include(e => e.Questions.OrderBy(q => q.OrderNumber))
            .FirstOrDefaultAsync(e => e.Id == examId);

        if (exam == null) return NotFound();

        ViewBag.Exam = exam;
        return View(exam.Questions.ToList());
    }

    [HttpGet]
    public async Task<IActionResult> CreateQuestion(int examId)
    {
        var exam = await _context.Exams.FindAsync(examId);
        if (exam == null) return NotFound();

        return View(new CreateQuestionViewModel { ExamId = examId, ExamTitle = exam.Title });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateQuestion(CreateQuestionViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var exam = await _context.Exams.FindAsync(model.ExamId);
            model.ExamTitle = exam?.Title ?? "";
            return View(model);
        }

        var lastOrder = await _context.Questions
            .Where(q => q.ExamId == model.ExamId)
            .MaxAsync(q => (int?)q.OrderNumber) ?? 0;

        var question = new Question
        {
            ExamId = model.ExamId,
            QuestionType = model.QuestionType == "Theory" ? "Theory" : "MCQ",
            Text = model.Text,
            OptionA = model.OptionA,
            OptionB = model.OptionB,
            OptionC = model.OptionC,
            OptionD = model.OptionD,
            CorrectOption = model.QuestionType == "MCQ" ? model.CorrectOption?.ToUpper() : null,
            ModelAnswer = model.QuestionType == "Theory" ? model.ModelAnswer : null,
            OrderNumber = lastOrder + 1
        };

        _context.Questions.Add(question);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Question added successfully.";
        return RedirectToAction("Questions", new { examId = model.ExamId });
    }

    // ── Bulk Question Upload ──────────────────────────────────────────────────

    public IActionResult DownloadQuestionTemplate()
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: MCQ ──────────────────────────────────────────────────────
        var mcq = wb.Worksheets.Add("MCQ");
        string[] mcqHeaders = { "QuestionText", "OptionA", "OptionB", "OptionC", "OptionD", "CorrectOption (A/B/C/D)" };
        for (int c = 0; c < mcqHeaders.Length; c++)
        {
            mcq.Cell(1, c + 1).Value = mcqHeaders[c];
            mcq.Cell(1, c + 1).Style.Font.Bold = true;
            mcq.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#2d6a4f");
            mcq.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
        }
        // Sample rows
        mcq.Cell(2, 1).Value = "What does RAM stand for?";
        mcq.Cell(2, 2).Value = "Random Access Memory";
        mcq.Cell(2, 3).Value = "Read Access Memory";
        mcq.Cell(2, 4).Value = "Rapid Action Memory";
        mcq.Cell(2, 5).Value = "None of the above";
        mcq.Cell(2, 6).Value = "A";

        mcq.Cell(3, 1).Value = "Which protocol is used for email?";
        mcq.Cell(3, 2).Value = "HTTP";
        mcq.Cell(3, 3).Value = "FTP";
        mcq.Cell(3, 4).Value = "SMTP";
        mcq.Cell(3, 5).Value = "TCP";
        mcq.Cell(3, 6).Value = "C";
        mcq.Columns().AdjustToContents();
        mcq.Column(1).Width = 50;

        // ── Sheet 2: Theory ───────────────────────────────────────────────────
        var theory = wb.Worksheets.Add("Theory");
        string[] thHeaders = { "QuestionText", "ModelAnswer (for AI grading)" };
        for (int c = 0; c < thHeaders.Length; c++)
        {
            theory.Cell(1, c + 1).Value = thHeaders[c];
            theory.Cell(1, c + 1).Style.Font.Bold = true;
            theory.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#495057");
            theory.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
        }
        theory.Cell(2, 1).Value = "Explain the difference between TCP and UDP.";
        theory.Cell(2, 2).Value = "TCP is connection-oriented and guarantees delivery; UDP is connectionless and faster but unreliable.";
        theory.Columns().AdjustToContents();
        theory.Column(1).Width = 60;
        theory.Column(2).Width = 70;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "questions_template.xlsx");
    }

    public async Task<IActionResult> BulkUploadQuestions(int examId)
    {
        var exam = await _context.Exams.FindAsync(examId);
        if (exam == null) return NotFound();
        ViewBag.Exam = exam;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> BulkUploadQuestions(int examId, IFormFile file)
    {
        var exam = await _context.Exams.FindAsync(examId);
        if (exam == null) return NotFound();
        ViewBag.Exam = exam;

        if (file == null || Path.GetExtension(file.FileName).ToLower() != ".xlsx")
        {
            TempData["Error"] = "Please upload a valid .xlsx file.";
            return View();
        }

        int lastOrder = await _context.Questions
            .Where(q => q.ExamId == examId)
            .MaxAsync(q => (int?)q.OrderNumber) ?? 0;

        int added = 0;
        int skipped = 0;
        var errors = new List<string>();

        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);

        // Process MCQ sheet
        if (wb.Worksheets.TryGetWorksheet("MCQ", out var mcqSheet))
        {
            int row = 2;
            while (true)
            {
                var text = mcqSheet.Cell(row, 1).GetString().Trim();
                if (string.IsNullOrEmpty(text)) break;

                var optA    = mcqSheet.Cell(row, 2).GetString().Trim();
                var optB    = mcqSheet.Cell(row, 3).GetString().Trim();
                var optC    = mcqSheet.Cell(row, 4).GetString().Trim();
                var optD    = mcqSheet.Cell(row, 5).GetString().Trim();
                var correct = mcqSheet.Cell(row, 6).GetString().Trim().ToUpper();

                if (string.IsNullOrEmpty(optA) || string.IsNullOrEmpty(optB) ||
                    !new[] { "A", "B", "C", "D" }.Contains(correct))
                {
                    errors.Add($"MCQ row {row}: skipped — missing options or invalid correct answer '{correct}'.");
                    skipped++;
                    row++;
                    continue;
                }

                _context.Questions.Add(new Question
                {
                    ExamId       = examId,
                    QuestionType = "MCQ",
                    Text         = text,
                    OptionA      = optA,
                    OptionB      = optB,
                    OptionC      = string.IsNullOrEmpty(optC) ? null : optC,
                    OptionD      = string.IsNullOrEmpty(optD) ? null : optD,
                    CorrectOption = correct,
                    OrderNumber  = ++lastOrder
                });
                added++;
                row++;
            }
        }

        // Process Theory sheet
        if (wb.Worksheets.TryGetWorksheet("Theory", out var thSheet))
        {
            int row = 2;
            while (true)
            {
                var text   = thSheet.Cell(row, 1).GetString().Trim();
                if (string.IsNullOrEmpty(text)) break;

                var model = thSheet.Cell(row, 2).GetString().Trim();

                _context.Questions.Add(new Question
                {
                    ExamId       = examId,
                    QuestionType = "Theory",
                    Text         = text,
                    ModelAnswer  = string.IsNullOrEmpty(model) ? null : model,
                    OrderNumber  = ++lastOrder
                });
                added++;
                row++;
            }
        }

        if (added > 0)
            await _context.SaveChangesAsync();

        TempData["Success"] = $"{added} question(s) imported successfully." +
            (skipped > 0 ? $" {skipped} row(s) skipped." : "");
        if (errors.Any())
            TempData["Error"] = string.Join(" | ", errors);

        return RedirectToAction("Questions", new { examId });
    }

    [HttpGet]
    public async Task<IActionResult> EditQuestion(int id)
    {
        var question = await _context.Questions.Include(q => q.Exam).FirstOrDefaultAsync(q => q.Id == id);
        if (question == null) return NotFound();

        var vm = new EditQuestionViewModel
        {
            Id = question.Id,
            ExamId = question.ExamId,
            ExamTitle = question.Exam.Title,
            QuestionType = question.QuestionType,
            Text = question.Text,
            OptionA = question.OptionA,
            OptionB = question.OptionB,
            OptionC = question.OptionC,
            OptionD = question.OptionD,
            CorrectOption = question.CorrectOption,
            ModelAnswer = question.ModelAnswer
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditQuestion(EditQuestionViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var question = await _context.Questions.FindAsync(model.Id);
        if (question == null) return NotFound();

        question.QuestionType = model.QuestionType == "Theory" ? "Theory" : "MCQ";
        question.Text = model.Text;
        question.OptionA = model.OptionA;
        question.OptionB = model.OptionB;
        question.OptionC = model.OptionC;
        question.OptionD = model.OptionD;
        question.CorrectOption = model.QuestionType == "MCQ" ? model.CorrectOption?.ToUpper() : null;
        question.ModelAnswer = model.QuestionType == "Theory" ? model.ModelAnswer : null;

        await _context.SaveChangesAsync();
        TempData["Success"] = "Question updated successfully.";
        return RedirectToAction("Questions", new { examId = model.ExamId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        var question = await _context.Questions.FindAsync(id);
        if (question == null) return NotFound();

        int examId = question.ExamId;
        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Question deleted successfully.";
        return RedirectToAction("Questions", new { examId });
    }

    // ===== STUDENTS =====
    public async Task<IActionResult> Students()
    {
        var students = await _context.Users
            .Where(u => _context.UserRoles.Any(r => r.UserId == u.Id &&
                r.RoleId == _context.Roles.Where(x => x.Name == "Student").Select(x => x.Id).FirstOrDefault()))
            .Include(u => u.AssignedExam)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        var vm = students.Select(s => new StudentListViewModel
            {
                Id = s.Id,
                StudentNumber = s.StudentNumber ?? "",
                Surname = s.Surname ?? "",
                FullName = s.FullName,
                Email = s.Email?.EndsWith("@ncs.cbt") == true ? null : s.Email,
                IsInSession = s.IsInSession,
                HasCompletedExam = s.HasCompletedExam,
                CreatedAt = s.CreatedAt,
                AssignedExamId = s.AssignedExamId,
                AssignedExamTitle = s.AssignedExam?.Title
            })
            .ToList();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> BulkUpload()
    {
        var vm = new BulkUploadViewModel
        {
            Exams = await _context.Exams.OrderByDescending(e => e.CreatedAt).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB to allow ZIP with photos
    public async Task<IActionResult> BulkUpload(BulkUploadViewModel model)
    {
        model.Exams = await _context.Exams.OrderByDescending(e => e.CreatedAt).ToListAsync();
        if (!ModelState.IsValid || model.File == null)
            return View(model);

        var ext = Path.GetExtension(model.File.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".zip")
        {
            ModelState.AddModelError("File", "Upload either an .xlsx file or a .zip file containing the Excel plus passport photos.");
            return View(model);
        }

        // Extract Excel stream and collect any embedded passport photos from ZIP
        Stream excelStream;
        var passportEntries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        if (ext == ".zip")
        {
            using var zip = new System.IO.Compression.ZipArchive(model.File.OpenReadStream(), System.IO.Compression.ZipArchiveMode.Read);
            var xlsxEntry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            if (xlsxEntry == null)
            {
                ModelState.AddModelError("File", "No .xlsx file found inside the ZIP.");
                return View(model);
            }

            // Read Excel into memory first
            var xlsBytes = new MemoryStream();
            using (var es = xlsxEntry.Open()) await es.CopyToAsync(xlsBytes);
            xlsBytes.Position = 0;
            excelStream = xlsBytes;

            // Collect photos
            var passportDir = Path.Combine(_env.ContentRootPath, "data", "passports");
            Directory.CreateDirectory(passportDir);
            foreach (var entry in zip.Entries)
            {
                var photoExt = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (photoExt is not (".jpg" or ".jpeg" or ".png")) continue;
                var studentNo = Path.GetFileNameWithoutExtension(entry.Name).Trim();
                using var ms = new MemoryStream();
                using var es = entry.Open();
                await es.CopyToAsync(ms);
                passportEntries[studentNo] = ms.ToArray();
            }
        }
        else
        {
            excelStream = model.File.OpenReadStream();
        }

        var results = new List<BulkUploadResultItem>();
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>();

        using (excelStream)
        using (var wb = new XLWorkbook(excelStream))
        {
            var ws = wb.Worksheets.First();
            var passportDir = Path.Combine(_env.ContentRootPath, "data", "passports");
            Directory.CreateDirectory(passportDir);

            int row = 2;
            while (true)
            {
                var studentNumber = ws.Cell(row, 1).GetString().Trim().ToUpper();
                if (string.IsNullOrEmpty(studentNumber)) break;

                var surname  = ws.Cell(row, 2).GetString().Trim().ToUpper();
                var fullName = ws.Cell(row, 3).GetString().Trim();
                var emailVal = ws.Cell(row, 4).GetString().Trim();

                var item = new BulkUploadResultItem { Row = row, StudentNumber = studentNumber, FullName = fullName };

                if (string.IsNullOrEmpty(surname) || string.IsNullOrEmpty(fullName))
                {
                    item.Error = "Surname and Full Name are required.";
                    results.Add(item);
                    row++;
                    continue;
                }

                var existing = _userManager.Users.FirstOrDefault(u => u.StudentNumber == studentNumber);
                if (existing != null)
                {
                    item.Error = "Student number already registered.";
                    results.Add(item);
                    row++;
                    continue;
                }

                var accessCode = GenerateAccessCode();
                var emailAddr  = !string.IsNullOrEmpty(emailVal) ? emailVal : $"{studentNumber.ToLower()}@ncs.cbt";

                var student = new ApplicationUser
                {
                    UserName       = studentNumber,
                    Email          = emailAddr,
                    FullName       = fullName,
                    StudentNumber  = studentNumber,
                    Surname        = surname,
                    EmailConfirmed = true,
                    CreatedAt      = DateTime.UtcNow
                };
                student.AccessCodeHash = hasher.HashPassword(student, accessCode);

                // Attach passport photo if found in ZIP
                if (passportEntries.TryGetValue(studentNumber, out var photoBytes))
                {
                    var photoExt = passportEntries.Keys
                        .Where(k => string.Equals(k, studentNumber, StringComparison.OrdinalIgnoreCase))
                        .Select(_ => ".jpg").FirstOrDefault() ?? ".jpg";
                    var photoFile = $"{studentNumber}{photoExt}";
                    await System.IO.File.WriteAllBytesAsync(Path.Combine(passportDir, photoFile), photoBytes);
                    student.PassportPhotoPath = photoFile;
                }

                var createResult = await _userManager.CreateAsync(student, $"Student@{studentNumber}");
                if (!createResult.Succeeded)
                {
                    item.Error = string.Join("; ", createResult.Errors.Select(e => e.Description));
                    results.Add(item);
                    row++;
                    continue;
                }

                await _userManager.AddToRoleAsync(student, "Student");

                // Assign exam if one was selected at upload time
                if (model.AssignedExamId.HasValue)
                {
                    student.AssignedExamId = model.AssignedExamId.Value;
                    await _userManager.UpdateAsync(student);
                }

                item.AccessCode = accessCode;
                item.Success = true;

                if (!string.IsNullOrEmpty(emailVal))
                {
                    _ = _emailService.SendStudentCredentialsAsync(emailVal, fullName, studentNumber, surname, accessCode);
                    item.EmailSent = true;
                }

                results.Add(item);
                row++;
            }
        }

        TempData["BulkResults"] = System.Text.Json.JsonSerializer.Serialize(results);
        return RedirectToAction("BulkUploadResults");
    }

    public IActionResult BulkUploadResults()
    {
        var json = TempData["BulkResults"]?.ToString();
        if (string.IsNullOrEmpty(json))
            return RedirectToAction("Students");

        var results = System.Text.Json.JsonSerializer.Deserialize<List<BulkUploadResultItem>>(json) ?? new();
        return View(results);
    }

    [HttpGet]
    public IActionResult DownloadBulkTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Students");

        ws.Cell(1, 1).Value = "StudentNumber";
        ws.Cell(1, 2).Value = "Surname";
        ws.Cell(1, 3).Value = "FullName";
        ws.Cell(1, 4).Value = "Email";

        var header = ws.Range("A1:D1");
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a3c5e");
        header.Style.Font.FontColor = XLColor.White;

        // Sample row
        ws.Cell(2, 1).Value = "NCS2026001";
        ws.Cell(2, 2).Value = "OKAFOR";
        ws.Cell(2, 3).Value = "Emmanuel Okafor";
        ws.Cell(2, 4).Value = "emmanuel.okafor@example.com";

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "students_template.xlsx");
    }

    [HttpGet]
    public IActionResult CreateStudent()
    {
        return View(new CreateStudentViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStudent(CreateStudentViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var existing = _userManager.Users.FirstOrDefault(u => u.StudentNumber == model.StudentNumber);
        if (existing != null)
        {
            ModelState.AddModelError("StudentNumber", "This student number is already registered.");
            return View(model);
        }

        var accessCode = GenerateAccessCode();
        var email = !string.IsNullOrWhiteSpace(model.Email)
            ? model.Email.Trim()
            : $"{model.StudentNumber.ToLower()}@ncs.cbt";

        var student = new ApplicationUser
        {
            UserName = model.StudentNumber,
            Email = email,
            FullName = model.FullName,
            StudentNumber = model.StudentNumber,
            Surname = model.Surname.ToUpper(),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>();
        student.AccessCodeHash = hasher.HashPassword(student, accessCode);

        // Save passport photo if provided
        if (model.PassportPhoto != null && model.PassportPhoto.Length > 0)
        {
            var ext = Path.GetExtension(model.PassportPhoto.FileName).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png")
            {
                var dir = Path.Combine(_env.ContentRootPath, "data", "passports");
                Directory.CreateDirectory(dir);
                var fileName = $"{model.StudentNumber}{ext}";
                var filePath = Path.Combine(dir, fileName);
                using var fs = new FileStream(filePath, FileMode.Create);
                await model.PassportPhoto.CopyToAsync(fs);
                student.PassportPhotoPath = fileName;
            }
        }

        var result = await _userManager.CreateAsync(student, $"Student@{model.StudentNumber}");
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _userManager.AddToRoleAsync(student, "Student");

        if (!string.IsNullOrWhiteSpace(model.Email))
            _ = _emailService.SendStudentCredentialsAsync(
                model.Email.Trim(), model.FullName, model.StudentNumber,
                model.Surname.ToUpper(), accessCode);

        var emailNote = !string.IsNullOrWhiteSpace(model.Email) ? " Credentials email queued." : "";
        TempData["Success"] = $"Student registered. Number: {model.StudentNumber} | Surname: {model.Surname.ToUpper()} | Access Code: {accessCode}{emailNote}";
        return RedirectToAction("Students");
    }

    // ── Bulk passport photo upload (ZIP of student-number.jpg files) ──────────
    [HttpGet]
    public IActionResult UploadPassports() => View(new BulkPassportUploadViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPassports(BulkPassportUploadViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (model.ZipFile == null || model.ZipFile.Length == 0) return View(model);

        var dir = Path.Combine(_env.ContentRootPath, "data", "passports");
        Directory.CreateDirectory(dir);

        int matched = 0, skipped = 0;
        using var zip = new System.IO.Compression.ZipArchive(model.ZipFile.OpenReadStream(), System.IO.Compression.ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            var name = Path.GetFileNameWithoutExtension(entry.Name).Trim();
            var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png")) { skipped++; continue; }

            var student = _userManager.Users.FirstOrDefault(u => u.StudentNumber == name);
            if (student == null) { skipped++; continue; }

            var fileName = $"{name}{ext}";
            var filePath = Path.Combine(dir, fileName);
            using var fs = new FileStream(filePath, FileMode.Create);
            using var es = entry.Open();
            await es.CopyToAsync(fs);

            student.PassportPhotoPath = fileName;
            await _userManager.UpdateAsync(student);
            matched++;
        }

        TempData["Success"] = $"Passports uploaded: {matched} matched, {skipped} skipped (no matching student or unsupported file).";
        return RedirectToAction("Students");
    }

    // ── Serve a student's passport photo (Admin/Viewer only) ──────────────────
    [HttpGet]
    public IActionResult StudentPassportPhoto(string studentNumber)
    {
        var student = _userManager.Users.FirstOrDefault(u => u.StudentNumber == studentNumber);
        if (student?.PassportPhotoPath == null) return NotFound();

        var filePath = Path.Combine(_env.ContentRootPath, "data", "passports", student.PassportPhotoPath);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var contentType = filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return PhysicalFile(filePath, contentType);
    }

    // ── Serve a student's exam-day captured photo (Admin/Viewer only) ─────────
    [HttpGet]
    [Authorize(Roles = "Admin,Viewer")]
    public IActionResult StudentExamPhoto(string studentNumber)
    {
        var student = _userManager.Users.FirstOrDefault(u => u.StudentNumber == studentNumber);
        if (student?.ExamPhotoPath == null) return NotFound();

        var filePath = Path.Combine(_env.ContentRootPath, "data", "exam-photos", student.ExamPhotoPath);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        return PhysicalFile(filePath, "image/jpeg");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStudent(string id)
    {
        var student = await _userManager.FindByIdAsync(id);
        if (student == null) return NotFound();

        await _userManager.DeleteAsync(student);
        TempData["Success"] = "Student deleted successfully.";
        return RedirectToAction("Students");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteStudents(List<string> studentIds)
    {
        if (!studentIds.Any())
        {
            TempData["Error"] = "No students selected.";
            return RedirectToAction("Students");
        }

        var students = await _userManager.Users
            .Where(u => studentIds.Contains(u.Id))
            .ToListAsync();

        int deleted = 0;
        foreach (var student in students)
        {
            // Delete answers and sessions first to satisfy FK constraint
            var sessionIds = await _context.ExamSessions
                .Where(s => s.StudentId == student.Id)
                .Select(s => s.Id)
                .ToListAsync();
            if (sessionIds.Any())
            {
                await _context.StudentAnswers
                    .Where(a => sessionIds.Contains(a.ExamSessionId))
                    .ExecuteDeleteAsync();
                await _context.ExamSessions
                    .Where(s => s.StudentId == student.Id)
                    .ExecuteDeleteAsync();
            }
            var result = await _userManager.DeleteAsync(student);
            if (result.Succeeded) deleted++;
        }

        TempData["Success"] = $"{deleted} student(s) deleted successfully.";
        return RedirectToAction("Students");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendStudentCredentials(string id)
    {
        var student = await _userManager.FindByIdAsync(id);
        if (student == null) return NotFound();

        if (string.IsNullOrEmpty(student.Email) || student.Email.EndsWith("@ncs.cbt"))
        {
            TempData["Error"] = "This student has no email address — credentials cannot be sent.";
            return RedirectToAction("Students");
        }

        var accessCode = GenerateAccessCode();
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>();
        student.AccessCodeHash = hasher.HashPassword(student, accessCode);
        await _userManager.UpdateAsync(student);

        var sent = await _emailService.SendStudentCredentialsAsync(
            student.Email,
            student.FullName,
            student.StudentNumber ?? student.UserName ?? "",
            student.Surname ?? "",
            accessCode);

        TempData[sent ? "Success" : "Error"] = sent
            ? $"Credentials email sent to {student.Email}."
            : $"Failed to send email to {student.Email}. Check SMTP settings.";

        return RedirectToAction("Students");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetStudentSession(string id)
    {
        var student = await _userManager.FindByIdAsync(id);
        if (student == null) return NotFound();

        student.IsInSession = false;
        student.HasCompletedExam = false;
        student.SessionStartTime = null;

        // Delete all answers and sessions so the student has a completely clean record
        var sessionIds = await _context.ExamSessions
            .Where(s => s.StudentId == id)
            .Select(s => s.Id)
            .ToListAsync();

        if (sessionIds.Any())
        {
            await _context.StudentAnswers
                .Where(a => sessionIds.Contains(a.ExamSessionId))
                .ExecuteDeleteAsync();
            await _context.ExamSessions
                .Where(s => s.StudentId == id)
                .ExecuteDeleteAsync();
        }

        await _userManager.UpdateAsync(student);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Student reset — all sessions and answers cleared.";
        return RedirectToAction("Students");
    }

    // ===== PROCTORS =====

    [HttpGet]
    public async Task<IActionResult> Proctors()
    {
        var viewers = await _userManager.GetUsersInRoleAsync("Viewer");
        var vm = viewers.Select(u => new ProctorListViewModel
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email ?? "",
            CreatedAt = u.CreatedAt
        }).OrderBy(u => u.FullName).ToList();
        return View(vm);
    }

    [HttpGet]
    public IActionResult CreateProctor() => View(new CreateProctorViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProctor(CreateProctorViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        if (await _userManager.FindByEmailAsync(model.Email.Trim()) != null)
        {
            ModelState.AddModelError("Email", "An account with this email already exists.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            FullName = model.FullName.Trim(),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, "Viewer");

        var examUrl = _configuration["Email:ExamUrl"] ?? "https://cbt.tlimc.net";
        _ = _emailService.SendProctorCredentialsAsync(user.Email!, user.FullName, model.Password, examUrl);

        TempData["Success"] = $"Proctor '{model.FullName}' registered. Login credentials sent to {user.Email}.";
        return RedirectToAction("Proctors");
    }

    [HttpGet]
    public async Task<IActionResult> ResetProctorPassword(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !await _userManager.IsInRoleAsync(user, "Viewer")) return NotFound();
        return View(new ResetProctorPasswordViewModel { Id = id, FullName = user.FullName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetProctorPassword(ResetProctorPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(model.Id);
        if (user == null || !await _userManager.IsInRoleAsync(user, "Viewer")) return NotFound();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        var examUrl = _configuration["Email:ExamUrl"] ?? "https://cbt.tlimc.net";
        _ = _emailService.SendProctorCredentialsAsync(user.Email!, user.FullName, model.NewPassword, examUrl);

        TempData["Success"] = $"Password for '{user.FullName}' reset. New credentials sent to {user.Email}.";
        return RedirectToAction("Proctors");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProctor(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !await _userManager.IsInRoleAsync(user, "Viewer")) return NotFound();

        await _userManager.DeleteAsync(user);
        TempData["Success"] = "Proctor removed successfully.";
        return RedirectToAction("Proctors");
    }

    // ===== RESULTS =====
    public async Task<IActionResult> Results(int? examId = null)
    {
        var query = _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Where(s => s.IsSubmitted)
            .AsQueryable();

        if (examId.HasValue)
            query = query.Where(s => s.ExamId == examId.Value);

        var sessions = await query
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.EndTime)
            .ToListAsync();

        var rawResults = sessions.Select(s => new ExamResultViewModel
        {
            SessionId     = s.Id,
            StudentName   = s.Student.FullName,
            StudentNumber = s.Student.StudentNumber ?? "",
            ExamTitle     = s.Exam.Title,
            Score         = s.Score,
            TotalQuestions = s.TotalQuestions,
            MCQScore      = s.Answers.Where(a => a.Question?.QuestionType != "Theory").Count(a => a.IsCorrect),
            TheoryScore   = (int)s.Answers.Where(a => a.Question?.QuestionType == "Theory").Sum(a => a.AIScore ?? 0),
            MCQCount      = s.Answers.Count(a => a.Question?.QuestionType != "Theory"),
            TheoryCount   = s.Answers.Count(a => a.Question?.QuestionType == "Theory"),
            StartTime     = s.StartTime,
            EndTime       = s.EndTime,
            IsSubmitted   = s.IsSubmitted,
            IsDisqualified = s.IsDisqualified
        }).ToList();

        // Assign ranks
        for (int i = 0; i < rawResults.Count; i++)
            rawResults[i].Rank = i + 1;

        var exams = await _context.Exams.OrderByDescending(e => e.CreatedAt).ToListAsync();

        var vm = new ResultsViewModel
        {
            Results = rawResults,
            Exams = exams,
            SelectedExamId = examId
        };

        return View(vm);
    }

    // ── Export: Student Answers ───────────────────────────────────────────────
    public async Task<IActionResult> ExportAnswers(int? examId = null)
    {
        var query = _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Where(s => s.IsSubmitted)
            .AsQueryable();

        if (examId.HasValue)
            query = query.Where(s => s.ExamId == examId.Value);

        var sessions = await query.OrderBy(s => s.Student.StudentNumber).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Student Answers");

        // Header
        string[] headers = { "Rank", "Student No.", "Full Name", "Exam", "Score", "Total Qs",
                              "%", "Q No.", "Question", "Type", "Student Answer", "Correct Answer", "Correct?" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#2d6a4f");
            ws.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
        }

        int row = 2;
        int rank = 1;
        var sorted = sessions.OrderByDescending(s => s.Score).ThenBy(s => s.EndTime).ToList();
        foreach (var session in sorted)
        {
            var answers = session.Answers.OrderBy(a => a.Question.OrderNumber).ToList();
            if (!answers.Any())
            {
                ws.Cell(row, 1).Value = rank;
                ws.Cell(row, 2).Value = session.Student.StudentNumber;
                ws.Cell(row, 3).Value = session.Student.FullName;
                ws.Cell(row, 4).Value = session.Exam.Title;
                ws.Cell(row, 5).Value = session.Score;
                ws.Cell(row, 6).Value = session.TotalQuestions;
                ws.Cell(row, 7).Value = session.TotalQuestions > 0
                    ? Math.Round((double)session.Score / session.TotalQuestions * 100, 1) : 0;
                ws.Cell(row, 8).Value = "—";
                ws.Cell(row, 9).Value = "(no answers)";
                row++;
            }
            else
            {
                foreach (var a in answers)
                {
                    ws.Cell(row, 1).Value = rank;
                    ws.Cell(row, 2).Value = session.Student.StudentNumber;
                    ws.Cell(row, 3).Value = session.Student.FullName;
                    ws.Cell(row, 4).Value = session.Exam.Title;
                    ws.Cell(row, 5).Value = session.Score;
                    ws.Cell(row, 6).Value = session.TotalQuestions;
                    ws.Cell(row, 7).Value = session.TotalQuestions > 0
                        ? Math.Round((double)session.Score / session.TotalQuestions * 100, 1) : 0;
                    ws.Cell(row, 8).Value = a.Question.OrderNumber;
                    ws.Cell(row, 9).Value = a.Question.Text;
                    ws.Cell(row, 10).Value = a.Question.QuestionType;
                    ws.Cell(row, 11).Value = a.Question.QuestionType == "Theory"
                        ? a.TheoryAnswer : a.SelectedOption;
                    ws.Cell(row, 12).Value = a.Question.QuestionType == "Theory"
                        ? "(theory)" : a.Question.CorrectOption;
                    ws.Cell(row, 13).Value = a.IsCorrect ? "Yes" : "No";
                    if (a.IsCorrect)
                        ws.Cell(row, 13).Style.Font.FontColor = XLColor.Green;
                    else
                        ws.Cell(row, 13).Style.Font.FontColor = XLColor.Red;
                    row++;
                }
            }
            rank++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(9).Width = 60; // Question text — cap it

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var fileName = examId.HasValue
            ? $"answers_exam{examId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            : $"answers_all_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // ── Export: Violations ────────────────────────────────────────────────────
    public async Task<IActionResult> ExportViolations(int? examId = null)
    {
        var query = _context.ProctorViolations
            .Include(v => v.ExamSession).ThenInclude(s => s.Student)
            .Include(v => v.ExamSession).ThenInclude(s => s.Exam)
            .AsQueryable();

        if (examId.HasValue)
            query = query.Where(v => v.ExamSession.ExamId == examId.Value);

        var violations = await query
            .OrderBy(v => v.ExamSession.Student.StudentNumber)
            .ThenBy(v => v.Timestamp)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Violations");

        string[] headers = { "Student No.", "Full Name", "Exam", "Violation Type", "Timestamp (WAT)",
                              "Total Violations (Session)" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#b02a37");
            ws.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
        }

        int row = 2;
        foreach (var v in violations)
        {
            var watTime = TimeZoneInfo.ConvertTimeFromUtc(v.Timestamp, _wat);
            ws.Cell(row, 1).Value = v.ExamSession.Student.StudentNumber;
            ws.Cell(row, 2).Value = v.ExamSession.Student.FullName;
            ws.Cell(row, 3).Value = v.ExamSession.Exam.Title;
            ws.Cell(row, 4).Value = v.ViolationType;
            ws.Cell(row, 5).Value = watTime.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, 6).Value = v.ExamSession.ViolationCount;
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var fileName = examId.HasValue
            ? $"violations_exam{examId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            : $"violations_all_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public async Task<IActionResult> SessionDetail(int sessionId)
    {
        var session = await _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return NotFound();

        var answers = session.Answers
            .OrderBy(a => a.Question.OrderNumber)
            .Select(a => new AnswerDetailViewModel
            {
                OrderNumber = a.Question.OrderNumber,
                QuestionText = a.Question.Text,
                QuestionType = a.Question.QuestionType,
                OptionA = a.Question.OptionA,
                OptionB = a.Question.OptionB,
                OptionC = a.Question.OptionC,
                OptionD = a.Question.OptionD,
                CorrectOption = a.Question.CorrectOption,
                ModelAnswer = a.Question.ModelAnswer,
                SelectedOption = a.SelectedOption,
                TheoryAnswer = a.TheoryAnswer,
                AIFeedback = a.AIFeedback,
                AIScore = a.AIScore,
                IsCorrect = a.IsCorrect
            })
            .ToList();

        var vm = new SessionDetailViewModel
        {
            SessionId = session.Id,
            StudentName = session.Student.FullName,
            StudentNumber = session.Student.StudentNumber ?? "",
            ExamTitle = session.Exam.Title,
            Score = session.Score,
            TotalQuestions = session.TotalQuestions,
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            IsDisqualified = session.IsDisqualified,
            Answers = answers
        };

        return View(vm);
    }

    // ===== ASSIGN EXAMS =====

    [HttpGet]
    public async Task<IActionResult> AssignExams(string filter = "all", string? search = null)
    {
        var exams = await _context.Exams.OrderBy(e => e.Title).ToListAsync();

        var studentsQuery = _userManager.Users
            .Where(u => u.StudentNumber != null);

        if (!string.IsNullOrWhiteSpace(search))
            studentsQuery = studentsQuery.Where(u =>
                u.FullName.Contains(search) ||
                (u.StudentNumber != null && u.StudentNumber.Contains(search)) ||
                (u.Email != null && u.Email.Contains(search)));

        if (filter == "unassigned")
            studentsQuery = studentsQuery.Where(u => u.AssignedExamId == null);
        else if (filter.StartsWith("exam:") && int.TryParse(filter[5..], out var fid))
            studentsQuery = studentsQuery.Where(u => u.AssignedExamId == fid);

        var students = await studentsQuery
            .OrderBy(u => u.StudentNumber)
            .Select(u => new StudentAssignmentItem
            {
                Id              = u.Id,
                StudentNumber   = u.StudentNumber ?? "",
                FullName        = u.FullName,
                Email           = u.Email,
                AssignedExamId  = u.AssignedExamId,
                AssignedExamTitle = u.AssignedExam != null ? u.AssignedExam.Title : null,
                HasCompletedExam = u.HasCompletedExam
            })
            .ToListAsync();

        var vm = new AssignExamsViewModel
        {
            Exams      = exams,
            Students   = students,
            Filter     = filter,
            SearchTerm = search
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignExams(int examId, List<string> studentIds)
    {
        if (!studentIds.Any())
        {
            TempData["Error"] = "No students selected.";
            return RedirectToAction("AssignExams");
        }

        var exam = await _context.Exams.FindAsync(examId);
        if (exam == null)
        {
            TempData["Error"] = "Exam not found.";
            return RedirectToAction("AssignExams");
        }

        var students = await _userManager.Users
            .Where(u => studentIds.Contains(u.Id))
            .ToListAsync();

        foreach (var student in students)
            student.AssignedExamId = examId;

        await _context.SaveChangesAsync();

        TempData["Success"] = $"{students.Count} student(s) assigned to \"{exam.Title}\".";
        return RedirectToAction("AssignExams");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignExam(string studentId)
    {
        var student = await _userManager.FindByIdAsync(studentId);
        if (student == null) return NotFound();

        student.AssignedExamId = null;
        await _userManager.UpdateAsync(student);

        TempData["Success"] = $"{student.FullName} unassigned from their exam.";
        return RedirectToAction("AssignExams");
    }
}
