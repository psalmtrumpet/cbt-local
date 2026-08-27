using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Hubs;
using NCS.CBT.Models;
using NCS.CBT.Services;
using NCS.CBT.ViewModels;

namespace NCS.CBT.Controllers;

[Authorize(Roles = "Student")]
public class StudentController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHubContext<ExamHub> _hubContext;
    private readonly AIGradingService _aiGrading;
    private readonly IWebHostEnvironment _env;
    private readonly FaceVerificationService _faceVerify;

    public StudentController(ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IHubContext<ExamHub> hubContext,
        AIGradingService aiGrading,
        IWebHostEnvironment env,
        FaceVerificationService faceVerify)
    {
        _context = context;
        _userManager = userManager;
        _hubContext = hubContext;
        _aiGrading = aiGrading;
        _env = env;
        _faceVerify = faceVerify;
    }

    // ── Serve the authenticated student's own passport photo ──────────────────
    [HttpGet]
    public async Task<IActionResult> PassportPhoto()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.PassportPhotoPath == null) return NotFound();

        var filePath = Path.Combine(_env.ContentRootPath, "data", "passports", user.PassportPhotoPath);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var contentType = filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return PhysicalFile(filePath, contentType);
    }

    // ── Capture exam photo and verify identity via Azure Face API ─────────────
    [HttpPost]
    public async Task<IActionResult> VerifyFace(IFormFile photo)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        bool verified = true;
        string message = "Photo captured. You may begin.";

        if (photo != null && photo.Length > 0)
        {
            // Read bytes once so we can save to disk AND pass to the face service
            using var ms = new MemoryStream();
            await photo.CopyToAsync(ms);
            var photoBytes = ms.ToArray();

            // Save exam photo regardless of verification result (proctor review)
            try
            {
                var dir = Path.Combine(_env.ContentRootPath, "data", "exam-photos");
                Directory.CreateDirectory(dir);
                var fileName = $"{user.StudentNumber ?? user.Id}_exam.jpg";
                var filePath = Path.Combine(dir, fileName);
                await System.IO.File.WriteAllBytesAsync(filePath, photoBytes);
                user.ExamPhotoPath = fileName;
                await _userManager.UpdateAsync(user);
            }
            catch { /* don't block student if save fails */ }

            // Run Azure Face verification if student has a passport photo
            if (!string.IsNullOrEmpty(user.PassportPhotoPath))
            {
                var passportPath = Path.Combine(_env.ContentRootPath, "data", "passports", user.PassportPhotoPath);
                if (System.IO.File.Exists(passportPath))
                {
                    using var liveStream = new MemoryStream(photoBytes);
                    var (match, _, msg) = await _faceVerify.VerifyAsync(passportPath, liveStream);
                    verified = match;
                    // Only surface the message to the student on failure (don't expose internal state)
                    if (!match) message = msg;
                }
            }
        }

        return Json(new { verified, message });
    }

    public async Task<IActionResult> Rules(int sessionId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == user.Id);

        if (session == null) return RedirectToAction("Login", "Account");
        if (session.IsSubmitted) return RedirectToAction("Completed", new { sessionId });

        ViewBag.StudentName = user.FullName;
        ViewBag.ExamTitle = session.Exam.Title;
        ViewBag.Duration = session.Exam.DurationMinutes;
        ViewBag.SessionId = sessionId;
        ViewBag.HasPassport = !string.IsNullOrEmpty(user.PassportPhotoPath);
        return View();
    }

    public async Task<IActionResult> TakeExam(int sessionId, int questionIndex = 0)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .Include(s => s.Exam)
            .ThenInclude(e => e.Questions.OrderBy(q => q.OrderNumber))
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == user.Id);

        if (session == null)
            return RedirectToAction("Login", "Account");

        if (session.IsSubmitted)
            return RedirectToAction("Completed", new { sessionId });

        var questions = session.Exam.Questions.OrderBy(q => q.OrderNumber).ToList();
        if (!questions.Any())
        {
            TempData["Error"] = "No questions found in this exam.";
            return RedirectToAction("Login", "Account");
        }

        // If student has not yet answered anything this is their first entry into TakeExam
        // (they were on the Rules page before). Reset StartTime to NOW so the timer starts
        // from when they actually begin, not from when they logged in.
        if (!session.Answers.Any())
        {
            session.StartTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // Clamp index
        if (questionIndex < 0) questionIndex = 0;
        if (questionIndex >= questions.Count) questionIndex = questions.Count - 1;

        var currentQuestion = questions[questionIndex];
        var answeredIds = session.Answers.Select(a => a.QuestionId).ToList();
        var existingAnswer = session.Answers.FirstOrDefault(a => a.QuestionId == currentQuestion.Id);

        // Calculate end time — capped at ScheduledEnd if the exam has a window
        var endTime = session.StartTime.AddMinutes(session.Exam.DurationMinutes);
        if (session.Exam.ScheduledEnd.HasValue && session.Exam.ScheduledEnd.Value < endTime)
            endTime = session.Exam.ScheduledEnd.Value;
        var endTimeMs = new DateTimeOffset(endTime, TimeSpan.Zero).ToUnixTimeMilliseconds();

        // Remaining seconds computed server-side — immune to wrong client clocks
        var remainingSeconds = (int)Math.Max(0, (endTime - DateTime.UtcNow).TotalSeconds);

        if (remainingSeconds == 0)
        {
            // Time already expired — auto-submit
            await SubmitExamInternal(session, user);
            return RedirectToAction("Completed", new { sessionId });
        }

        var vm = new ExamViewModel
        {
            SessionId = sessionId,
            ExamId = session.ExamId,
            ExamTitle = session.Exam.Title,
            TotalQuestions = questions.Count,
            CurrentIndex = questionIndex,
            EndTimeMs = endTimeMs,
            RemainingSeconds = remainingSeconds,
            QuestionNavItems = questions.Select((q, i) => new QuestionNavItem
            {
                Index = i,
                QuestionId = q.Id,
                IsAnswered = answeredIds.Contains(q.Id)
            }).ToList(),
            CurrentQuestion = new QuestionViewModel
            {
                Id = currentQuestion.Id,
                QuestionType = currentQuestion.QuestionType,
                Text = currentQuestion.Text,
                OptionA = currentQuestion.OptionA,
                OptionB = currentQuestion.OptionB,
                OptionC = currentQuestion.OptionC,
                OptionD = currentQuestion.OptionD,
                OrderNumber = currentQuestion.OrderNumber
            },
            CurrentAnswer = existingAnswer?.SelectedOption,
            CurrentTheoryAnswer = existingAnswer?.TheoryAnswer
        };

        return View(vm);
    }

    [HttpPost]
    [EnableRateLimiting("answers")]
    public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .Include(s => s.Answers)
            .Include(s => s.Exam)
            .ThenInclude(e => e.Questions)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.StudentId == user.Id);

        if (session == null || session.IsSubmitted)
            return Json(new { success = false, message = "Session not found or already submitted." });

        // Check time — respect ScheduledEnd cap if set
        var sessionEndTime = session.StartTime.AddMinutes(session.Exam.DurationMinutes);
        if (session.Exam.ScheduledEnd.HasValue && session.Exam.ScheduledEnd.Value < sessionEndTime)
            sessionEndTime = session.Exam.ScheduledEnd.Value;
        if (DateTime.UtcNow >= sessionEndTime)
        {
            await SubmitExamInternal(session, user);
            return Json(new { success = false, expired = true });
        }

        var question = session.Exam.Questions.FirstOrDefault(q => q.Id == request.QuestionId);
        if (question == null)
            return Json(new { success = false, message = "Question not found." });

        // Validate based on question type
        var isTheory = question.QuestionType == "Theory";
        var option = request.SelectedOption?.ToUpper();

        if (!isTheory && (string.IsNullOrEmpty(option) || !new[] { "A", "B", "C", "D" }.Contains(option)))
            return Json(new { success = false, message = "Invalid option." });

        if (isTheory && string.IsNullOrWhiteSpace(request.TheoryAnswer))
            return Json(new { success = false, message = "Answer cannot be empty." });

        // Upsert answer
        var existing = session.Answers.FirstOrDefault(a => a.QuestionId == request.QuestionId);
        if (existing != null)
        {
            if (isTheory)
            {
                existing.TheoryAnswer = request.TheoryAnswer;
                existing.IsCorrect = false; // graded on submit
            }
            else
            {
                existing.SelectedOption = option;
                existing.IsCorrect = option == question.CorrectOption;
            }
            existing.AnsweredAt = DateTime.UtcNow;
        }
        else
        {
            var answer = new StudentAnswer
            {
                ExamSessionId = request.SessionId,
                QuestionId = request.QuestionId,
                SelectedOption = isTheory ? null : option,
                TheoryAnswer = isTheory ? request.TheoryAnswer : null,
                IsCorrect = !isTheory && option == question.CorrectOption,
                AnsweredAt = DateTime.UtcNow
            };
            _context.StudentAnswers.Add(answer);
            session.Answers.Add(answer);
        }

        // Update live score
        await _context.SaveChangesAsync();

        // Reload answers for live score (theory graded on submit; AIScore=0 until then)
        var allAnswers = await _context.StudentAnswers
            .Include(a => a.Question)
            .Where(a => a.ExamSessionId == session.Id)
            .ToListAsync();

        session.Score = allAnswers
            .Where(a => a.Question.QuestionType != "Theory")
            .Count(a => a.IsCorrect)
            + allAnswers
            .Where(a => a.Question.QuestionType == "Theory")
            .Sum(a => (int)(a.AIScore ?? 0));
        await _context.SaveChangesAsync();

        // Broadcast to the assigned proctor + any admin viewers
        var update = new StudentProgressUpdate
        {
            SessionId = session.Id,
            StudentId = user.Id,
            StudentName = user.FullName,
            StudentNumber = user.StudentNumber ?? "",
            QuestionsAnswered = allAnswers.Count,
            TotalQuestions = session.TotalQuestions,
            CurrentScore = session.Score,
            IsSubmitted = false,
            ExamTitle = session.Exam.Title
        };
        await BroadcastToProctorAndAdmins(_hubContext, session.AssignedProctorId, "StudentProgressUpdated", update);

        return Json(new { success = true, answered = allAnswers.Count });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitExam(int sessionId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .Include(s => s.Answers)
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == user.Id);

        if (session == null)
            return RedirectToAction("Login", "Account");

        await SubmitExamInternal(session, user);
        return RedirectToAction("Completed", new { sessionId });
    }

    [HttpPost]
    public async Task<IActionResult> AutoSubmit([FromBody] SubmitExamRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .Include(s => s.Answers)
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.StudentId == user.Id);

        if (session == null || session.IsSubmitted)
            return Json(new { success = false });

        await SubmitExamInternal(session, user);
        return Json(new { success = true, redirectUrl = Url.Action("Completed", new { sessionId = request.SessionId }) });
    }

    private async Task SubmitExamInternal(ExamSession session, ApplicationUser user)
    {
        if (session.IsSubmitted) return;

        var allAnswers = await _context.StudentAnswers
            .Include(a => a.Question)
            .Where(a => a.ExamSessionId == session.Id)
            .ToListAsync();

        // Grade theory answers with AI
        foreach (var answer in allAnswers.Where(a => a.Question.QuestionType == "Theory" && !string.IsNullOrWhiteSpace(a.TheoryAnswer)))
        {
            var result = await _aiGrading.GradeAsync(
                answer.Question.Text,
                answer.Question.ModelAnswer ?? "",
                answer.TheoryAnswer!);
            answer.IsCorrect = result.Pass;
            answer.AIScore = result.Marks;   // store fuzzy mark (1/5/10/15/20)
            answer.AIFeedback = result.Feedback;
        }

        session.Score = allAnswers
            .Where(a => a.Question.QuestionType != "Theory")
            .Count(a => a.IsCorrect)
            + allAnswers
            .Where(a => a.Question.QuestionType == "Theory")
            .Sum(a => (int)(a.AIScore ?? 0));
        session.IsSubmitted = true;
        session.IsActive = false;
        session.EndTime = DateTime.UtcNow;

        user.HasCompletedExam = true;
        user.IsInSession = false;

        await _userManager.UpdateAsync(user);
        await _context.SaveChangesAsync();

        // Broadcast completion to viewers
        var update = new StudentProgressUpdate
        {
            SessionId = session.Id,
            StudentId = user.Id,
            StudentName = user.FullName,
            StudentNumber = user.StudentNumber ?? "",
            QuestionsAnswered = allAnswers.Count,
            TotalQuestions = session.TotalQuestions,
            CurrentScore = session.Score,
            IsSubmitted = true,
            ExamTitle = session.Exam?.Title ?? "",
            ViolationCount = session.ViolationCount,
            IsDisqualified = session.IsDisqualified
        };
        await BroadcastToProctorAndAdmins(_hubContext, session.AssignedProctorId, "StudentSubmitted", update);
    }

    // Send to assigned proctor's group AND the admin "viewers" group
    private static async Task BroadcastToProctorAndAdmins(IHubContext<ExamHub> hub, string? proctorId, string method, object data)
    {
        if (!string.IsNullOrEmpty(proctorId))
            await hub.Clients.Group($"viewer-{proctorId}").SendAsync(method, data);
        await hub.Clients.Group("viewers").SendAsync(method, data);
    }

    public async Task<IActionResult> Completed(int sessionId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == user.Id);

        if (session == null)
            return RedirectToAction("Login", "Account");

        ViewBag.StudentName = user.FullName;
        return View(session);
    }

    public async Task<IActionResult> Disqualified(int sessionId = 0)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        ExamSession? session = null;
        if (sessionId > 0)
        {
            session = await _context.ExamSessions
                .Include(s => s.Exam)
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == user.Id);
        }
        else
        {
            // Find most recent disqualified session
            session = await _context.ExamSessions
                .Include(s => s.Exam)
                .Where(s => s.StudentId == user.Id && s.IsDisqualified)
                .OrderByDescending(s => s.EndTime)
                .FirstOrDefaultAsync();
        }

        ViewBag.StudentName = user.FullName;
        return View(session);
    }
}
