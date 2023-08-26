using Consensus.Raft.Persistence;
using TaskFlux.Serialization.Helpers;

namespace Consensus.Raft.Tests;

public class StubSnapshot : ISnapshot
{
    private readonly byte[] _data;

    public StubSnapshot(byte[] data)
    {
        _data = data;
    }

    public StubSnapshot(IEnumerable<byte> data) => _data = data.ToArray();

    public void WriteTo(Stream stream, CancellationToken token = default)
    {
        var writer = new StreamBinaryWriter(stream);
        writer.Write(_data);
    }
}