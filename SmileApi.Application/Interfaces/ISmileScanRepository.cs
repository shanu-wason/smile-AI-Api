using SmileApi.Application.DTOs;
using SmileApi.Domain.Entities;

namespace SmileApi.Application.Interfaces;

public interface ISmileScanRepository
{
    Task SaveAsync(SmileScan scan, AIUsageLog usageLog);
    Task<List<SmileScan>> GetScansAsync(string externalPatientId, Guid? userId = null);
    Task<ObservabilityStatsDto> GetObservabilityStatsAsync(Guid? userId = null);
}
