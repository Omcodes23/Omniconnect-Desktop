using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.DTOs;

namespace OmniConnect.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly FileService _fileService;

    public FilesController(FileService fileService) => _fileService = fileService;

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)] // 100MB
    public async Task<ActionResult<FileUploadResponse>> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });
        var result = await _fileService.UploadAsync(GetUserId(), file);
        return Ok(result);
    }

    [HttpGet("download/{fileName}")]
    [AllowAnonymous]
    public IActionResult Download(string fileName)
    {
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads", fileName);
        if (!System.IO.File.Exists(filePath)) return NotFound();
        return PhysicalFile(filePath, "application/octet-stream", fileName);
    }

    [HttpGet]
    public async Task<ActionResult<List<FileDto>>> GetMyFiles()
    {
        return Ok(await _fileService.GetMyFilesAsync(GetUserId()));
    }

    [HttpDelete("{fileId}")]
    public async Task<ActionResult> Delete(Guid fileId)
    {
        var deleted = await _fileService.DeleteAsync(fileId, GetUserId());
        return deleted ? NoContent() : NotFound();
    }
}