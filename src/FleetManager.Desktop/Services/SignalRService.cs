using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace FleetManager.Desktop.Services;

public class SignalRService
{
    private HubConnection? _hubConnection;
    
    public event Action<Guid, int>? OnProxyRotated;
    public event Action<Guid, string>? OnManualRequired;
    public event Action<Guid, string>? OnBotStatusChanged;

    public async Task ConnectAsync(string backendUrl, string jwtToken)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{backendUrl}/hubs/operations", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(jwtToken)!;
            })
            .WithAutomaticReconnect()
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to SignalR: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
        }
    }
}
