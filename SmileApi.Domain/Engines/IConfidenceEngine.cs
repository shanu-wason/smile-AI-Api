namespace SmileApi.Domain.Engines;

public interface IConfidenceEngine
{
    double CalculateConfidence(double aiConfidence, double imageQuality, double consistencyScore);
}
