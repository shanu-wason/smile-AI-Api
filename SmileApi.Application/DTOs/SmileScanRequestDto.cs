using System.ComponentModel.DataAnnotations;

namespace SmileApi.Application.DTOs;

public class SmileScanRequestDto
{
    [Required]
    public string ExternalPatientId { get; set; } = string.Empty;

    [Required]
    public string ImageUrl { get; set; } = string.Empty;

    public string ModelSelection { get; set; } = "gpt4o";

    public Guid? UserId { get; set; }
}
