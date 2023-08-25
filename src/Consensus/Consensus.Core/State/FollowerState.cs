using System.Diagnostics;
using Consensus.Core.Commands.AppendEntries;
using Consensus.Core.Commands.InstallSnapshot;
using Consensus.Core.Commands.RequestVote;
using Consensus.Core.Commands.Submit;
using Consensus.Core.Log;
using Serilog;
using TaskFlux.Core;

namespace Consensus.Core.State;

public class FollowerState<TCommand, TResponse> : ConsensusModuleState<TCommand, TResponse>
{
    public override NodeRole Role => NodeRole.Follower;
    private readonly ILogger _logger;

    internal FollowerState(IConsensusModule<TCommand, TResponse> consensusModule, ILogger logger)
        : base(consensusModule)
    {
        _logger = logger;
    }

    public override void Initialize()
    {
        ElectionTimer.Timeout += OnElectionTimerTimeout;
    }

    public override RequestVoteResponse Apply(RequestVoteRequest request)
    {
        _logger.Verbose("Получен RequestVote");
        ElectionTimer.Reset();

        // Мы в более актуальном Term'е
        if (request.CandidateTerm < CurrentTerm)
        {
            return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: false);
        }

        if (CurrentTerm < request.CandidateTerm)
        {
            _logger.Debug("Получен RequestVote с большим термом {MyTerm} < {NewTerm}. Перехожу в Follower", CurrentTerm,
                request.CandidateTerm);
            ConsensusModule.PersistenceManager.UpdateState(request.CandidateTerm, request.CandidateId);
            return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: true);
        }

        var canVote =
            // Ранее не голосовали
            VotedFor is null
          ||
            // Текущий лидер/кандидат посылает этот запрос (почему бы не согласиться)
            VotedFor == request.CandidateId;

        // Отдать свободный голос можем только за кандидата 
        if (canVote
          &&
            // У которого лог в консистентном с нашим состоянием
            !PersistenceManager.Conflicts(request.LastLogEntryInfo))
        {
            _logger.Debug(
                "Получен RequestVote от узла за которого можем проголосовать. Id узла {NodeId}, Терм узла {Term}. Обновляю состояние",
                request.CandidateId.Value, request.CandidateTerm.Value);
            ConsensusModule.PersistenceManager.UpdateState(request.CandidateTerm, request.CandidateId);

            return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: true);
        }

        // Кандидат только что проснулся и не знает о текущем состоянии дел. 
        // Обновим его
        return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: false);
    }

    public override AppendEntriesResponse Apply(AppendEntriesRequest request)
    {
        ElectionTimer.Reset();

        if (request.Term < CurrentTerm)
        {
            // Лидер устрел
            return AppendEntriesResponse.Fail(CurrentTerm);
        }

        if (CurrentTerm < request.Term)
        {
            // Мы отстали от общего состояния (старый терм)
            ConsensusModule.PersistenceManager.UpdateState(request.Term, null);
        }

        if (PersistenceManager.Contains(request.PrevLogEntryInfo) is false)
        {
            // Префиксы закомиченных записей лога не совпадают 
            return AppendEntriesResponse.Fail(CurrentTerm);
        }

        if (0 < request.Entries.Count)
        {
            // Если это не Heartbeat, то применить новые команды
            PersistenceManager.InsertRange(request.Entries, request.PrevLogEntryInfo.Index + 1);
        }

        Debug.Assert(PersistenceManager.CommitIndex <= request.LeaderCommit,
            $"Индекс коммита лидера не должен быть меньше индекса коммита последователя. Индекс лидера: {request.LeaderCommit}. Индекс последователя: {PersistenceManager.CommitIndex}");

        if (PersistenceManager.CommitIndex == request.LeaderCommit)
        {
            return AppendEntriesResponse.Ok(CurrentTerm);
        }

        // В случае, если какие-то записи были закоммичены лидером, то сделать то же самое у себя.

        // Коммитим записи по индексу лидера
        PersistenceManager.Commit(request.LeaderCommit);

        // Закоммиченные записи можно уже применять к машине состояний 
        var notApplied = PersistenceManager.GetNotApplied();

        foreach (var entry in notApplied)
        {
            var command = CommandSerializer.Deserialize(entry.Data);
            StateMachine.ApplyNoResponse(command);
        }

        // После применения команды, обновляем индекс последней примененной записи.
        // Этот индекс обновляем сразу, т.к. 
        // 1. Если возникнет исключение в работе, то это означает неправильную работу самого приложения, а не бизнес-логики
        // 2. Эта операция сразу сбрасывает данные на диск (Flush) - дорого
        PersistenceManager.SetLastApplied(request.LeaderCommit);

        if (MaxLogFileSize < PersistenceManager.LogFileSize)
        {
            var snapshot = StateMachine.GetSnapshot();
            var snapshotLastEntryInfo = PersistenceManager.LastApplied;
            PersistenceManager.SaveSnapshot(snapshotLastEntryInfo, snapshot, CancellationToken.None);
            PersistenceManager.ClearCommandLog();
        }

        return AppendEntriesResponse.Ok(CurrentTerm);
    }

    public override SubmitResponse<TResponse> Apply(SubmitRequest<TCommand> request)
    {
        if (!request.Descriptor.IsReadonly)
        {
            return SubmitResponse<TResponse>.NotLeader;
        }

        var response = StateMachine.Apply(request.Descriptor.Command);
        return SubmitResponse<TResponse>.Success(response, false);
    }

    private void OnElectionTimerTimeout()
    {
        _logger.Debug("Сработал Election Timeout. Перехожу в состояние Candidate");
        var candidateState = ConsensusModule.CreateCandidateState();
        if (ConsensusModule.TryUpdateState(candidateState, this))
        {
            ConsensusModule.ElectionTimer.Stop();
            // Голосуем за себя и переходим в следующий терм
            ConsensusModule.PersistenceManager.UpdateState(ConsensusModule.CurrentTerm.Increment(), ConsensusModule.Id);
            ConsensusModule.ElectionTimer.Start();
        }
    }

    public override void Dispose()
    {
        ElectionTimer.Timeout -= OnElectionTimerTimeout;
    }

    public override InstallSnapshotResponse Apply(InstallSnapshotRequest request, CancellationToken token = default)
    {
        if (request.Term < CurrentTerm)
        {
            return new InstallSnapshotResponse(CurrentTerm);
        }

        // 1. Обновляем файл снапшота
        PersistenceManager.SaveSnapshot(new LogEntryInfo(request.LastIncludedTerm, request.LastIncludedIndex),
            request.Snapshot, token);
        // 2. Очищаем лог (лучше будет перезаписать данные полностью)
        PersistenceManager.ClearCommandLog();

        // 3. Восстановить состояние из снапшота
        RestoreState();
        return new InstallSnapshotResponse(CurrentTerm);
    }

    private void RestoreState()
    {
        // 1. Восстанавливаем из снапшота
        var stateMachine =
            PersistenceManager.TryGetSnapshot(out var snapshot)
                ? StateMachineFactory.Restore(snapshot)
                : StateMachineFactory.CreateEmpty();

        // 2. Применяем команды из лога, если есть
        var nonApplied = PersistenceManager.GetNotApplied();
        if (nonApplied.Count > 0)
        {
            foreach (var (_, payload) in nonApplied)
            {
                var command = ConsensusModule.CommandSerializer.Deserialize(payload);
                stateMachine.ApplyNoResponse(command);
            }
        }

        // 3. Обновляем машину
        ConsensusModule.StateMachine = stateMachine;
    }
}