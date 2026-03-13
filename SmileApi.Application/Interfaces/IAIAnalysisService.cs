using SmileApi.Application.DTOs;

namespace SmileApi.Application.Interfaces;

public interface IAIAnalysisService
{
    Task<SmileAnalysisResultDto> AnalyzeAsync(byte[] imageBytes, string? modelSelection = null, Dictionary<string, string>? userApiKeys = null, Action<string, int>? onProgress = null);
}
