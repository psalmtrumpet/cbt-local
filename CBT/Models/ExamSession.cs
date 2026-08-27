namespace NCS.CBT.Models;

public class ExamSession
{
    public int Id { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public int ExamId { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public bool IsSubmitted { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public int Score { get; set; } = 0;
    public int TotalQuestions { get; set; } = 0;
    public ApplicationUser Student { get; set; } = null!;
    public Exam Exam { get; set; } = null!;
    public ICollection<StudentAnswer> Answers { get; set; } = new List<StudentAnswer>();
}
