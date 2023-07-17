using Consensus.Core.Commands.AppendEntries;
using Consensus.Core.Commands.RequestVote;
using Consensus.Core.Commands.Submit;
using Consensus.Core.Log;
using Consensus.Core.State;
using Consensus.CommandQueue;
using Consensus.StateMachine;
using Serilog;

namespace Consensus.Core;

public interface IConsensusModule
{
    /// <summary>
    /// ID текущего узла
    /// </summary>
    public NodeId Id { get; }

    /// <summary>
    /// Текущая роль узла
    /// </summary>
    public NodeRole CurrentRole => CurrentState.Role;
    
    /// <summary>
    /// Номер текущего терма
    /// </summary>
    public Term CurrentTerm { get; }
    
    /// <summary>
    /// Id кандидата, за которого проголосовала текущая нода
    /// </summary>
    public NodeId? VotedFor { get; }
    
    /// <summary>
    /// Текущее состояние узла в зависимости от роли: Follower, Candidate, Leader
    /// </summary>
    internal IConsensusModuleState CurrentState { get; set; }

    /// <summary>
    /// Логгер для удобства
    /// </summary>
    ILogger Logger { get; }
    
    /// <summary>
    /// Таймер выборов.
    /// Используется в Follower и Candidate состояниях
    /// </summary>
    ITimer ElectionTimer { get; }
    
    /// <summary>
    /// Таймер для отправки Heartbeat запросов
    /// </summary>
    ITimer HeartbeatTimer { get; }
    
    /// <summary>
    /// Очередь задач для выполнения в на заднем фоне
    /// </summary>
    IJobQueue JobQueue { get; }
    
    /// <summary>
    /// Очередь команд для применения к узлу.
    /// Используется в первую очередь для изменения состояния
    /// </summary>
    ICommandQueue CommandQueue { get; }
    
    /// <summary>
    /// WAL для машины состояний, которую мы реплицируем
    /// </summary>
    ILog Log { get; }
    
    /// <summary>
    /// Группа других узлов кластера
    /// </summary>
    public PeerGroup PeerGroup { get; }
    
    /// <summary>
    /// Машина состояний, которую мы реплицируем
    /// </summary>
    public IStateMachine StateMachine { get; }

    public IMetadataStorage MetadataStorage { get; }

    /// <summary>
    /// Обновить состояние узла
    /// </summary>
    /// <param name="newTerm">Новый терм</param>
    /// <param name="votedFor">Отданный голос</param>
    public void UpdateState(Term newTerm, NodeId? votedFor);
    public RequestVoteResponse Handle(RequestVoteRequest request);
    public AppendEntriesResponse Handle(AppendEntriesRequest request);
    public SubmitResponse Handle(SubmitRequest request);
}