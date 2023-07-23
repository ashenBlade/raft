namespace TaskFlux.Network.Requests.Packets;

public class ErrorResponsePacket: Packet
{
    public static readonly ErrorResponsePacket EmptyErrorMessagePacket = new(string.Empty); 
    public string Message { get; }
    public override PacketType Type => PacketType.ErrorResponse;

    public ErrorResponsePacket(string message)
    {
        Message = message;
    }
    
    public override void Accept(IPacketVisitor visitor)
    {
        visitor.Visit(this);
    }
    
    
    public override ValueTask AcceptAsync(IAsyncPacketVisitor visitor, CancellationToken token = default)
    {
        return visitor.VisitAsync(this, token);
    }
}