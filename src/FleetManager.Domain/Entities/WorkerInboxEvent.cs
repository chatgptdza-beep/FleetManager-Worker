using FleetManager.Domain.Common;
using FleetManager.Domain.Enums;

namespace FleetManager.Domain.Entities;

public sealed class WorkerInboxEvent : BaseEntity
{
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? VpsNodeId { get; set; }
    public VpsNode? VpsNode { get; set; }
    public WorkerInboxEventType EventType { get; set; } = WorkerInboxEventType.CommandFailed;
    public WorkerInboxEventStatus Status { get; set; } = WorkerInboxEventStatus.Pending;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
}
