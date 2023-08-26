using System.Threading.Channels;
using Consensus.Raft.Persistence;

namespace Consensus.Raft.State.LeaderState;

internal record ChannelRequestQueue(IPersistenceFacade PersistenceFacade) : IRequestQueue
{
    private const int DefaultQueueSize = 32;

    private readonly Channel<AppendEntriesRequestSynchronizer> _channel =
        Channel.CreateBounded<AppendEntriesRequestSynchronizer>(new BoundedChannelOptions(DefaultQueueSize)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite
        });

    public IAsyncEnumerable<AppendEntriesRequestSynchronizer> ReadAllRequestsAsync(CancellationToken token)
    {
        return _channel.Reader.ReadAllAsync(token);
    }

    public void AddHeartbeatIfEmpty()
    {
        if (0 < _channel.Reader.Count) return;

        _channel.Writer.TryWrite(new AppendEntriesRequestSynchronizer(AlwaysTrueQuorumChecker.Instance,
            PersistenceFacade.LastEntry.Index));
    }

    public void AddAppendEntries(AppendEntriesRequestSynchronizer synchronizer)
    {
        while (!_channel.Writer.TryWrite(synchronizer))
        {
            _channel.Writer
                    .WaitToWriteAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
        }
    }
}