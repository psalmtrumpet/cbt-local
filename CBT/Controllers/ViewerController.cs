using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Models;
using NCS.CBT.ViewModels;

namespace NCS.CBT.Controllers;

[Authorize(Roles = "Viewer,Admin")]
public class ViewerController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ViewerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<IActionResult> Dashboard()
    {
        var activeSessions = await _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Include(s => s.Answers)
            .Where(s => s.IsActive && !s.IsSubmitted)
            .ToListAsync();

        var completedSessions = await _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Where(s => s.IsSubmitted)
            .OrderByDescending(s => s.EndTime)
            .Take(50)
            .ToListAsync();


        var liveStudents = activeSessions.Select(s =>
        {
            var endTime = s.StartTime.AddMinutes(s.Exam.DurationMinutes);
            if (s.Exam.ScheduledEnd.HasValue && s.Exam.ScheduledEnd.Value < endTime)
                endTime = s.Exam.ScheduledEnd.Value;
            return new LiveStudentViewModel
            {
                SessionId = s.Id,
                StudentId = s.StudentId,
                StudentName = s.Student.FullName,
                StudentNumber = s.Student.StudentNumber ?? "",
                ExamTitle = s.Exam.Title,
                QuestionsAnswered = s.Answers.Count,
                TotalQuestions = s.TotalQuestions,
                CurrentScore = s.Score,
                StartTime = s.StartTime,
                ExamDurationMinutes = s.Exam.DurationMinutes,
                RemainingSeconds = Math.Max(0, (int)(endTime - DateTime.UtcNow).TotalSeconds)
            };
        }).ToList();

        var completedStudents = completedSessions.Select(s => new CompletedStudentViewModel
        {
            SessionId = s.Id,
            StudentName = s.Student.FullName,
            StudentNumber = s.Student.StudentNumber ?? "",
            ExamTitle = s.Exam.Title,
            Score = s.Score,
            TotalQuestions = s.TotalQuestions,
            EndTime = s.EndTime,
            ViolationCount = s.ViolationCount,
            IsDisqualified = s.IsDisqualified
        }).ToList();

        var vm = new ViewerDashboardViewModel
        {
            ActiveStudents = liveStudents,
            CompletedStudents = completedStudents,
            TotalActive = liveStudents.Count,
            TotalCompleted = completedStudents.Count
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> SessionViolations(int sessionId)
    {
        var session = await _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Where(s => s.Id == sessionId)
            .Select(s => new
            {
                studentName = s.Student.FullName,
                studentNumber = s.Student.StudentNumber ?? "",
                examTitle = s.Exam.Title,
                isDisqualified = s.IsDisqualified,
                score = s.Score,
                totalQuestions = s.TotalQuestions,
                violationCount = s.ViolationCount
            })
            .FirstOrDefaultAsync();

        if (session == null) return NotFound();

        var rawViolations = await _context.ProctorViolations
            .Where(v => v.ExamSessionId == sessionId)
            .OrderBy(v => v.Timestamp)
            .ToListAsync();

        var violations = rawViolations.Select(v => new
        {
            violationType = v.ViolationType,
            timestamp = v.Timestamp.ToString("HH:mm:ss"),
            snapshotPath = v.SnapshotPath
        });

        return Json(new { session, violations });
    }

    [HttpGet]
    public async Task<IActionResult> LiveData()
    {
        var activeSessions = await _context.ExamSessions
            .Include(s => s.Student)
            .Include(s => s.Exam)
            .Include(s => s.Answers)
            .Where(s => s.IsActive && !s.IsSubmitted)
            .ToListAsync();

        var liveStudents = activeSessions.Select(s =>
        {
            var endTime = s.StartTime.AddMinutes(s.Exam.DurationMinutes);
            if (s.Exam.ScheduledEnd.HasValue && s.Exam.ScheduledEnd.Value < endTime)
                endTime = s.Exam.ScheduledEnd.Value;
            return new
            {
                sessionId = s.Id,
                studentId = s.StudentId,
                studentName = s.Student.FullName,
                studentNumber = s.Student.StudentNumber ?? "",
                examTitle = s.Exam.Title,
                questionsAnswered = s.Answers.Count,
                totalQuestions = s.TotalQuestions,
                currentScore = s.Score,
                remainingSeconds = Math.Max(0, (int)(endTime - DateTime.UtcNow).TotalSeconds),
                isSubmitted = false
            };
        }).ToList();

        return Json(liveStudents);
    }
}
