namespace NCS.CBT.Models;

public class Exam
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedById { get; set; } = string.Empty;

    // Optional scheduled window — students can only start within this window;
    // exam timer is capped at ScheduledEnd regardless of DurationMinutes
    public DateTime? ScheduledStart { get; set; }
    public DateTime? ScheduledEnd { get; set; }

    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<ExamSession> ExamSessions { get; set; } = new List<ExamSession>();
}
