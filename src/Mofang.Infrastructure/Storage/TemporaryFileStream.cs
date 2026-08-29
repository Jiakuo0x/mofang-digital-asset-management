namespace Mofang.Infrastructure.Storage;

internal sealed class TemporaryFileStream(string path) : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose)
{
}
