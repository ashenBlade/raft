using TaskFlux.Consensus;
using TaskFlux.Utils.Serialization;

namespace TaskFlux.Application.Cluster.Network.Packets;

public class InstallSnapshotResponsePacket : NodePacket
{
    public Term CurrentTerm { get; }
    public override NodePacketType PacketType => NodePacketType.InstallSnapshotResponse;

    public InstallSnapshotResponsePacket(Term term)
    {
        CurrentTerm = term;
    }

    protected override int EstimatePacketSize()
    {
        return SizeOf.PacketType // Маркер
             + SizeOf.Term;      // Терм
    }

    protected override void SerializeBuffer(Span<byte> buffer)
    {
        var writer = new SpanBinaryWriter(buffer);
        writer.Write(( byte ) NodePacketType.InstallSnapshotResponse);
        writer.Write(CurrentTerm.Value);
    }

    private const int PayloadSize = SizeOf.Term;

    public new static InstallSnapshotResponsePacket Deserialize(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[PayloadSize];
        stream.ReadExactly(buffer);
        return DeserializePayload(buffer);
    }

    public new static async Task<InstallSnapshotResponsePacket> DeserializeAsync(Stream stream, CancellationToken token)
    {
        using var buffer = Rent(PayloadSize);
        await stream.ReadExactlyAsync(buffer.GetMemory(), token);
        return DeserializePayload(buffer.GetSpan());
    }

    private static InstallSnapshotResponsePacket DeserializePayload(Span<byte> buffer)
    {
        var reader = new SpanBinaryReader(buffer);
        var term = reader.ReadTerm();
        return new InstallSnapshotResponsePacket(term);
    }
}