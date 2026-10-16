using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AiService _aiService;

    public AiController(AiService aiService) => _aiService = aiService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Send a chat message to an AI provider and receive a response.
    /// Supports OpenAI, Gemini, OpenRouter, and local GGUF models.
    /// </summary>
    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResponse>> Chat([FromBody] AiChatRequest request)
    {
        var result = await _aiService.ChatAsync(GetUserId(), request);
        return Ok(result);
    }

    /// <summary>
    /// Translate text from one language to another.
    /// </summary>
    [HttpPost("translate")]
    public async Task<ActionResult<AiTranslateResponse>> Translate([FromBody] AiTranslateRequest request)
    {
        var result = await _aiService.TranslateAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Summarize a block of text.
    /// </summary>
    [HttpPost("summarize")]
    public async Task<ActionResult<AiSummarizeResponse>> Summarize([FromBody] AiSummarizeRequest request)
    {
        var result = await _aiService.SummarizeAsync(request);
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
    public async Task<ActionResult<AiSmartReplyResponse>> SmartReply([FromBody] AiSmartReplyRequest request)
    {
        var result = await _aiService.GenerateSmartRepliesAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Generate embeddings for semantic search.
    /// </summary>
    [HttpPost("embeddings")]
    public async Task<ActionResult<AiEmbeddingResponse>> Embeddings([FromBody] AiEmbeddingRequest request)
    {
        var result = await _aiService.GenerateEmbeddingAsync(request);
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