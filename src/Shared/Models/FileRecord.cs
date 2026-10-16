namespace OmniConnect.Shared.Models;

public class FileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UploaderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? MimeType { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}