using Microsoft.AspNetCore.SignalR;
using SmileApi.Application.Interfaces;
using smile_api.Hubs;

namespace smile_api.Infrastructure;

public class SignalRScanProgressNotifier : IScanProgressNotifier
{
    private readonly IHubContext<ScanLogicHub> _hubContext;

    public SignalRScanProgressNotifier(IHubContext<ScanLogicHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyProgressAsync(string externalPatientId, string message, int progressPercentage, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group(externalPatientId).SendAsync("ReceiveProgress", message, progressPercentage, cancellationToken);
    }
}
