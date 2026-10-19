namespace OmniConnect.Shared.Models;

/// <summary>
/// Describes a single downloadable GGUF model. The catalog is intentionally
/// small and curated so the "Select the model" experience stays simple:
/// every entry is a small, instruction-tuned model that runs acceptably on
/// ordinary desktop and mobile CPUs.
/// </summary>
public sealed record ModelCatalogEntry(
    string Id,
    string DisplayName,
    string Description,
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    string Quantization,
    int ContextLength,
    string Family
);

/// <summary>
/// The single source of truth for the models OmniConnect can run locally.
/// Both the desktop backend and the Flutter client consume this list so the
/// two platforms never drift apart.
/// </summary>
public static class ModelCatalog
{
    // HuggingFace "resolve/main" URLs are plain HTTPS with no auth, no API key
    // and no rate-limit dance — the only network call the whole AI stack makes.
    private const string Hf = "https://huggingface.co";

    public static IReadOnlyList<ModelCatalogEntry> All { get; } = new List<ModelCatalogEntry>
    {
        new(
            Id: "qwen2.5-0.5b-instruct-q4km",
            DisplayName: "Qwen2.5 0.5B (Starter)",
            Description: "Tiny and fast. Great first download for older phones and laptops.",
            FileName: "qwen2.5-0.5b-instruct-q4_k_m.gguf",
            DownloadUrl: $"{Hf}/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf",
            SizeBytes: 397_808_000,
            Quantization: "Q4_K_M",
            ContextLength: 4096,
            Family: "Qwen2.5"),

        new(
            Id: "qwen2.5-1.5b-instruct-q4km",
            DisplayName: "Qwen2.5 1.5B",
            Description: "Balanced quality and speed. Recommended default.",
            FileName: "qwen2.5-1.5b-instruct-q4_k_m.gguf",
            DownloadUrl: $"{Hf}/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            SizeBytes: 1_117_320_000,
            Quantization: "Q4_K_M",
            ContextLength: 4096,
            Family: "Qwen2.5"),

        new(
            Id: "llama-3.2-1b-instruct-q4km",
            DisplayName: "Llama 3.2 1B",
            Description: "Meta's small instruction model. Solid general-purpose chat.",
            FileName: "Llama-3.2-1B-Instruct-Q4_K_M.gguf",
            DownloadUrl: $"{Hf}/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf",
            SizeBytes: 807_694_000,
            Quantization: "Q4_K_M",
            ContextLength: 8192,
            Family: "Llama 3.2"),

        new(
            Id: "llama-3.2-3b-instruct-q4km",
            DisplayName: "Llama 3.2 3B",
            Description: "Noticeably smarter, needs ~3 GB RAM free.",
            FileName: "Llama-3.2-3B-Instruct-Q4_K_M.gguf",
            DownloadUrl: $"{Hf}/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf",
            SizeBytes: 2_019_388_000,
            Quantization: "Q4_K_M",
            ContextLength: 8192,
            Family: "Llama 3.2"),

        new(
            Id: "phi-3.5-mini-instruct-q4km",
            DisplayName: "Phi-3.5 Mini",
            Description: "Strong reasoning for its size. Best on desktops.",
            FileName: "Phi-3.5-mini-instruct-Q4_K_M.gguf",
            DownloadUrl: $"{Hf}/bartowski/Phi-3.5-mini-instruct-GGUF/resolve/main/Phi-3.5-mini-instruct-Q4_K_M.gguf",
            SizeBytes: 2_393_233_000,
            Quantization: "Q4_K_M",
            ContextLength: 4096,
            Family: "Phi-3.5"),

        new(
            Id: "gemma-2-2b-it-q4km",
            DisplayName: "Gemma 2 2B",
            Description: "Google's compact chat model. Friendly, conversational tone.",
            FileName: "gemma-2-2b-it-Q4_K_M.gguf",
            DownloadUrl: $"{Hf}/bartowski/gemma-2-2b-it-GGUF/resolve/main/gemma-2-2b-it-Q4_K_M.gguf",
            SizeBytes: 1_711_902_000,
            Quantization: "Q4_K_M",
            ContextLength: 8192,
            Family: "Gemma 2"),
    };

    public static ModelCatalogEntry? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The model suggested when nothing is downloaded yet. Small enough to be a
    /// painless first download, capable enough to feel like an assistant.
    /// </summary>
    public static ModelCatalogEntry Default => Find("qwen2.5-1.5b-instruct-q4km")!;

    /// <summary>Human-friendly byte formatting shared by both clients.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
