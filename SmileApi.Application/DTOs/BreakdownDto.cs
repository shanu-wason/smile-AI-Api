namespace SmileApi.Application.DTOs;

public class BreakdownDto
{
    public int AlignmentScore { get; set; }
    public int GumHealthScore { get; set; }
    public int WhitenessScore { get; set; }
    public int SymmetryScore { get; set; }
    public string PlaqueRiskLevel { get; set; } = string.Empty;
}
