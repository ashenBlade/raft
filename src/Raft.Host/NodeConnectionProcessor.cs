using System.Net.Sockets;
using Raft.Core;
using Raft.Network;
using Raft.Network.Packets;
using Raft.Peer;
using Serilog;

namespace Raft.Host;

public class NodeConnectionProcessor : IDisposable
{
    public NodeConnectionProcessor(NodeId id, PacketClient client, RaftConsensusModule consensusModule, ILogger logger)
    {
        Id = id;
        Client = client;
        ConsensusModule = consensusModule;
        Logger = logger;
    }

    public CancellationTokenSource CancellationTokenSource { get; init; } = null!;
    private NodeId Id { get; }
    private Socket Socket => Client.Socket;
    private PacketClient Client { get; }
    private RaftConsensusModule ConsensusModule { get; }
    private ILogger Logger { get; }

    public async Task ProcessClientBackground()
    {
        var token = CancellationTokenSource.Token;
        Logger.Information("Начинаю обрабатывать запросы клиента {Id}", Id);
        try
        {
            while (token.IsCancellationRequested is false)
            {
                var packet = await Client.ReceiveAsync(token);
                if (packet is null)
                {
                    Logger.Information("От узла пришел пустой пакет. Соединение разорвано. Прекращаю обработку");
                    break;
                }

                var success = await ProcessPacket(packet, token);
                if (!success)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { }
        catch (Exception e)
        {
            Logger.Warning(e, "Во время обработки узла {Node} возникло необработанное исключение", Id);
            CloseClient();
        }
    }



    private async Task<bool> ProcessPacket(IPacket packet, CancellationToken token)
    {
        switch (packet.PacketType)
        {
            case PacketType.AppendEntriesRequest:
                await ProcessAppendEntriesAsync();
                break;
            case PacketType.RequestVoteRequest:
                await ProcessRequestVoteAsync();
                break;
            default:
                Logger.Information("От клиента получен неожиданный тип пакета: {PacketType}. Закрываю соединение", packet.PacketType);
                return false;
        }

        return true;

        async Task ProcessAppendEntriesAsync()
        {
            var request = ( ( AppendEntriesRequestPacket ) packet ).Request;
            var result = ConsensusModule.Handle(request);
            await Client.SendAsync(new AppendEntriesResponsePacket(result), token);
        }

        async Task ProcessRequestVoteAsync()
        {
            var request = ((RequestVoteRequestPacket ) packet ).Request;
            var result = ConsensusModule.Handle(request);
            await Client.SendAsync(new RequestVoteResponsePacket(result), token);
        }
    }

    private void CloseClient()
    {
        if (!Socket.Connected)
        {
            return;
        }
        Logger.Information("Закрываю соединение с узлом");
        Client.Socket.Disconnect(false);
        Client.Socket.Close();
    }

    public void Dispose()
    {
        try
        {
            CancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        { }

        CloseClient();
        Socket.Dispose();
        CancellationTokenSource.Dispose();
    }
}