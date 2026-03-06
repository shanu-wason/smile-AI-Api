namespace SmileApi.Application.Interfaces;

/// <summary>
/// Abstracts progress notifications (e.g. SignalR). Implemented in the API/Presentation layer.
/// </summary>
public interface IScanProgressNotifier
{
    Task NotifyProgressAsync(string externalPatientId, string message, int progressPercentage, CancellationToken cancellationToken = default);
}
