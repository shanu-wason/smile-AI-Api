namespace SmileApi.Domain.Engines;

public class SmileScoringEngine : ISmileScoringEngine
{
    public int CalculateSmileScore(int alignment, int gumHealth, int symmetry, int whiteness, string plaqueRisk)
    {
        double baseScore =
            (alignment * 0.40) +
            (gumHealth * 0.35) +
            (whiteness * 0.15) +
            (symmetry * 0.10);

        double plaqueMultiplier = plaqueRisk.ToLowerInvariant() switch
        {
            "low" => 1.0,
            "medium" => 0.85,
            "high" => 0.70,
            _ => 1.0
        };

        double finalScore = baseScore * plaqueMultiplier;
        return (int)Math.Clamp(Math.Round(finalScore, MidpointRounding.AwayFromZero), 0, 100);
    }
}
