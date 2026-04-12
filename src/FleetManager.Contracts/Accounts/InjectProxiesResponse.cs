namespace FleetManager.Contracts.Accounts;

public sealed class InjectProxiesResponse
{
    public int InjectedCount { get; set; }
    public int TotalProxies { get; set; }
    public int ClearedCount { get; set; }
}
