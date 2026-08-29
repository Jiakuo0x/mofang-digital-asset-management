using System.Threading.Channels;
using Mofang.Application;

namespace Mofang.Infrastructure.Jobs;

public sealed class ThumbnailQueue : IThumbnailQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1024)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid assetId, CancellationToken cancellationToken = default) => _channel.Writer.WriteAsync(assetId, cancellationToken);
    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);
}
