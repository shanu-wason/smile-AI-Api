namespace SmileApi.Domain.Contracts;

/// <summary>
/// Contract for AI analysis result used by domain engines (e.g. FeatureConsistencyEngine).
/// Implemented by Application DTOs so Domain does not depend on Application or Infrastructure.
/// </summary>
public interface ISmileAnalysisResultInput
{
    int AlignmentScore { get; }
    int GumHealthScore { get; }
    int WhitenessScore { get; }
    int SymmetryScore { get; }
    string PlaqueRiskLevel { get; }
    double AiSelfConfidence { get; }
}
