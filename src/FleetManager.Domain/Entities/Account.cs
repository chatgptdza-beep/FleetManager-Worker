using FleetManager.Domain.Common;
using FleetManager.Domain.Enums;

namespace FleetManager.Domain.Entities;

public sealed class Account : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public AccountStatus Status { get; set; } = AccountStatus.Running;
    public Guid VpsNodeId { get; set; }
    public VpsNode? VpsNode { get; set; }
    public string CurrentStageCode { get; set; } = string.Empty;
    public string CurrentStageName { get; set; } = string.Empty;
    public DateTime? LastStageTransitionAtUtc { get; set; }

    public ICollection<AccountWorkflowStage> WorkflowStages { get; set; } = new List<AccountWorkflowStage>();
    public ICollection<AccountAlert> Alerts { get; set; } = new List<AccountAlert>();
    
    // Proxy Management
    public int CurrentProxyIndex { get; set; } = 0;
    public ICollection<ProxyEntry> Proxies { get; set; } = new List<ProxyEntry>();
}
