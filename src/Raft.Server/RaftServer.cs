using Raft.Core;
using Raft.Core.Commands;
using Raft.Core.StateMachine;
using Raft.Timers;
using Serilog;

namespace Raft.Server;

public class RaftServer
{
    private readonly ILogger _logger;

    public RaftServer(ILogger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken token)
    {
        _logger.Information("Сервер Raft запускается. Создаю узел");
        using var electionTimer = new SystemTimersTimer(TimeSpan.FromSeconds(5));
        using var heartbeatTimer = new SystemTimersTimer(TimeSpan.FromSeconds(1));

        var tcs = new TaskCompletionSource();
        await using var _ = token.Register(() => tcs.SetCanceled(token));

        _logger.Information("Запускаю сервер Raft");
        using var stateMachine = new RaftStateMachine(new Node(new PeerId(1)), _logger, electionTimer, heartbeatTimer);

        try
        {
            await tcs.Task;
        }
        catch (TaskCanceledException taskCanceled)
        {
            _logger.Information(taskCanceled, "Запрошено завершение работы приложения");
        }
        
        _logger.Information("Узел завершает работу");
        GC.KeepAlive(stateMachine);
    }
}