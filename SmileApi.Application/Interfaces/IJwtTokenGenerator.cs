namespace SmileApi.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid userId, string email, string? name, out DateTime expiresAt);
}
