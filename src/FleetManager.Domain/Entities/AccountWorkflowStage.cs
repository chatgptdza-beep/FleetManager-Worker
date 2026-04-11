using FleetManager.Domain.Common;
using FleetManager.Domain.Enums;

namespace FleetManager.Domain.Entities;

public sealed class AccountWorkflowStage : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public int DisplayOrder { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public WorkflowStageState State { get; set; } = WorkflowStageState.Pending;
    public string Message { get; set; } = string.Empty;
    public DateTime? OccurredAtUtc { get; set; }
}
