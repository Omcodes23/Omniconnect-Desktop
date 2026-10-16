namespace OmniConnect.Shared.DTOs;

public record FileUploadResponse(
    Guid Id,
    string FileName,
    long FileSize,
    string MimeType,
    string StoragePath,
    string? PublicUrl,
    DateTime UploadedAt
);

public record FileDto(
    Guid Id,
    Guid UploaderId,
    string UploaderName,
    string FileName,
    long FileSize,
    string? MimeType,
    string? PublicUrl,
    DateTime UploadedAt
);