namespace SmileApi.Application.DTOs;

public class SmileScanResponseDto
{
    public Guid ScanId { get; set; }
    public string ExternalPatientId { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int SmileScore { get; set; }
    public BreakdownDto Breakdown { get; set; } = new();
    public double ConfidenceScore { get; set; }
    public double ImageQualityScore { get; set; }
    public List<CarePlanActionDto> CarePlanActions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
