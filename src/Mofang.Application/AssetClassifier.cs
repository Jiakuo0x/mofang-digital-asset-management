using Mofang.Domain;

namespace Mofang.Application;

public static class AssetClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".flac", ".aac", ".m4a", ".ogg" };
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".txt", ".md", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" };
    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase) { ".fbx", ".obj", ".gltf", ".glb", ".blend", ".max", ".ma", ".mb", ".usd", ".usdz" };
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar", ".tar", ".gz" };
    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase) { ".prproj", ".aep", ".drp", ".psd", ".ai", ".c4d", ".nk" };

    public static AssetType Classify(string fileName, string? mimeType = null)
    {
        var extension = Path.GetExtension(fileName);
        if (ImageExtensions.Contains(extension) || mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true) return AssetType.Image;
        if (VideoExtensions.Contains(extension) || mimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true) return AssetType.Video;
        if (AudioExtensions.Contains(extension) || mimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true) return AssetType.Audio;
        if (DocumentExtensions.Contains(extension) || mimeType is "application/pdf" or "text/plain" or "text/markdown") return AssetType.Document;
        if (ModelExtensions.Contains(extension)) return AssetType.Model3D;
        if (ArchiveExtensions.Contains(extension)) return AssetType.Archive;
        if (ProjectExtensions.Contains(extension)) return AssetType.ProjectFile;
        return AssetType.Other;
    }

    public static bool NeedsThumbnail(AssetType type) => type == AssetType.Image;
    public static bool CanPreviewText(string extension) => extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
}
