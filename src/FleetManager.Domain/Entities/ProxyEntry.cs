using System;
using FleetManager.Domain.Common;

namespace FleetManager.Domain.Entities;

public sealed class ProxyEntry : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    
    public int Order { get; set; }
    public int FailCount { get; set; }
    public bool IsBlacklisted { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public override string ToString()
    {
        return $"Proxy({Host}:{Port})";
    }
}
