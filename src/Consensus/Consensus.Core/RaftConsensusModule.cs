using System.Diagnostics;
using Consensus.Core.Commands.AppendEntries;
using Consensus.Core.Commands.RequestVote;
using Consensus.Core.Commands.Submit;
using Consensus.Core.Log;
using Consensus.Core.State;
using Consensus.CommandQueue;
using Consensus.StateMachine;
using Serilog;
using TaskFlux.Core;

namespace Consensus.Core;

[DebuggerDisplay("Роль: {CurrentRole}; Терм: {CurrentTerm}; Id: {Id}")]
public class RaftConsensusModule<TCommand, TResponse>
    : IConsensusModule<TCommand, TResponse>, 
      IDisposable
{
    public NodeRole CurrentRole =>
        ( ( IConsensusModule<TCommand, TResponse> ) this ).CurrentState.Role;
    public ILogger Logger { get; }
    public NodeId Id { get; }
    public Term CurrentTerm => MetadataStorage.ReadTerm();
    public NodeId? VotedFor => MetadataStorage.ReadVotedFor();
    public PeerGroup PeerGroup { get; }
    public IStateMachine<TCommand, TResponse> StateMachine { get; }
    public IMetadataStorage MetadataStorage { get; }
    public ISerializer<TCommand> CommandSerializer { get; }

    // Выставляем вручную в .Create
    public ConsensusModuleState<TCommand, TResponse>? CurrentState;

    ConsensusModuleState<TCommand, TResponse> IConsensusModule<TCommand, TResponse>.CurrentState
    {
        get => GetCurrentStateCheck();
        set
        {
            var oldState = CurrentState;
            oldState?.Dispose();
            var newState = value;
            CurrentState = newState;
            
            RoleChanged?.Invoke(oldState?.Role ?? NodeRole.Follower, newState.Role);
        }
    }

    private ConsensusModuleState<TCommand, TResponse> GetCurrentStateCheck()
    {
        return CurrentState 
            ?? throw new ArgumentNullException(nameof(CurrentState), "Текущее состояние еще не проставлено");
    }

    public ITimer ElectionTimer { get; }
    public ITimer HeartbeatTimer { get; }
    public IBackgroundJobQueue BackgroundJobQueue { get; }
    public ICommandQueue CommandQueue { get; } 
    public ILog Log { get; }

    internal RaftConsensusModule(
        NodeId id,
        PeerGroup peerGroup,
        ILogger logger,
        ITimer electionTimer,
        ITimer heartbeatTimer,
        IBackgroundJobQueue backgroundJobQueue,
        ILog log,
        ICommandQueue commandQueue,
        IStateMachine<TCommand, TResponse> stateMachine,
        IMetadataStorage metadataStorage,
        ISerializer<TCommand> commandSerializer)
    {
        Id = id;
        Logger = logger;
        PeerGroup = peerGroup;
        ElectionTimer = electionTimer;
        HeartbeatTimer = heartbeatTimer;
        BackgroundJobQueue = backgroundJobQueue;
        Log = log;
        CommandQueue = commandQueue;
        StateMachine = stateMachine;
        MetadataStorage = metadataStorage;
        CommandSerializer = commandSerializer;
    }

    public void UpdateState(Term newTerm, NodeId? votedFor)
    {
        MetadataStorage.Update(newTerm, votedFor);
    }

    public RequestVoteResponse Handle(RequestVoteRequest request)
    {
        return CommandQueue.Enqueue(new RequestVoteCommand<TCommand, TResponse>(request, this));
    }
    
    public AppendEntriesResponse Handle(AppendEntriesRequest request)
    {
        return CommandQueue.Enqueue(new AppendEntriesCommand<TCommand, TResponse>(request, this));
    }

    public SubmitResponse<TResponse> Handle(SubmitRequest<TCommand> request)
    {
        return GetCurrentStateCheck().Apply(request);
    }

    public event RoleChangedEventHandler? RoleChanged;

    public override string ToString()
    {
        return $"RaftNode(Id = {Id}, Role = {CurrentRole}, Term = {CurrentTerm}, VotedFor = {VotedFor?.ToString() ?? "null"})";
    }

    public void Dispose()
    {
        CurrentState?.Dispose();
    }
}

public static class RaftConsensusModule
{
    public static RaftConsensusModule<TCommand, TResponse> Create<TCommand, TResponse>(NodeId id,
                                                                                       PeerGroup peerGroup,
                                                                                       ILogger logger,
                                                                                       ITimer electionTimer,
                                                                                       ITimer heartbeatTimer,
                                                                                       IBackgroundJobQueue backgroundJobQueue,
                                                                                       ILog log,
                                                                                       ICommandQueue commandQueue,
                                                                                       IStateMachine<TCommand, TResponse> stateMachine,
                                                                                       IMetadataStorage metadataStorage,
                                                                                       ISerializer<TCommand> serializer)
    {
        var raft = new RaftConsensusModule<TCommand, TResponse>(id, peerGroup, logger, electionTimer, heartbeatTimer, backgroundJobQueue, log, commandQueue, stateMachine, metadataStorage, serializer);
        raft.CurrentState = FollowerState.Create(raft);
        return raft;
    }
}