using Raft.Core.Commands;
using Raft.Core.Commands.Heartbeat;
using Raft.Core.Log;
using Serilog;

namespace Raft.Core.StateMachine;

public class RaftStateMachine: IDisposable, IStateMachine
{
    public NodeRole CurrentRole => _currentState.Role;
    public ILogger Logger { get; }
    public INode Node { get; }
    
    private INodeState _currentState;
    public INodeState CurrentState
    {
        get => _currentState;
        set
        {
            _currentState?.Dispose();
            _currentState = value;
        }
    }

    public ITimer ElectionTimer { get; }
    public ITimer HeartbeatTimer { get; }
    public IJobQueue JobQueue { get; }
    public ILog Log { get; }


    // Для тестов
    internal RaftStateMachine(INode node, ILogger logger, ITimer electionTimer, ITimer heartbeatTimer, IJobQueue jobQueue, ILog log)
    {
        Node = node;
        Logger = logger;
        ElectionTimer = electionTimer;
        HeartbeatTimer = heartbeatTimer;
        JobQueue = jobQueue;
        Log = log;
        _currentState = FollowerState.Start(this);
    }
    

    public Task<RequestVoteResponse> Handle(RequestVoteRequest request, CancellationToken token = default)
    {
        return CurrentState.Apply(request, token);
    }

    public Task<HeartbeatResponse> Handle(HeartbeatRequest request, CancellationToken token = default)
    {
        return CurrentState.Apply(request, token);
    }
    public void Dispose()
    {
        _currentState.Dispose();
    }

    public static RaftStateMachine Start(INode node,
                                         ILogger logger,
                                         ITimer electionTimer,
                                         ITimer heartbeatTimer,
                                         IJobQueue jobQueue,
                                         ILog log)
    {
        var raft = new RaftStateMachine(node, logger, electionTimer, heartbeatTimer, jobQueue, log);
        return raft;
    }
}