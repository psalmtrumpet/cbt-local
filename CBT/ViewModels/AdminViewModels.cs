using System.ComponentModel.DataAnnotations;
using NCS.CBT.Models;

namespace NCS.CBT.ViewModels;

public class AdminDashboardViewModel
{
    public int TotalStudents { get; set; }
    public int TotalExams { get; set; }
    public int ActiveSessions { get; set; }
    public int CompletedExams { get; set; }
    public List<RecentSessionViewModel> RecentSessions { get; set; } = new();
}

public class RecentSessionViewModel
{
    public string StudentName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Total { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class CreateExamViewModel
{
    [Required(ErrorMessage = "Title is required")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Duration is required")]
    [Range(1, 480, ErrorMessage = "Duration must be between 1 and 480 minutes")]
    [Display(Name = "Duration (minutes)")]
    public int DurationMinutes { get; set; } = 60;

    public bool IsActive { get; set; } = true;

    [Display(Name = "Scheduled Start")]
    public DateTime? ScheduledStart { get; set; }

    [Display(Name = "Scheduled End")]
    public DateTime? ScheduledEnd { get; set; }
}

public class EditExamViewModel : CreateExamViewModel
{
    public int Id { get; set; }
    public int QuestionCount { get; set; }
}

public class CreateQuestionViewModel
{
    public int ExamId { get; set; }
    public string ExamTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "Question text is required")]
    [Display(Name = "Question")]
    public string Text { get; set; } = string.Empty;

    [Display(Name = "Question Type")]
    public string QuestionType { get; set; } = "MCQ";

    // MCQ fields — only required when QuestionType == "MCQ"
    [Display(Name = "Option A")]
    public string? OptionA { get; set; }

    [Display(Name = "Option B")]
    public string? OptionB { get; set; }

    [Display(Name = "Option C")]
    public string? OptionC { get; set; }

    [Display(Name = "Option D")]
    public string? OptionD { get; set; }

    [Display(Name = "Correct Answer")]
    public string? CorrectOption { get; set; }

    // Theory field — only required when QuestionType == "Theory"
    [Display(Name = "Model Answer")]
    public string? ModelAnswer { get; set; }
}

public class EditQuestionViewModel : CreateQuestionViewModel
{
    public int Id { get; set; }
}

public class CreateStudentViewModel
{
    [Required(ErrorMessage = "Student number is required")]
    [Display(Name = "Student Number")]
    public string StudentNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Surname is required")]
    [Display(Name = "Surname")]
    public string Surname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email Address")]
    public string? Email { get; set; }

    [Display(Name = "Passport Photo")]
    public IFormFile? PassportPhoto { get; set; }
}

public class BulkPassportUploadViewModel
{
    [Required(ErrorMessage = "Please select a ZIP file")]
    [Display(Name = "ZIP file (photos named by student number, e.g. NCS001.jpg)")]
    public IFormFile? ZipFile { get; set; }
}

public class ProctorListViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateProctorViewModel
{
    [Required(ErrorMessage = "Full name is required")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    [Display(Name = "Password")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm the password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    [Display(Name = "Confirm Password")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ResetProctorPasswordViewModel
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    [Display(Name = "New Password")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;
}

public class BulkUploadViewModel
{
    [Required(ErrorMessage = "Please select an Excel file")]
    [Display(Name = "Excel File (.xlsx)")]
    public IFormFile? File { get; set; }

    [Display(Name = "Assign to Exam (optional)")]
    public int? AssignedExamId { get; set; }

    public List<NCS.CBT.Models.Exam> Exams { get; set; } = new();
}

public class BulkUploadResultItem
{
    public int Row { get; set; }
    public string StudentNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool EmailSent { get; set; }
    public string? Error { get; set; }
}

public class StudentListViewModel
{
    public string Id { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsInSession { get; set; }
    public bool HasCompletedExam { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? AssignedExamId { get; set; }
    public string? AssignedExamTitle { get; set; }
}

public class AssignExamsViewModel
{
    public List<NCS.CBT.Models.Exam> Exams { get; set; } = new();
    public List<StudentAssignmentItem> Students { get; set; } = new();
    public int? FilterExamId { get; set; }
    public string? SearchTerm { get; set; }
    public string Filter { get; set; } = "all"; // all | unassigned | exam:{id}
}

public class StudentAssignmentItem
{
    public string Id { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int? AssignedExamId { get; set; }
    public string? AssignedExamTitle { get; set; }
    public bool HasCompletedExam { get; set; }
}

public class ResultsViewModel
{
    public List<ExamResultViewModel> Results { get; set; } = new();
    public List<Exam> Exams { get; set; } = new();
    public int? SelectedExamId { get; set; }
}

public class ExamResultViewModel
{
    public int SessionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public int Score { get; set; }
    public int TotalQuestions { get; set; }
    public int MCQScore { get; set; }
    public int TheoryScore { get; set; }
    public int MCQCount { get; set; }
    public int TheoryCount { get; set; }
    public bool HasTheory => TheoryCount > 0;
    public double Percentage
    {
        get
        {
            var maxTotal = MCQCount + TheoryCount * 20;
            return maxTotal > 0 ? Math.Round((double)Score / maxTotal * 100, 1) : 0;
        }
    }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsSubmitted { get; set; }
    public bool IsDisqualified { get; set; }
    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
    public int Rank { get; set; }
}

public class SessionDetailViewModel
{
    public int SessionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public int Score { get; set; }
    public int TotalQuestions { get; set; }
    public double Percentage => TotalQuestions > 0 ? Math.Round((double)Score / TotalQuestions * 100, 1) : 0;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsDisqualified { get; set; }
    public List<AnswerDetailViewModel> Answers { get; set; } = new();
}

public class AnswerDetailViewModel
{
    public int OrderNumber { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "MCQ";
    // MCQ
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
    public string? CorrectOption { get; set; }
    public string? SelectedOption { get; set; }
    // Theory
    public string? ModelAnswer { get; set; }
    public string? TheoryAnswer { get; set; }
    public string? AIFeedback { get; set; }
    public double? AIScore { get; set; }
    public bool IsCorrect { get; set; }
}
