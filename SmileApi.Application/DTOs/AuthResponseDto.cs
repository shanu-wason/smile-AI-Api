namespace SmileApi.Application.DTOs;

public class AuthResponseDto
{
    public required string Token { get; set; }
    public required string Email { get; set; }
    public Guid UserId { get; set; }
    public string? Name { get; set; }
    public DateTime ExpiresAt { get; set; }
}
