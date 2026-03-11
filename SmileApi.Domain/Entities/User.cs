namespace SmileApi.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
