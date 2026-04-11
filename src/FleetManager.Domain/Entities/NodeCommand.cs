using FleetManager.Domain.Common;
using FleetManager.Domain.Enums;

namespace FleetManager.Domain.Entities;

public sealed class NodeCommand : BaseEntity
{
    public Guid VpsNodeId { get; set; }
    public VpsNode? VpsNode { get; set; }
    public NodeCommandType CommandType { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public CommandStatus Status { get; set; } = CommandStatus.Pending;
    public string? ResultMessage { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
}
