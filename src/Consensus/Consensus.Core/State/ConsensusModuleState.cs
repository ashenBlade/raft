using Consensus.Core.Commands.AppendEntries;
using Consensus.Core.Commands.RequestVote;
using Consensus.Core.Commands.Submit;
using Consensus.Core.Log;
using Consensus.CommandQueue;
using Consensus.StateMachine;
using TaskFlux.Core;

namespace Consensus.Core.State;

/// <summary>
/// Интерфейс, представляющий конкретное состояние узла
/// </summary>
/// <remarks>
/// IDisposable нужно вызывать для сброса таймеров и очистки ресурсов предыдущего состояния (отписка от таймеров и т.д.)
/// </remarks>
public abstract class ConsensusModuleState<TCommand, TResponse>
{
    internal IConsensusModule<TCommand, TResponse> ConsensusModule { get; }
    protected ILog Log => ConsensusModule.Log;
    protected Term CurrentTerm => ConsensusModule.CurrentTerm;
    protected NodeId? VotedFor => ConsensusModule.VotedFor;
    protected ICommandQueue CommandQueue => ConsensusModule.CommandQueue;
    protected IStateMachine<TCommand, TResponse> StateMachine => ConsensusModule.StateMachine;
    protected NodeId Id => ConsensusModule.Id;
    protected ITimer ElectionTimer => ConsensusModule.ElectionTimer;
    protected ITimer HeartbeatTimer => ConsensusModule.HeartbeatTimer;
    protected IBackgroundJobQueue BackgroundJobQueue => ConsensusModule.BackgroundJobQueue;
    protected PeerGroup PeerGroup => ConsensusModule.PeerGroup;
    protected ISerializer<TCommand> CommandSerializer => ConsensusModule.CommandSerializer;

    protected ConsensusModuleState<TCommand, TResponse> CurrentState
    {
        get => ConsensusModule.CurrentState;
        set => ConsensusModule.CurrentState = value;
    }

    internal ConsensusModuleState(IConsensusModule<TCommand, TResponse> consensusModule)
    {
        ConsensusModule = consensusModule;
    }

    /// <summary>
    /// Текущая роль этого состояния
    /// </summary>
    public abstract NodeRole Role { get; }

    /// <summary>
    /// Применить команду RequestVote
    /// </summary>
    /// <param name="request">Объект запроса</param>
    /// <returns>Ответ узла</returns>
    public abstract RequestVoteResponse Apply(RequestVoteRequest request);

    /// <summary>
    /// Применить команду AppendEntries
    /// </summary>
    /// <param name="request">Объект запроса</param>
    /// <returns>Ответ узла</returns>
    public abstract AppendEntriesResponse Apply(AppendEntriesRequest request);

    /// <summary>
    /// Применить команду к машине состояний
    /// </summary>
    /// <param name="request">Объект запроса</param>
    /// <returns>Результат операции</returns>
    public abstract SubmitResponse<TResponse> Apply(SubmitRequest<TCommand> request);
    public abstract void Dispose();
}