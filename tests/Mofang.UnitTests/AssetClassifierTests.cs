using Mofang.Application;
using Mofang.Domain;

namespace Mofang.UnitTests;

public sealed class AssetClassifierTests
{
    [Theory]
    [InlineData("frame.jpg", "image/jpeg", AssetType.Image)]
    [InlineData("daily.webm", "video/webm", AssetType.Video)]
    [InlineData("notice.pdf", "application/pdf", AssetType.Document)]
    [InlineData("readme.md", "text/markdown", AssetType.Document)]
    [InlineData("castle.fbx", "application/octet-stream", AssetType.Model3D)]
    [InlineData("timeline.prproj", "application/octet-stream", AssetType.ProjectFile)]
    [InlineData("package.7z", "application/octet-stream", AssetType.Archive)]
    public void Classify_RecognizesProductionFileTypes(string fileName, string mimeType, AssetType expected)
    {
        Assert.Equal(expected, AssetClassifier.Classify(fileName, mimeType));
    }

    [Fact]
    public void Thumbnail_IsOnlyRequiredForImages()
    {
        Assert.True(AssetClassifier.NeedsThumbnail(AssetType.Image));
        Assert.False(AssetClassifier.NeedsThumbnail(AssetType.Video));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".MD")]
    public void TextPreview_SupportsTxtAndMarkdown(string extension)
    {
        Assert.True(AssetClassifier.CanPreviewText(extension));
    }
}
