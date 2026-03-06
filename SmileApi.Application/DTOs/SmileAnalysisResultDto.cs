using SmileApi.Domain.Contracts;

namespace SmileApi.Application.DTOs;

/// <summary>
/// Full AI analysis result returned by IAIAnalysisService. Implements domain contract for scoring engines.
/// </summary>
public class SmileAnalysisResultDto : ISmileAnalysisResultInput
{
    public string Reasoning { get; set; } = string.Empty;
    public bool IsDentalImage { get; set; }
    public int AlignmentScore { get; set; }
    public int GumHealthScore { get; set; }
    public int WhitenessScore { get; set; }
    public int SymmetryScore { get; set; }
    public string PlaqueRiskLevel { get; set; } = string.Empty;
    public double AiSelfConfidence { get; set; }
    public List<CarePlanActionDto> CarePlanActions { get; set; } = new();

    public int TokensUsed { get; set; }
    public long ProcessingTimeMs { get; set; }
    public string ModelUsed { get; set; } = "meta/llama-3.2-11b-vision-instruct";
}
