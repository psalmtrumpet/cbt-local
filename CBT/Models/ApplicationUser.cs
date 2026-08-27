using Microsoft.AspNetCore.Identity;

namespace NCS.CBT.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? StudentNumber { get; set; }
    public string? Surname { get; set; }
    public bool HasCompletedExam { get; set; } = false;
    public bool IsInSession { get; set; } = false;
    public DateTime? SessionStartTime { get; set; }
    public string? AccessCodeHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? AssignedExamId { get; set; }
    public Exam? AssignedExam { get; set; }
    public ICollection<ExamSession> ExamSessions { get; set; } = new List<ExamSession>();
}
