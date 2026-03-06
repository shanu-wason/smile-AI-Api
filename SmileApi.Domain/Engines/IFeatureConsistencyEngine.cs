using SmileApi.Domain.Contracts;

namespace SmileApi.Domain.Engines;

public interface IFeatureConsistencyEngine
{
    double CalculateConsistencyScore(ISmileAnalysisResultInput result);
}
