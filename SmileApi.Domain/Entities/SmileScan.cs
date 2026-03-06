namespace SmileApi.Domain.Entities;

public class SmileScan
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public required string ExternalPatientId { get; set; }
    public required string ImageUrl { get; set; }
    public int SmileScore { get; set; }
    public int AlignmentScore { get; set; }
    public int GumHealthScore { get; set; }
    public int WhitenessScore { get; set; }
    public int SymmetryScore { get; set; }
    public required string PlaqueRiskLevel { get; set; }
    public double ConfidenceScore { get; set; }
    public string CarePlanActionsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
}
