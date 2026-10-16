using Microsoft.EntityFrameworkCore;
using OmniConnect.Backend.Data;
using OmniConnect.Shared.DTOs;
using OmniConnect.Shared.Models;

namespace OmniConnect.Backend.Services;

public class FileService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public FileService(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<FileUploadResponse> UploadAsync(Guid userId, IFormFile file)
    {
        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        var fileRecord = new FileRecord
        {
            UploaderId = userId,
            FileName = file.FileName,
            FileSize = file.Length,
            MimeType = file.ContentType,
            StoragePath = filePath,
            PublicUrl = $"/api/files/download/{fileName}",
        };

        _db.Set<FileRecord>().Add(fileRecord);
        await _db.SaveChangesAsync();

        return new FileUploadResponse(
            fileRecord.Id, fileRecord.FileName, fileRecord.FileSize,
            fileRecord.MimeType ?? "application/octet-stream",
            fileRecord.StoragePath, fileRecord.PublicUrl, fileRecord.UploadedAt
        );
    }

    public async Task<FileRecord?> GetFileAsync(Guid fileId) =>
        await _db.Set<FileRecord>().FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted);

    public async Task<List<FileDto>> GetMyFilesAsync(Guid userId)
    {
        return await _db.Set<FileRecord>()
            .Where(f => f.UploaderId == userId && !f.IsDeleted)
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new FileDto(f.Id, f.UploaderId, "", f.FileName,
                f.FileSize, f.MimeType, f.PublicUrl, f.UploadedAt))
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(Guid fileId, Guid userId)
    {
        var file = await _db.Set<FileRecord>().FirstOrDefaultAsync(f => f.Id == fileId && f.UploaderId == userId);
        if (file == null) return false;
        file.IsDeleted = true;
        if (File.Exists(file.StoragePath)) File.Delete(file.StoragePath);
        await _db.SaveChangesAsync();
        return true;
    }
}