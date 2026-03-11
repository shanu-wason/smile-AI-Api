using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using SmileApi.Application.DTOs;
using SmileApi.Application.Interfaces;
using SmileApi.Application.Validators;

namespace smile_api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class SmileScanController : ControllerBase
{
    private readonly ISmileScanService _smileScanService;
    private readonly IUserRepository _userRepository;

    public SmileScanController(ISmileScanService smileScanService, IUserRepository userRepository)
    {
        _smileScanService = smileScanService;
        _userRepository = userRepository;
    }

    [HttpPost]
    public async Task<ActionResult<SmileScanResponseDto>> CreateScan([FromBody] SmileScanRequestDto request)
    {
        var (patientValid, sanitizedPatientId, patientError) = InputSanitizer.SanitizePatientId(request.ExternalPatientId);
        if (!patientValid)
            return BadRequest(patientError);

        var (imageValid, sanitizedImageUrl, imageError) = InputSanitizer.SanitizeImageUrl(request.ImageUrl);
        if (!imageValid)
            return BadRequest(imageError);

        request.ExternalPatientId = sanitizedPatientId;
        request.ImageUrl = sanitizedImageUrl;

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            var userExists = await _userRepository.GetByIdAsync(userId) != null;
            request.UserId = userExists ? userId : null;
        }

        Dictionary<string, string>? userApiKeys = null;
        var openRouterKey = Request.Headers["X-OpenRouter-Key"].FirstOrDefault();
        var nvidiaKey = Request.Headers["X-NVIDIA-Key"].FirstOrDefault();
        var openAiKey = Request.Headers["X-OpenAI-Key"].FirstOrDefault();
        var anthropicKey = Request.Headers["X-Anthropic-Key"].FirstOrDefault();
        var googleKey = Request.Headers["X-Google-Key"].FirstOrDefault();
        var allKeys = new[] {
            ("openrouter", openRouterKey), ("nvidia", nvidiaKey),
            ("openai", openAiKey), ("anthropic", anthropicKey), ("google", googleKey)
        };
        if (allKeys.Any(k => !string.IsNullOrEmpty(k.Item2)))
            userApiKeys = allKeys.Where(k => !string.IsNullOrEmpty(k.Item2)).ToDictionary(k => k.Item1, k => k.Item2!);

        var result = await _smileScanService.CreateScanAsync(request, userApiKeys);
        return Ok(result);
    }

    [HttpGet("{externalPatientId}")]
    public async Task<ActionResult<List<SmileScanResponseDto>>> GetScans(string externalPatientId)
    {
        Guid? userId = null;
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var parsedId))
            userId = parsedId;

        var (isValid, sanitizedId, error) = InputSanitizer.SanitizePatientId(externalPatientId);
        if (!isValid)
            return BadRequest(error);

        var result = await _smileScanService.GetScansAsync(sanitizedId, userId);
        return Ok(result);
    }
}
