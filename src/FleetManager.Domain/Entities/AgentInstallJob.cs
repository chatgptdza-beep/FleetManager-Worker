using FleetManager.Domain.Common;
using FleetManager.Domain.Enums;

namespace FleetManager.Domain.Entities;

public sealed class AgentInstallJob : BaseEntity
{
    public Guid VpsNodeId { get; set; }
    public VpsNode? VpsNode { get; set; }
    public InstallJobStatus JobStatus { get; set; } = InstallJobStatus.Pending;
    public string CurrentStep { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}
