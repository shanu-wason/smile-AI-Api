using SmileApi.Domain.Contracts;

namespace SmileApi.Domain.Engines;

public class FeatureConsistencyEngine : IFeatureConsistencyEngine
{
    public double CalculateConsistencyScore(ISmileAnalysisResultInput result)
    {
        double consistencyScore = 1.0;
        bool isHighPlaque = result.PlaqueRiskLevel.Equals("high", StringComparison.OrdinalIgnoreCase);

        if (isHighPlaque && result.GumHealthScore >= 85)
            consistencyScore -= 0.20;

        if (isHighPlaque && result.WhitenessScore >= 85)
            consistencyScore -= 0.15;

        if (result.AlignmentScore <= 40 && result.SymmetryScore >= 90)
            consistencyScore -= 0.15;

        if (result.AlignmentScore >= 95 &&
            result.GumHealthScore >= 95 &&
            result.SymmetryScore >= 95 &&
            result.WhitenessScore >= 95 &&
            isHighPlaque)
        {
            consistencyScore -= 0.25;
        }

        if (result.AlignmentScore <= 40 &&
            result.GumHealthScore <= 40 &&
            result.SymmetryScore <= 40 &&
            result.WhitenessScore <= 40 &&
            result.AiSelfConfidence >= 0.90)
        {
            consistencyScore -= 0.20;
        }

        return Math.Clamp(consistencyScore, 0.0, 1.0);
    }
}
