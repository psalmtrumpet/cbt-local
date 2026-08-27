namespace NCS.CBT.Models;

public class Question
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public string QuestionType { get; set; } = "MCQ"; // "MCQ" or "Theory"
    public string Text { get; set; } = string.Empty;
    // MCQ fields (null for Theory questions)
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
    public string? CorrectOption { get; set; } // "A", "B", "C", "D" — null for Theory
    // Theory fields (null for MCQ questions)
    public string? ModelAnswer { get; set; }
    public int OrderNumber { get; set; }
    public Exam Exam { get; set; } = null!;
    public ICollection<StudentAnswer> StudentAnswers { get; set; } = new List<StudentAnswer>();
}
