namespace SmileApi.Domain.Entities;

public class AIUsageLog
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public Guid? UserId { get; set; }
    public string ExternalPatientId { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public long ProcessingTimeMs { get; set; }
    public decimal CostEstimate { get; set; }
    public DateTime CreatedAt { get; set; }
}
