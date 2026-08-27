using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NCS.CBT.Data;
using NCS.CBT.Models;

namespace NCS.CBT.Services;

public class SessionExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionExpiryService> _logger;

    public SessionExpiryService(
        IServiceScopeFactory scopeFactory,
        ILogger<SessionExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
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
        }

        if (activeSessions.Any(s => !s.IsActive))
            await db.SaveChangesAsync(ct);
    }
}
