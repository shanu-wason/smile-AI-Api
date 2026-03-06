using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using SmileApi.Application.DTOs;
using SmileApi.Application.Interfaces;

namespace smile_api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ObservabilityController : ControllerBase
{
    private readonly ISmileScanRepository _repository;

    public ObservabilityController(ISmileScanRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ObservabilityStatsDto>> GetStats()
    {
        Guid? userId = null;
        var nameIdentifier = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(nameIdentifier) && Guid.TryParse(nameIdentifier, out var parsedUserId))
            userId = parsedUserId;

        var stats = await _repository.GetObservabilityStatsAsync(userId);
        return Ok(stats);
    }
}
