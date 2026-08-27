namespace NCS.CBT.Models;

public class StudentAnswer
{
    public int Id { get; set; }
    public int ExamSessionId { get; set; }
    public int QuestionId { get; set; }
    public string? SelectedOption { get; set; }   // MCQ: "A","B","C","D"
    public string? TheoryAnswer { get; set; }     // Theory: free text
    public bool IsCorrect { get; set; } = false;
    public double? AIScore { get; set; }          // 0.0–1.0 from Claude grading
    public string? AIFeedback { get; set; }       // Brief feedback from Claude
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
    public ExamSession ExamSession { get; set; } = null!;
    public Question Question { get; set; } = null!;
}
