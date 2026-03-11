using Microsoft.Extensions.Logging;
using SmileApi.Application.DTOs;
using SmileApi.Application.Interfaces;
using SmileApi.Domain.Entities;

namespace SmileApi.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IJwtTokenGenerator jwtTokenGenerator,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
        _logger = logger;
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var existing = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning("Registration failed: email already exists {Email}", email);
            return null;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BCrypt.Net.BCrypt.GenerateSalt(12)),
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.CreateAsync(user, cancellationToken);
        _logger.LogInformation("User registered: {Email}", user.Email);

        var token = _jwtTokenGenerator.GenerateToken(user.Id, user.Email, user.Name, out var expiresAt);
        return new AuthResponseDto
        {
            Token = token,
            Email = user.Email,
            UserId = user.Id,
            Name = user.Name,
            ExpiresAt = expiresAt
        };
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Login failed: user not found {Email}", email);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: invalid password for {Email}", email);
            return null;
        }

        var token = _jwtTokenGenerator.GenerateToken(user.Id, user.Email, user.Name, out var expiresAt);
        return new AuthResponseDto
        {
            Token = token,
            Email = user.Email,
            UserId = user.Id,
            Name = user.Name,
            ExpiresAt = expiresAt
        };
    }
}
