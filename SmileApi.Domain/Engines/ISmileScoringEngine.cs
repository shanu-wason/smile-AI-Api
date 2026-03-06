namespace SmileApi.Domain.Engines;

public interface ISmileScoringEngine
{
    int CalculateSmileScore(int alignment, int gumHealth, int symmetry, int whiteness, string plaqueRisk);
}
