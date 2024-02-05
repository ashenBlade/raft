using System.Diagnostics;
using Serilog;
using TaskFlux.Consensus.Commands.AppendEntries;
using TaskFlux.Consensus.Commands.InstallSnapshot;
using TaskFlux.Consensus.Commands.RequestVote;
using TaskFlux.Consensus.Persistence;
using TaskFlux.Core;

namespace TaskFlux.Consensus.State;

public class FollowerState<TCommand, TResponse>
    : State<TCommand, TResponse>
{
    public override NodeId? LeaderId => _leaderId;
    private NodeId? _leaderId;

    public override NodeRole Role => NodeRole.Follower;
    private readonly ITimer _electionTimer;
    private readonly ILogger _logger;

    internal FollowerState(RaftConsensusModule<TCommand, TResponse> consensusModule,
                           ITimer electionTimer,
                           ILogger logger)
        : base(consensusModule)
    {
        _electionTimer = electionTimer;
        _logger = logger;
    }

    public override void Initialize()
    {
        _electionTimer.Timeout += OnElectionTimerTimeout;
        _electionTimer.Schedule();
    }

    public override RequestVoteResponse Apply(RequestVoteRequest request)
    {
        // Мы в более актуальном Term'е
        if (request.CandidateTerm < CurrentTerm)
        {
            _logger.Verbose("Терм кандидата меньше моего терма. Отклоняю запрос");
            return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: false);
        }

        // Флаг возможности отдать голос,
        // так как в каждом терме мы можем отдать голос только за 1 кандидата
        var canVote =
            // Ранее не голосовали
            VotedFor is null
          ||
            // Текущий лидер/кандидат опять посылает этот запрос (почему бы не согласиться)
            VotedFor == request.CandidateId;

        // Отдать свободный голос можем только за кандидата 
        var isUpToDate = Persistence.IsUpToDate(request.LastLogEntryInfo);
        if (canVote && isUpToDate)
        {
            _logger.Debug(
                "Получен RequestVote от узла за которого можем проголосовать. Id узла {NodeId}, Терм узла {Term}. Обновляю состояние",
                request.CandidateId.Id, request.CandidateTerm.Value);

            Persistence.UpdateState(request.CandidateTerm, request.CandidateId);

            return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: true);
        }

        if (CurrentTerm < request.CandidateTerm)
        {
            if (!isUpToDate)
            {
                _logger.Debug(
                    "Терм кандидата больше, но лог конфликтует: обновляю только терм. Кандидат: {CandidateId}. Моя последняя запись: {MyLastEntry}. Его последняя запись: {CandidateLastEntry}",
                    request.CandidateId, Persistence.LastEntry, request.LastLogEntryInfo);
            }
            else
            {
                _logger.Debug("Терм кандидата больше. Кандидат: {CandidateId}", request.CandidateId);
            }

            Persistence.UpdateState(request.CandidateTerm, null);
        }

        return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: false);
    }

    public override AppendEntriesResponse Apply(AppendEntriesRequest request)
    {
        if (request.Term < CurrentTerm)
        {
            // Лидер устарел/отстал
            return AppendEntriesResponse.Fail(CurrentTerm);
        }

        if (request.Entries.Count > 0)
        {
            _logger.Debug("Получен AppendEntries запрос");
        }

        using var _ = ElectionTimerScope.BeginScope(_electionTimer);
        if (CurrentTerm < request.Term)
        {
            _logger.Information("Получен AppendEntries с большим термом {GreaterTerm}. Старый терм: {CurrentTerm}",
                request.Term, CurrentTerm);
            // Мы отстали от общего состояния (старый терм)
            Persistence.UpdateState(request.Term, null);
        }

        if (!Persistence.PrefixMatch(request.PrevLogEntryInfo))
        {
            // Префиксы закоммиченных записей лога не совпадают 
            _logger.Debug(
                "Текущий лог не совпадает с логом узла {NodeId}. Моя последняя запись: {MyLastEntry}. Его предыдущая запись: {HisLastEntry}",
                request.LeaderId, Persistence.LastEntry, request.PrevLogEntryInfo);
            return AppendEntriesResponse.Fail(CurrentTerm);
        }

        if (0 < request.Entries.Count)
        {
            var insertIndex = request.PrevLogEntryInfo.Index + 1;
            Debug.Assert(Persistence.CommitIndex < insertIndex, "Persistence.CommitIndex < insertIndex",
                "Нельзя перезаписать закоммиченные записи");
            Persistence.InsertRange(request.Entries, insertIndex);
        }

        if (Persistence.CommitIndex == request.LeaderCommit)
        {
            _leaderId = request.LeaderId;
            return AppendEntriesResponse.Ok(CurrentTerm);
        }

        // Дополнительная проверка того, что не выходим за кол-во записей у себя же
        if (Persistence.CommitIndex < request.LeaderCommit)
        {
            var commitIndex = Math.Min(request.LeaderCommit, Persistence.LastEntry.Index);
            _logger.Information("Коммичу запись по индексу {CommitIndex}", commitIndex);
            Persistence.Commit(commitIndex);
        }
        else if (request.LeaderCommit < Persistence.CommitIndex)
        {
            _logger.Warning(
                "Лидер передал индекс коммита меньше, чем у меня. Индекс коммита лидера: {LeaderCommitIndex}. Текущий индекс коммита: {CommitIndex}",
                request.LeaderCommit, Persistence.CommitIndex);
        }

        if (Persistence.ShouldCreateSnapshot())
        {
            _logger.Information("Создаю снапшот приложения");
            var oldSnapshot = Persistence.TryGetSnapshot(out var s, out var _)
                                  ? s
                                  : null;
            var deltas = Persistence.ReadCommittedDeltaFromPreviousSnapshot();
            var newSnapshot = ApplicationFactory.CreateSnapshot(oldSnapshot, deltas);
            var lastIncludedEntry = Persistence.GetEntryInfo(Persistence.CommitIndex);
            Persistence.SaveSnapshot(newSnapshot, lastIncludedEntry);
        }

        _leaderId = request.LeaderId;

        return AppendEntriesResponse.Ok(CurrentTerm);
    }

    private readonly record struct ElectionTimerScope(ITimer Timer) : IDisposable
    {
        public void Dispose()
        {
            Timer.Schedule();
        }

        public static ElectionTimerScope BeginScope(ITimer timer)
        {
            timer.Stop();
            return new ElectionTimerScope(timer);
        }
    }

    public override SubmitResponse<TResponse> Apply(TCommand command, CancellationToken token = default)
    {
        return SubmitResponse<TResponse>.NotLeader;
    }

    private void OnElectionTimerTimeout()
    {
        var candidateState = ConsensusModule.CreateCandidateState();
        if (ConsensusModule.TryUpdateState(candidateState, this))
        {
            _logger.Debug("Сработал Election Timeout. Стал кандидатом");
            // Голосуем за себя и переходим в следующий терм
            ConsensusModule.Persistence.UpdateState(ConsensusModule.CurrentTerm.Increment(),
                ConsensusModule.Id);
        }
        else
        {
            _logger.Debug("Сработал таймер выборов, но перейти в кандидата не удалось: состояние уже изменилось");
        }
    }

    public override void Dispose()
    {
        _electionTimer.Timeout -= OnElectionTimerTimeout;
        _electionTimer.Dispose();
    }

    public override InstallSnapshotResponse Apply(InstallSnapshotRequest request,
                                                  CancellationToken token = default)
    {
        if (request.Term < CurrentTerm)
        {
            _logger.Information("Терм узла меньше моего. Отклоняю InstallSnapshot запрос");
            return new InstallSnapshotResponse(CurrentTerm);
        }

        using var _ = ElectionTimerScope.BeginScope(_electionTimer);
        _logger.Information("Получен InstallSnapshot запрос");
        _logger.Debug("Получен снапшот с индексом {Index} и термом {Term}", request.LastEntry.Index,
            request.LastEntry.Term);

        if (CurrentTerm < request.Term)
        {
            _logger.Information("Терм лидера больше моего. Обновляю терм до {Term}", request.Term);
            NodeId? votedFor = null;
            if (Persistence.VotedFor is null || Persistence.VotedFor == request.LeaderId)
            {
                votedFor = request.LeaderId;
            }

            Persistence.UpdateState(request.Term, votedFor);
        }

        _electionTimer.Stop();
        _electionTimer.Schedule();
        _logger.Debug("Начинаю получать чанки снапшота");
        token.ThrowIfCancellationRequested();
        var snapshotWriter = Persistence.CreateSnapshot(request.LastEntry);
        try
        {
            foreach (var chunk in request.Snapshot.GetAllChunks(token))
            {
                using var scope = ElectionTimerScope.BeginScope(_electionTimer);
                snapshotWriter.InstallChunk(chunk.Span, token);
            }

            snapshotWriter.Commit();
        }
        catch (Exception)
        {
            snapshotWriter.Discard();
            throw;
        }

        _logger.Debug("Снапшот установлен");

        // Persistence.SetLastApplied(Persistence.CommitIndex);

        _leaderId = request.LeaderId;

        return new InstallSnapshotResponse(CurrentTerm);
    }
}