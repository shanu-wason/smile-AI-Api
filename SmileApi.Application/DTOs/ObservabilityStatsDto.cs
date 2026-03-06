namespace SmileApi.Application.DTOs;

public class ObservabilityStatsDto
{
    public int TotalScans { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public long TotalTokensUsed { get; set; }
    public double AverageTokensPerScan { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AverageCostPerScan { get; set; }
    public int FailedScans { get; set; }
    public double FailureRate { get; set; }
}
