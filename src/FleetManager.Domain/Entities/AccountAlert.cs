using FleetManager.Domain.Common;
using FleetManager.Domain.Enums;

namespace FleetManager.Domain.Entities;

public sealed class AccountAlert : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? AcknowledgedAtUtc { get; set; }
}
