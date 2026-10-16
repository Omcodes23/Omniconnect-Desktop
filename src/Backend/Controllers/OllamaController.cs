using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/ollama")]
[Authorize]
public class OllamaController : ControllerBase
{
    private readonly OllamaService _ollama;

    public OllamaController(OllamaService ollama) => _ollama = ollama;

    [HttpGet("models")]
    public async Task<ActionResult<List<OllamaModel>>> GetModels()
    {
        var models = await _ollama.ListModelsAsync();
        return Ok(models);
    }

    [HttpPost("pull")]
    public async Task<ActionResult> PullModel([FromBody] PullModelRequest request)
    {
        var success = await _ollama.PullModelAsync(request.Name);
        return success ? Ok(new { status = "pulling" }) : BadRequest(new { error = "Failed to pull model" });
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<string>>> SearchModels([FromBody] SearchModelsRequest request)
    {
        var models = await _ollama.SearchLibraryAsync(request.Query ?? "");
        return Ok(models);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<OllamaGenerateResponse>> Generate([FromBody] OllamaGenerateRequest request)
    {
        var response = await _ollama.GenerateAsync(request.Model, request.Prompt);
        return Ok(new OllamaGenerateResponse { Response = response, Done = true });
    }
}

public record PullModelRequest(string Name);
public record SearchModelsRequest(string? Query);
public record OllamaGenerateRequest(string Model, string Prompt);
