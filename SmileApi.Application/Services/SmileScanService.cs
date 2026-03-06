using Microsoft.Extensions.Logging;
using SmileApi.Application.DTOs;
using SmileApi.Application.Interfaces;
using SmileApi.Domain.Entities;
using SmileApi.Domain.Engines;

namespace SmileApi.Application.Services;

public class SmileScanService : ISmileScanService
{
    private readonly IImageProcessingService _imageProcessingService;
    private readonly IAIAnalysisService _aiAnalysisService;
    private readonly ISmileScoringEngine _smileScoringEngine;
    private readonly IFeatureConsistencyEngine _featureConsistencyEngine;
    private readonly IConfidenceEngine _confidenceEngine;
    private readonly ISmileScanRepository _smileScanRepository;
    private readonly IScanProgressNotifier _progressNotifier;
    private readonly ILogger<SmileScanService> _logger;

    public SmileScanService(
        IImageProcessingService imageProcessingService,
        IAIAnalysisService aiAnalysisService,
        ISmileScoringEngine smileScoringEngine,
        IFeatureConsistencyEngine featureConsistencyEngine,
        IConfidenceEngine confidenceEngine,
        ISmileScanRepository smileScanRepository,
        IScanProgressNotifier progressNotifier,
        ILogger<SmileScanService> logger)
    {
        _imageProcessingService = imageProcessingService;
        _aiAnalysisService = aiAnalysisService;
        _smileScoringEngine = smileScoringEngine;
        _featureConsistencyEngine = featureConsistencyEngine;
        _confidenceEngine = confidenceEngine;
        _smileScanRepository = smileScanRepository;
        _progressNotifier = progressNotifier;
        _logger = logger;
    }

    public async Task<SmileScanResponseDto> CreateScanAsync(SmileScanRequestDto request, Dictionary<string, string>? userApiKeys = null)
    {
        _logger.LogInformation("Starting scan for patient {PatientId}", request.ExternalPatientId);
        await _progressNotifier.NotifyProgressAsync(request.ExternalPatientId, "Downloading and verifying image from secure vault...", 10);

        var (processedImageBytes, imageQuality) = await _imageProcessingService.ProcessImageAsync(request.ImageUrl);
        await _progressNotifier.NotifyProgressAsync(request.ExternalPatientId, "Initiating AI analysis sequence...", 30);

        var aiResult = await _aiAnalysisService.AnalyzeAsync(processedImageBytes, request.ModelSelection, userApiKeys);
        await _progressNotifier.NotifyProgressAsync(request.ExternalPatientId, "Calculating clinical smile score...", 70);

        var consistencyScore = _featureConsistencyEngine.CalculateConsistencyScore(aiResult);
        var smileScore = _smileScoringEngine.CalculateSmileScore(
            aiResult.AlignmentScore,
            aiResult.GumHealthScore,
            aiResult.SymmetryScore,
            aiResult.WhitenessScore,
            aiResult.PlaqueRiskLevel);

        await _progressNotifier.NotifyProgressAsync(request.ExternalPatientId, "Finalizing care plan recommendations...", 85);
        var finalConfidence = _confidenceEngine.CalculateConfidence(aiResult.AiSelfConfidence, imageQuality, consistencyScore);

        var scanId = Guid.NewGuid();
        var scanEntity = new SmileScan
        {
            Id = scanId,
            UserId = request.UserId,
            ExternalPatientId = request.ExternalPatientId,
            ImageUrl = request.ImageUrl,
            SmileScore = smileScore,
            AlignmentScore = aiResult.AlignmentScore,
            GumHealthScore = aiResult.GumHealthScore,
            WhitenessScore = aiResult.WhitenessScore,
            SymmetryScore = aiResult.SymmetryScore,
            PlaqueRiskLevel = aiResult.PlaqueRiskLevel,
            ConfidenceScore = finalConfidence,
            CarePlanActionsJson = System.Text.Json.JsonSerializer.Serialize(aiResult.CarePlanActions),
            CreatedAt = DateTime.UtcNow
        };

        decimal costPerThousandTokens = aiResult.ModelUsed.ToLowerInvariant() switch
        {
            "openai/gpt-4o-mini" => 0.0004m,
            "gpt-4o-mini" => 0.0004m,
            "gpt-4o" => 0.005m,
            "claude-3-5-sonnet-20241022" => 0.003m,
            "gemini-1.5-pro" => 0.00125m,
            _ => 0.0m
        };
        decimal costEstimate = Math.Round(aiResult.TokensUsed * costPerThousandTokens / 1000m, 6);

        var usageLog = new AIUsageLog
        {
            Id = Guid.NewGuid(),
            ScanId = scanId,
            UserId = request.UserId,
            ExternalPatientId = request.ExternalPatientId,
            ModelUsed = aiResult.ModelUsed,
            TokensUsed = aiResult.TokensUsed,
            ProcessingTimeMs = aiResult.ProcessingTimeMs,
            CostEstimate = costEstimate,
            CreatedAt = DateTime.UtcNow
        };

        await _progressNotifier.NotifyProgressAsync(request.ExternalPatientId, "Saving encrypted results to database...", 95);
        await _smileScanRepository.SaveAsync(scanEntity, usageLog);

        return new SmileScanResponseDto
        {
            ScanId = scanEntity.Id,
            ExternalPatientId = scanEntity.ExternalPatientId,
            ImageUrl = scanEntity.ImageUrl,
            SmileScore = scanEntity.SmileScore,
            Breakdown = new BreakdownDto
            {
                AlignmentScore = scanEntity.AlignmentScore,
                GumHealthScore = scanEntity.GumHealthScore,
                WhitenessScore = scanEntity.WhitenessScore,
                SymmetryScore = scanEntity.SymmetryScore,
                PlaqueRiskLevel = scanEntity.PlaqueRiskLevel
            },
            ConfidenceScore = scanEntity.ConfidenceScore,
            ImageQualityScore = imageQuality,
            CarePlanActions = new List<CarePlanActionDto>(aiResult.CarePlanActions),
            CreatedAt = scanEntity.CreatedAt
        };
    }

    public async Task<List<SmileScanResponseDto>> GetScansAsync(string externalPatientId, Guid? userId = null)
    {
        var scans = await _smileScanRepository.GetScansAsync(externalPatientId, userId);
        var response = new List<SmileScanResponseDto>();

        foreach (var scan in scans)
        {
            response.Add(new SmileScanResponseDto
            {
                ScanId = scan.Id,
                ExternalPatientId = scan.ExternalPatientId,
                ImageUrl = scan.ImageUrl,
                SmileScore = scan.SmileScore,
                Breakdown = new BreakdownDto
                {
                    AlignmentScore = scan.AlignmentScore,
                    GumHealthScore = scan.GumHealthScore,
                    WhitenessScore = scan.WhitenessScore,
                    SymmetryScore = scan.SymmetryScore,
                    PlaqueRiskLevel = scan.PlaqueRiskLevel
                },
                ConfidenceScore = scan.ConfidenceScore,
                ImageQualityScore = 1.0,
                CarePlanActions = string.IsNullOrEmpty(scan.CarePlanActionsJson)
                    ? new List<CarePlanActionDto>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<CarePlanActionDto>>(scan.CarePlanActionsJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<CarePlanActionDto>(),
                CreatedAt = scan.CreatedAt
            });
        }

        return response;
    }
}
