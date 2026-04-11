using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace FleetManager.Desktop.Services;

public sealed class SignalRService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    
    public event Action<Guid, int>? OnProxyRotated;
    public event Action<Guid, string>? OnManualRequired;
    public event Action<Guid, string>? OnBotStatusChanged;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string backendUrl, string jwtToken)
    {
        if (_hubConnection is not null)
        {
            await DisconnectAsync();
        }

        var hubUrl = backendUrl.TrimEnd('/') + "/hubs/operations";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(jwtToken)!;
            })
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        _hubConnection.On<Guid, int>("SendProxyRotatedEvent", (accountId, newIndex) =>
        {
            OnProxyRotated?.Invoke(accountId, newIndex);
        });

        _hubConnection.On<Guid, string>("SendManualRequiredEvent", (accountId, vncUrl) =>
        {
            OnManualRequired?.Invoke(accountId, vncUrl);
        });

        _hubConnection.On<Guid, string>("SendBotStatusChanged", (accountId, status) =>
        {
            OnBotStatusChanged?.Invoke(accountId, status);
        });

        try
        {
            await _hubConnection.StartAsync();
        }
        catch
        {
            // SignalR is optional — silently fail if hub is unavailable
        }
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection is not null)
        {
            try
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
            }
            catch { /* best-effort cleanup */ }
            finally
            {
                _hubConnection = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
