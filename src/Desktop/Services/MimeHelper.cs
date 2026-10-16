namespace OmniConnect.Desktop.Services;

public static class MimeHelper
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".zip"] = "application/zip",
        [".rar"] = "application/vnd.rar",
        [".7z"] = "application/x-7z-compressed",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
    };

    public static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return MimeTypes.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }
}
