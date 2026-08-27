using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Hubs;
using NCS.CBT.Models;
using NCS.CBT.ViewModels;

namespace NCS.CBT.Controllers;

public class ProctorController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHubContext<ExamHub> _hubContext;
    private readonly IWebHostEnvironment _env;

    public ProctorController(ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IHubContext<ExamHub> hubContext,
        IWebHostEnvironment env)
    {
        _context = context;
        _userManager = userManager;
        _hubContext = hubContext;
        _env = env;
    }

    // ── Log a proctoring violation ────────────────────────────────────────────
    [Authorize(Roles = "Student")]
    [HttpPost]
    public async Task<IActionResult> LogViolation([FromBody] ViolationLogRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId
                                   && s.StudentId == user.Id
                                   && !s.IsSubmitted);

        if (session == null)
            return Json(new ViolationLogResponse { Success = false });

        // Save snapshot if provided (max ~1.5 MB decoded = ~2 MB base64)
        const int MaxSnapshotBase64Length = 2 * 1024 * 1024;
        string? snapshotPath = null;
        if (!string.IsNullOrEmpty(request.SnapshotBase64)
            && request.SnapshotBase64.Length <= MaxSnapshotBase64Length)
        {
            snapshotPath = await SaveSnapshotAsync(request.SnapshotBase64, request.SessionId);
        }

        // Record violation
        var violation = new ProctorViolation
        {
            ExamSessionId = session.Id,
            ViolationType = request.ViolationType,
            Timestamp = DateTime.UtcNow,
            SnapshotPath = snapshotPath
        };
        _context.ProctorViolations.Add(violation);

        session.ViolationCount++;
        var count = session.ViolationCount;

        await _context.SaveChangesAsync();

        // Broadcast to assigned proctor + admins
        var violationPayload = new
        {
            sessionId = session.Id,
            studentName = user.FullName,
            studentNumber = user.StudentNumber ?? "",
            violationType = request.ViolationType,
            violationCount = count,
            isDisqualified = false,
            timestamp = DateTime.UtcNow.ToString("HH:mm:ss"),
            snapshotPath = snapshotPath
        };
        if (!string.IsNullOrEmpty(session.AssignedProctorId))
            await _hubContext.Clients.Group($"viewer-{session.AssignedProctorId}").SendAsync("ViolationDetected", violationPayload);
        await _hubContext.Clients.Group("viewers").SendAsync("ViolationDetected", violationPayload);

        return Json(new ViolationLogResponse
        {
            Success = true,
            Action = "warn",
            ViolationCount = count,
            RedirectUrl = null
        });
    }

    // ── Proctor manually disqualifies a student ───────────────────────────────
    [Authorize(Roles = "Admin,Viewer")]
    [HttpPost]
    public async Task<IActionResult> DisqualifyStudent([FromBody] DisqualifyRequest request)
    {
        var session = await _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId);

        if (session == null)
            return Json(new { success = false, message = "Session not found." });

        if (session.IsDisqualified)
            return Json(new { success = false, message = "Student is already disqualified." });

        var student = session.Student;

        if (!session.IsSubmitted)
        {
            // Live disqualification — submit and notify student
            var answers = await _context.StudentAnswers
                .Where(a => a.ExamSessionId == session.Id)
                .ToListAsync();
            session.Score = answers.Count(a => a.IsCorrect);
            session.IsSubmitted = true;
            session.IsActive = false;
            session.EndTime = DateTime.UtcNow;
            student.HasCompletedExam = true;
            student.IsInSession = false;
            await _userManager.UpdateAsync(student);
            await _hubContext.Clients.Group($"student-{session.StudentId}").SendAsync("Disqualified");
        }

        session.IsDisqualified = true;
        await _context.SaveChangesAsync();

        // Notify assigned proctor + admins
        var disqPayload = new
        {
            sessionId = session.Id,
            studentName = student.FullName,
            studentNumber = student.StudentNumber ?? "",
            timestamp = DateTime.UtcNow.ToString("HH:mm:ss")
        };
        if (!string.IsNullOrEmpty(session.AssignedProctorId))
            await _hubContext.Clients.Group($"viewer-{session.AssignedProctorId}").SendAsync("StudentDisqualified", disqPayload);
        await _hubContext.Clients.Group("viewers").SendAsync("StudentDisqualified", disqPayload);

        return Json(new { success = true });
    }

    // ── Receive a recording chunk (audio+video) ────────────────────────────────
    [Authorize(Roles = "Student")]
    [HttpPost]
    public async Task<IActionResult> SaveChunk(int sessionId, IFormFile chunk)
    {
        const long MaxChunkBytes = 10 * 1024 * 1024; // 10 MB

        if (chunk == null || chunk.Length == 0 || chunk.Length > MaxChunkBytes)
            return BadRequest("Invalid chunk.");

        if (!chunk.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Invalid content type.");

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var session = await _context.ExamSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.StudentId == user.Id);
        if (session == null) return BadRequest();

        var dir = Path.Combine(_env.ContentRootPath, "data", "recordings", sessionId.ToString());
        Directory.CreateDirectory(dir);

        var fileName = $"chunk_{DateTime.UtcNow:yyyyMMddHHmmssff}.webm";
        var filePath = Path.Combine(dir, fileName);

        using var fs = new FileStream(filePath, FileMode.Create);
        await chunk.CopyToAsync(fs);

        return Ok();
    }

    // ── Admin: view violations log ─────────────────────────────────────────────
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> Index(int? sessionId = null)
    {
        var query = _context.ProctorViolations
            .Include(v => v.ExamSession)
            .ThenInclude(s => s.Student)
            .Include(v => v.ExamSession)
            .ThenInclude(s => s.Exam)
            .AsQueryable();

        if (sessionId.HasValue)
            query = query.Where(v => v.ExamSessionId == sessionId.Value);

        var violations = await query
            .OrderByDescending(v => v.Timestamp)
            .Select(v => new ViolationItemViewModel
            {
                Id = v.Id,
                SessionId = v.ExamSessionId,
                StudentName = v.ExamSession.Student.FullName,
                StudentNumber = v.ExamSession.Student.StudentNumber ?? "",
                ExamTitle = v.ExamSession.Exam.Title,
                ViolationType = v.ViolationType,
                Timestamp = v.Timestamp,
                SnapshotPath = v.SnapshotPath,
                IsDisqualified = v.ExamSession.IsDisqualified
            })
            .ToListAsync();

        var disqualified = await _context.ExamSessions
            .CountAsync(s => s.IsDisqualified);

        var vm = new ViolationsViewModel
        {
            Violations = violations,
            TotalViolations = violations.Count,
            DisqualifiedCount = disqualified
        };

        return View(vm);
    }

    // ── Admin: stream snapshot image ───────────────────────────────────────────
    [Authorize(Roles = "Admin,Viewer")]
    [HttpGet]
    public IActionResult Snapshot(string path)
    {
        var safePath = Path.GetFullPath(
            Path.Combine(_env.ContentRootPath, "data", "snapshots", Path.GetFileName(path)));

        if (!System.IO.File.Exists(safePath))
            return NotFound();

        return PhysicalFile(safePath, "image/jpeg");
    }

    // ── Admin: list recordings for a session ──────────────────────────────────
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult Recordings(int sessionId)
    {
        var dir = Path.Combine(_env.ContentRootPath, "data", "recordings", sessionId.ToString());
        if (!Directory.Exists(dir))
            return Json(new List<string>());

        var files = Directory.GetFiles(dir, "*.webm")
            .Select(f => Path.GetFileName(f))
            .OrderBy(f => f)
            .ToList();

        return Json(files);
    }

    // ── Admin: stream recording chunk ─────────────────────────────────────────
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult PlayRecording(int sessionId, string filename)
    {
        var safeName = Path.GetFileName(filename);
        var filePath = Path.GetFullPath(
            Path.Combine(_env.ContentRootPath, "data", "recordings", sessionId.ToString(), safeName));

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        return PhysicalFile(filePath, "video/webm", enableRangeProcessing: true);
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private async Task<string?> SaveSnapshotAsync(string base64, int sessionId)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var dir = Path.Combine(_env.ContentRootPath, "data", "snapshots");
            Directory.CreateDirectory(dir);

            var fileName = $"{sessionId}_{DateTime.UtcNow:yyyyMMddHHmmssff}.jpg";
            var filePath = Path.Combine(dir, fileName);
            await System.IO.File.WriteAllBytesAsync(filePath, bytes);
            return fileName;
        }
        catch
        {
            return null;
        }
    }
}
