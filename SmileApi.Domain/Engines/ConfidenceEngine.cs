namespace SmileApi.Domain.Engines;

public class ConfidenceEngine : IConfidenceEngine
{
    public double CalculateConfidence(double aiConfidence, double imageQuality, double consistencyScore)
    {
        double cappedAiConfidence = Math.Min(aiConfidence, 0.95);
        double confidenceScore =
            (0.40 * cappedAiConfidence) +
            (0.35 * imageQuality) +
            (0.25 * consistencyScore);
        return Math.Clamp(confidenceScore, 0.0, 1.0);
    }
}
