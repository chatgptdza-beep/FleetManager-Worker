using System;
using FleetManager.Domain.Common;

namespace FleetManager.Domain.Entities;

public sealed class ProxyRotationLog : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    public int FromOrder { get; set; }
    public int ToOrder { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RotatedAtUtc { get; set; }
}
