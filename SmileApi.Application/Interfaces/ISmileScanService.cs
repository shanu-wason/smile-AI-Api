using SmileApi.Application.DTOs;

namespace SmileApi.Application.Interfaces;

public interface ISmileScanService
{
    Task<SmileScanResponseDto> CreateScanAsync(SmileScanRequestDto request, Dictionary<string, string>? userApiKeys = null);
    Task<List<SmileScanResponseDto>> GetScansAsync(string externalPatientId, Guid? userId = null);
}
