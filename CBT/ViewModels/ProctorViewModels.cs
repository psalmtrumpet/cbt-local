namespace NCS.CBT.ViewModels;

public class ViolationLogRequest
{
    public int SessionId { get; set; }
    public string ViolationType { get; set; } = string.Empty;
    public string? SnapshotBase64 { get; set; }
}

public class DisqualifyRequest
{
    public int SessionId { get; set; }
}

public class ViolationLogResponse
{
    public bool Success { get; set; }
    public string Action { get; set; } = string.Empty; // "warn" | "disqualify" | "ok"
    public int ViolationCount { get; set; }
    public string? RedirectUrl { get; set; }
}

public class ViolationsViewModel
{
    public List<ViolationItemViewModel> Violations { get; set; } = new();
    public int TotalViolations { get; set; }
    public int DisqualifiedCount { get; set; }
}

public class ViolationItemViewModel
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public string ViolationType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? SnapshotPath { get; set; }
    public bool IsDisqualified { get; set; }
}
