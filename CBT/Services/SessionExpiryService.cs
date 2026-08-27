using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Hubs;
using NCS.CBT.Models;
using NCS.CBT.ViewModels;

namespace NCS.CBT.Services;

public class SessionExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ExamHub> _hubContext;
    private readonly ILogger<SessionExpiryService> _logger;

    public SessionExpiryService(
        IServiceScopeFactory scopeFactory,
        IHubContext<ExamHub> hubContext,
        ILogger<SessionExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            try { await CloseExpiredSessions(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "SessionExpiryService error"); }
        }
    }

    private async Task CloseExpiredSessions(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        var activeSessions = await db.ExamSessions
            .Include(s => s.Exam)
            .Include(s => s.Student)
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Where(s => s.IsActive && !s.IsSubmitted && s.Answers.Any())
            .ToListAsync(ct);

        foreach (var session in activeSessions)
        {
            var endTime = session.StartTime.AddMinutes(session.Exam.DurationMinutes);
            if (session.Exam.ScheduledEnd.HasValue && session.Exam.ScheduledEnd.Value < endTime)
                endTime = session.Exam.ScheduledEnd.Value;

            if (now < endTime) continue;

            // Session has expired — close it without AI grading (background service)
            // Theory AIScore = 0 here (not AI-graded); only MCQ correct answers count
            session.Score = session.Answers
                .Where(a => a.Question.QuestionType != "Theory")
                .Count(a => a.IsCorrect)
                + session.Answers
                .Where(a => a.Question.QuestionType == "Theory")
                .Sum(a => (int)(a.AIScore ?? 0));
            session.IsSubmitted = true;
            session.IsActive = false;
            session.EndTime = now;

            var student = session.Student;
            student.HasCompletedExam = true;
            student.IsInSession = false;
            // Do NOT call userManager.UpdateAsync here — student is already tracked
            // by the same db context; db.SaveChangesAsync below handles everything.

            _logger.LogInformation(
                "Auto-closed expired session {SessionId} for student {StudentNumber}",
                session.Id, student.StudentNumber);

            // Broadcast to proctor dashboard
            var update = new StudentProgressUpdate
            {
                SessionId      = session.Id,
                StudentId      = student.Id,
                StudentName    = student.FullName,
                StudentNumber  = student.StudentNumber ?? "",
                QuestionsAnswered = session.Answers.Count,
                TotalQuestions = session.TotalQuestions,
                CurrentScore   = session.Score,
                IsSubmitted    = true,
                ExamTitle      = session.Exam?.Title ?? "",
                ViolationCount = session.ViolationCount,
                IsDisqualified = session.IsDisqualified
            };

            await _hubContext.Clients.Group("viewers")
                .SendAsync("StudentSubmitted", update, ct);
        }

        if (activeSessions.Any(s => !s.IsActive))
            await db.SaveChangesAsync(ct);
    }
}
