using System.Diagnostics;
using Consensus.CommandQueue;
using Consensus.Raft.Commands.AppendEntries;
using Consensus.Raft.Commands.InstallSnapshot;
using Consensus.Raft.Commands.RequestVote;
using Consensus.Raft.Commands.Submit;
using Consensus.Raft.Persistence;
using Consensus.Raft.State;
using Consensus.Raft.State.LeaderState;
using Serilog;
using TaskFlux.Core;

namespace Consensus.Raft;

[DebuggerDisplay("Роль: {CurrentRole}; Терм: {CurrentTerm}; Id: {Id}")]
public class RaftConsensusModule<TCommand, TResponse>
    : IConsensusModule<TCommand, TResponse>,
      IDisposable
{
    private readonly ICommandSerializer<TCommand> _commandSerializer;
    
    /// <summary>
    /// Фабрика для создания очередей команд для отправки другим узлам, когда узел станет лидером. 
    /// </summary>
    /// <remarks>
    /// Нужно только лидеру и вынесено в абстракцию, для тестирования
    /// </remarks>
    private readonly IRequestQueueFactory _requestQueueFactory;

    public NodeRole CurrentRole =>
        ( ( IConsensusModule<TCommand, TResponse> ) this ).CurrentState.Role;

    private ILogger Logger { get; }
    public NodeId Id { get; }
    
    public Term CurrentTerm => PersistenceFacade.CurrentTerm;
    public NodeId? VotedFor => PersistenceFacade.VotedFor;
    public PeerGroup PeerGroup { get; }
    public IStateMachine<TCommand, TResponse> StateMachine { get; set; }
    public IStateMachineFactory<TCommand, TResponse> StateMachineFactory { get; }

    // Инициализируем либо в .Create (прод), либо через internal метод SetStateTest
    private State<TCommand, TResponse> _currentState = null!;

    State<TCommand, TResponse> IConsensusModule<TCommand, TResponse>.CurrentState => GetCurrentStateCheck();

    private State<TCommand, TResponse> GetCurrentStateCheck()
    {
        return _currentState
            ?? throw new ArgumentNullException(nameof(_currentState), "Текущее состояние еще не проставлено");
    }

    internal void SetStateTest(State<TCommand, TResponse> state)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (_currentState is not null)
        {
            throw new InvalidOperationException($"Состояние узла уже выставлено в {_currentState.Role}");
        }

        state.Initialize();
        _currentState = state;
    }
    
    public bool TryUpdateState(State<TCommand, TResponse> newState,
                               State<TCommand, TResponse> oldState)
    {
        var stored = Interlocked.CompareExchange(ref _currentState, newState, oldState);
        if (stored == oldState)
        {
            stored.Dispose();
            newState.Initialize();
            RoleChanged?.Invoke(stored.Role, newState.Role);
            return true;
        }

        return false;
    }

    public ITimer ElectionTimer { get; }
    public ITimer HeartbeatTimer { get; }
    public IBackgroundJobQueue BackgroundJobQueue { get; }
    public ICommandQueue CommandQueue { get; }
    public StoragePersistenceFacade PersistenceFacade { get; }

    internal RaftConsensusModule(
        NodeId id,
        PeerGroup peerGroup,
        ILogger logger,
        ITimer electionTimer,
        ITimer heartbeatTimer,
        IBackgroundJobQueue backgroundJobQueue,
        StoragePersistenceFacade persistenceFacade,
        ICommandQueue commandQueue,
        IStateMachine<TCommand, TResponse> stateMachine,
        ICommandSerializer<TCommand> commandSerializer,
        IRequestQueueFactory requestQueueFactory,
        IStateMachineFactory<TCommand, TResponse> stateMachineFactory)
    {
        _commandSerializer = commandSerializer;
        _requestQueueFactory = requestQueueFactory;
        StateMachineFactory = stateMachineFactory;
        Id = id;
        Logger = logger;
        PeerGroup = peerGroup;
        ElectionTimer = electionTimer;
        HeartbeatTimer = heartbeatTimer;
        BackgroundJobQueue = backgroundJobQueue;
        PersistenceFacade = persistenceFacade;
        CommandQueue = commandQueue;
        StateMachine = stateMachine;
    }

    public RequestVoteResponse Handle(RequestVoteRequest request)
    {
        return _currentState.Apply(request);
    }

    public AppendEntriesResponse Handle(AppendEntriesRequest request)
    {
        return _currentState.Apply(request);
    }

    public InstallSnapshotResponse Handle(InstallSnapshotRequest request, CancellationToken token = default)
    {
        return _currentState.Apply(request, token);
    }

    public SubmitResponse<TResponse> Handle(SubmitRequest<TCommand> request)
    {
        return _currentState.Apply(request);
    }

    public event RoleChangedEventHandler? RoleChanged;

    public State<TCommand, TResponse> CreateFollowerState()
    {
        return new FollowerState<TCommand, TResponse>(this, StateMachineFactory, _commandSerializer, Logger.ForContext("SourceContext", "Raft(Follower)"));
    }

    public State<TCommand, TResponse> CreateLeaderState()
    {
        return new LeaderState<TCommand, TResponse>(this, Logger.ForContext("SourceContext", "Raft(Leader)"), _commandSerializer, _requestQueueFactory);
    }

    public State<TCommand, TResponse> CreateCandidateState()
    {
        return new CandidateState<TCommand, TResponse>(this, Logger.ForContext("SourceContext", "Raft(Candidate)"));
    }

    public override string ToString()
    {
        return
            $"RaftNode(Id = {Id}, Role = {CurrentRole}, Term = {CurrentTerm}, VotedFor = {VotedFor?.ToString() ?? "null"})";
    }

    public void Dispose()
    {
        _currentState?.Dispose();
    }

    public static RaftConsensusModule<TCommand, TResponse> Create(
        NodeId id,
        PeerGroup peerGroup,
        ILogger logger,
        ITimer electionTimer,
        ITimer heartbeatTimer,
        IBackgroundJobQueue backgroundJobQueue,
        StoragePersistenceFacade persistenceFacade,
        ICommandQueue commandQueue,
        IStateMachine<TCommand, TResponse> stateMachine,
        IStateMachineFactory<TCommand, TResponse> stateMachineFactory,
        ICommandSerializer<TCommand> commandSerializer,
        IRequestQueueFactory requestQueueFactory)
    {
        var module = new RaftConsensusModule<TCommand, TResponse>(id, peerGroup, logger, electionTimer, heartbeatTimer,
            backgroundJobQueue, persistenceFacade, commandQueue, stateMachine, commandSerializer,
            requestQueueFactory, stateMachineFactory);
        var followerState = module.CreateFollowerState();
        module._currentState = followerState;
        followerState.Initialize();
        return module;
    }
}