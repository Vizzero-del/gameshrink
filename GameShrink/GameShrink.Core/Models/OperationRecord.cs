namespace GameShrink.Core.Models;

public class OperationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = string.Empty;
    public CompressionMode Mode { get; set; }
    public CompressionAlgorithm Algorithm { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public long BeforeBytes { get; set; }
    public long AfterBytes { get; set; }
    public OperationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsRollback { get; set; }
    public Guid? OriginalOperationId { get; set; }
}

public enum OperationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
    RolledBack
}
