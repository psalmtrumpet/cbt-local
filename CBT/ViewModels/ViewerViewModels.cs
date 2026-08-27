namespace NCS.CBT.ViewModels;

public class ViewerDashboardViewModel
{
    public List<LiveStudentViewModel> ActiveStudents { get; set; } = new();
    public List<CompletedStudentViewModel> CompletedStudents { get; set; } = new();
    public int TotalActive { get; set; }
    public int TotalCompleted { get; set; }
}

public class LiveStudentViewModel
{
    public int SessionId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public int QuestionsAnswered { get; set; }
    public int TotalQuestions { get; set; }
    public int CurrentScore { get; set; }
    public DateTime StartTime { get; set; }
    public int RemainingSeconds { get; set; }
    public int ExamDurationMinutes { get; set; }
}

public class CompletedStudentViewModel
{
    public int SessionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public int Score { get; set; }
    public int TotalQuestions { get; set; }
    public double Percentage => TotalQuestions > 0 ? Math.Round((double)Score / TotalQuestions * 100, 1) : 0;
    public DateTime? EndTime { get; set; }
    public int ViolationCount { get; set; }
    public bool IsDisqualified { get; set; }
}

public class StudentProgressUpdate
{
    public int SessionId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public int QuestionsAnswered { get; set; }
    public int TotalQuestions { get; set; }
    public int CurrentScore { get; set; }
    public bool IsSubmitted { get; set; }
    public string ExamTitle { get; set; } = string.Empty;
    public int ViolationCount { get; set; }
    public bool IsDisqualified { get; set; }
}
