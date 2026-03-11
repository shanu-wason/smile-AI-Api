namespace SmileApi.Application.DTOs;

public class RegisterRequestDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public string? Name { get; set; }
}
