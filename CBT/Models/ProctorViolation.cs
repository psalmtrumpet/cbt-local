namespace NCS.CBT.Models;

public class ProctorViolation
{
    public int Id { get; set; }
    public int ExamSessionId { get; set; }
    public string ViolationType { get; set; } = string.Empty; // NO_FACE | MULTIPLE_FACES | LOOKING_AWAY | LIP_MOVEMENT | TAB_SWITCH
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? SnapshotPath { get; set; }
    public ExamSession ExamSession { get; set; } = null!;
}
