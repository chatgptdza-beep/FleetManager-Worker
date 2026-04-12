namespace FleetManager.Contracts.Operations;

public sealed class WorkerInboxEventResponse
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public Guid? AccountId { get; set; }
    public string? AccountEmail { get; set; }
    public Guid? NodeId { get; set; }
    public string? NodeName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
}
