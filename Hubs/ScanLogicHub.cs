using Microsoft.AspNetCore.SignalR;

namespace smile_api.Hubs;

public class ScanLogicHub : Hub
{
    public async Task SendProgressUpdate(string externalPatientId, string statusMessage, int progressPercentage)
    {
        await Clients.Group(externalPatientId).SendAsync("ReceiveProgress", statusMessage, progressPercentage);
    }

    public async Task SubscribeToPatient(string externalPatientId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, externalPatientId);
    }
}
