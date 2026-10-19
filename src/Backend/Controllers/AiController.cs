using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AiService _aiService;
    private readonly ModelManager _models;
    private readonly LocalInferenceEngine _engine;
    private readonly IAiBotState _botState;

    public AiController(
        AiService aiService,
        ModelManager models,
        LocalInferenceEngine engine,
        IAiBotState botState)
    {
        _aiService = aiService;
        _models = models;
        _engine = engine;
        _botState = botState;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Send a chat message and receive a response from the on-device model.
    /// This is fully local: no remote provider, no local server.
    /// </summary>
    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
        var result = await _aiService.ChatAsync(GetUserId(), request, ct);
        return Ok(result);
    }

    // ── Local model management ───────────────────────────────────────────
    // Progress is surfaced by polling this list while a download runs.

    /// <summary>List the curated local models and their download state.</summary>
    [HttpGet("models")]
    public async Task<ActionResult<AiModelListResponse>> GetModels()
        => Ok(await _models.ListAsync());

    /// <summary>
    /// Start (or resume) downloading a model. Idempotent; returns immediately
    /// while the download continues in the background.
    /// </summary>
    [HttpPost("models/download")]
    public ActionResult<AiModelDownloadResponse> DownloadModel([FromBody] AiModelDownloadRequest request)
    {
        try
        {
            return Ok(_models.StartDownload(request.ModelId));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Select which downloaded model subsequent requests should use.</summary>
    [HttpPost("models/select")]
    public ActionResult SelectModel([FromBody] AiModelDownloadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            return BadRequest(new { error = "A model id is required." });
        }

        if (!_models.SetActiveModel(request.ModelId))
        {
            return NotFound(new { error = $"Unknown model '{request.ModelId}'." });
        }

        return Ok(new { activeModelId = _models.ActiveModelId });
    }

    /// <summary>Delete a downloaded model file.</summary>
    [HttpDelete("models/{modelId}")]
    public async Task<ActionResult> DeleteModel(string modelId)
    {
        // A memory-mapped model file cannot be deleted on Windows, so release
        // the runtime handle first when the target is the loaded model.
        var entry = ModelCatalog.Find(modelId);
        if (entry is not null && string.Equals(_engine.LoadedModelPath, _models.PathFor(entry), StringComparison.OrdinalIgnoreCase))
        {
            _engine.Unload();
        }

        var deleted = await _models.DeleteAsync(modelId);
        return deleted ? NoContent() : NotFound();
    }

    // ── Companion mode (AI as a chat node) ───────────────────────────────

    /// <summary>Current companion-mode state of the AI chat node.</summary>
    [HttpGet("bot/status")]
    public ActionResult<AiBotStatusResponse> GetBotStatus()
        => Ok(BuildBotStatus());

    /// <summary>Enable or disable companion mode, optionally selecting a model.</summary>
    [HttpPost("bot/toggle")]
    public ActionResult<AiBotStatusResponse> ToggleBot([FromBody] AiBotToggleRequest request)
    {
        if (request.Enabled && !string.IsNullOrWhiteSpace(request.ModelId))
        {
            if (!_models.SetActiveModel(request.ModelId!))
            {
                return BadRequest(new { error = $"Unknown model '{request.ModelId}'." });
            }
        }

        _botState.IsEnabled = request.Enabled;
        _botState.ModelId = string.IsNullOrWhiteSpace(request.ModelId) ? _botState.ModelId : request.ModelId;

        if (request.Enabled)
        {
            if (!string.IsNullOrWhiteSpace(request.DeviceId))
                _botState.OwnerDeviceId = request.DeviceId;
            if (!string.IsNullOrWhiteSpace(request.DeviceName))
                _botState.OwnerDeviceName = request.DeviceName;
        }
        else
        {
            _botState.OwnerDeviceId = null;
            _botState.OwnerDeviceName = null;
        }

        return Ok(BuildBotStatus());
    }

    private AiBotStatusResponse BuildBotStatus()
    {
        var modelId = _botState.ModelId ?? _models.ActiveModelId;
        var ready = _models.GetModelPath(modelId) is not null;

        var status = !_botState.IsEnabled
            ? "offline"
            : ready ? "online" : "no-model";

        var modelName = ModelCatalog.Find(modelId)?.DisplayName;

        return new AiBotStatusResponse(
            _botState.IsEnabled,
            modelId,
            AiBotService.DisplayName,
            status,
            modelName,
            _botState.OwnerDeviceId,
            _botState.OwnerDeviceName);
    }

    /// <summary>
    /// Translate text from one language to another.
    /// </summary>
    [HttpPost("translate")]
    public async Task<ActionResult<AiTranslateResponse>> Translate([FromBody] AiTranslateRequest request, CancellationToken ct)
    {
        var result = await _aiService.TranslateAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Summarize a block of text.
    /// </summary>
    [HttpPost("summarize")]
    public async Task<ActionResult<AiSummarizeResponse>> Summarize([FromBody] AiSummarizeRequest request, CancellationToken ct)
    {
        var result = await _aiService.SummarizeAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Perform OCR on a base64-encoded image.
    /// </summary>
    [HttpPost("ocr")]
    public async Task<ActionResult<AiOcrResponse>> Ocr([FromBody] AiOcrRequest request)
    {
        var result = await _aiService.PerformOcrAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Generate smart reply suggestions based on conversation context.
    /// </summary>
    [HttpPost("smart-reply")]
    public async Task<ActionResult<AiSmartReplyResponse>> SmartReply([FromBody] AiSmartReplyRequest request, CancellationToken ct)
    {
        var result = await _aiService.GenerateSmartRepliesAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Generate embeddings for semantic search.
    /// </summary>
    [HttpPost("embeddings")]
    public async Task<ActionResult<AiEmbeddingResponse>> Embeddings([FromBody] AiEmbeddingRequest request, CancellationToken ct)
    {
        var result = await _aiService.GenerateEmbeddingAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// List all AI conversations for the authenticated user.
    /// </summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<AiConversationListResponse>> GetConversations(
        [FromQuery] int skip = 0, [FromQuery] int take = 20)
    {
        var result = await _aiService.GetConversationsAsync(GetUserId(), skip, take);
        return Ok(result);
    }

    /// <summary>
    /// Get the full message history for a specific conversation.
    /// </summary>
    [HttpGet("conversations/{conversationId}")]
    public async Task<ActionResult<AiConversationDetailResponse>> GetConversation(Guid conversationId)
    {
        var result = await _aiService.GetConversationDetailAsync(GetUserId(), conversationId);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Delete an AI conversation and its history.
    /// </summary>
    [HttpDelete("conversations/{conversationId}")]
    public async Task<ActionResult> DeleteConversation(Guid conversationId)
    {
        var deleted = await _aiService.DeleteConversationAsync(GetUserId(), conversationId);
        return deleted ? NoContent() : NotFound();
    }
}
