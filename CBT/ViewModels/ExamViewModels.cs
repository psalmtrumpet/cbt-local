namespace NCS.CBT.ViewModels;

public class ExamViewModel
{
    public int SessionId { get; set; }
    public int ExamId { get; set; }
    public string ExamTitle { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public int CurrentIndex { get; set; }
    public long EndTimeMs { get; set; }
    public int RemainingSeconds { get; set; } // server-computed; used as the authoritative countdown start
    public QuestionViewModel CurrentQuestion { get; set; } = null!;
    public List<QuestionNavItem> QuestionNavItems { get; set; } = new();
    public string? CurrentAnswer { get; set; }      // MCQ selected option
    public string? CurrentTheoryAnswer { get; set; } // Theory text answer
}

public class QuestionNavItem
{
    public int Index { get; set; }
    public int QuestionId { get; set; }
    public bool IsAnswered { get; set; }
}

public class QuestionViewModel
{
    public int Id { get; set; }
    public string QuestionType { get; set; } = "MCQ";
    public string Text { get; set; } = string.Empty;
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
    public int OrderNumber { get; set; }
}

public class SaveAnswerRequest
{
    public int SessionId { get; set; }
    public int QuestionId { get; set; }
    public string? SelectedOption { get; set; }   // MCQ
    public string? TheoryAnswer { get; set; }     // Theory
}

public class SubmitExamRequest
{
    public int SessionId { get; set; }
}
