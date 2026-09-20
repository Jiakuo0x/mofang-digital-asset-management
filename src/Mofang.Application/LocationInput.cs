using System.Text.RegularExpressions;

namespace Mofang.Application;

public sealed record LocationInput(Guid? LibraryId, string? Kind, Guid? Id, string Text, bool IsPath)
{
    public static LocationInput Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 8192)
            throw new ArgumentException("请粘贴文件或文件夹的位置，最多 8192 个字符。");

        // A supplied code is authoritative: never fall back to a stale path for an invalid code.
        var codes = Regex.Matches(input, @"MF\d+:[^\s]+", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (codes.Count > 0)
        {
            if (codes.Count != 1) throw new ArgumentException("请一次粘贴一个文件或文件夹的位置。");
            var parts = codes[0].Value.TrimEnd('。', '，', '；').Split(':');
            if (parts.Length != 4 || !parts[0].Equals("MF1", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(parts[1], "D", out var libraryId)
                || parts[2] is not ("asset" or "folder")
                || (parts[3] != "root" && !Guid.TryParseExact(parts[3], "D", out _))
                || (parts[3] == "root" && parts[2] != "folder"))
                throw new ArgumentException("定位码格式无效或版本不受支持，请重新复制位置。");
            return new(libraryId, parts[2], parts[3] == "root" ? null : Guid.Parse(parts[3]), "", false);
        }
        if (input.Contains("定位码", StringComparison.Ordinal))
            throw new ArgumentException("定位码不完整，请重新复制位置。");

        var lines = input.Trim().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var paths = lines.Where(x => x.StartsWith("位置：", StringComparison.Ordinal) || x.StartsWith("位置:", StringComparison.Ordinal)).ToArray();
        if (paths.Length > 1 || (paths.Length == 0 && lines.Length != 1))
            throw new ArgumentException("请一次粘贴一个位置，或输入文件、文件夹名称。");
        var text = (paths.Length == 1 ? paths[0][3..] : lines[0]).Trim().Trim('"', '“', '”');
        if (Regex.IsMatch(text, @"^(?:[a-zA-Z]:[\\/]|[a-zA-Z]+://|\\\\)", RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("请输入资产库中的目录路径，本机磁盘路径和下载链接不能用于定位。");
        text = text.Replace('\\', '/');
        var absolute = text.StartsWith('/');
        var segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x is "." or "..") || text.Any(char.IsControl))
            throw new ArgumentException("位置包含无效的路径片段。");
        text = (absolute ? "/" : "") + string.Join('/', segments);
        if (text.Length == 0) throw new ArgumentException("请输入文件或文件夹的位置。");
        return new(null, null, null, text, absolute);
    }
}
