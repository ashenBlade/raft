using System.Buffers;
using TaskFlux.Consensus.Commands.RequestVote;
using TaskFlux.Consensus.Persistence;
using TaskFlux.Utils.Serialization;

namespace TaskFlux.Application.Cluster.Network.Packets;

public class RequestVoteRequestPacket : NodePacket
{
    public RequestVoteRequest Request { get; }
    public override NodePacketType PacketType => NodePacketType.RequestVoteRequest;

    public RequestVoteRequestPacket(RequestVoteRequest request)
    {
        Request = request;
    }

    protected override int EstimatePacketSize()
    {
        return SizeOf.PacketType // Маркер пакета
             + SizeOf.NodeId     // Id узла
             + SizeOf.Term       // Терм узла кандидата
             + SizeOf.Term       // Терм последней записи
             + SizeOf.Lsn;       // Индекс последней записи
    }

    protected override void SerializeBuffer(Span<byte> buffer)
    {
        var writer = new SpanBinaryWriter(buffer);
        writer.Write(( byte ) NodePacketType.RequestVoteRequest);
        writer.Write(Request.CandidateId.Id);
        writer.Write(Request.CandidateTerm.Value);
        writer.Write(Request.LastLogEntryInfo.Term.Value);
        writer.Write(Request.LastLogEntryInfo.Index);
    }

    private const int PayloadSize = SizeOf.NodeId // Id узла кандидата
                                  + SizeOf.Term   // Терм узла кандидата
                                  + SizeOf.Term   // Терм последней записи
                                  + SizeOf.Lsn;   // Индекс последней записи

    public new static RequestVoteRequestPacket Deserialize(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[PayloadSize];
        stream.ReadExactly(buffer);
        return DeserializePayload(buffer);
    }

    public new static async Task<RequestVoteRequestPacket> DeserializeAsync(Stream stream, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(PayloadSize);
        try
        {
            var memory = buffer.AsMemory(0, PayloadSize);
            await stream.ReadExactlyAsync(memory, token);
            return DeserializePayload(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static RequestVoteRequestPacket DeserializePayload(Span<byte> buffer)
    {
        var reader = new SpanBinaryReader(buffer);
        var candidateId = reader.ReadNodeId();
        var candidateTerm = reader.ReadTerm();
        var lastEntryTerm = reader.ReadTerm();
        var lastEntryLsn = reader.ReadLsn();
        return new RequestVoteRequestPacket(new RequestVoteRequest(candidateId, candidateTerm,
            new LogEntryInfo(lastEntryTerm, lastEntryLsn)));
    }
}