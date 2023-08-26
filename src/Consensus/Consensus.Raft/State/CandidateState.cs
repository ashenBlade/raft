using Consensus.Raft.Commands.AppendEntries;
using Consensus.Raft.Commands.InstallSnapshot;
using Consensus.Raft.Commands.RequestVote;
using Consensus.Raft.Commands.Submit;
using Serilog;
using TaskFlux.Core;

namespace Consensus.Raft.State;

public class CandidateState<TCommand, TResponse> : State<TCommand, TResponse>
{
    public override NodeRole Role => NodeRole.Candidate;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;

    internal CandidateState(IConsensusModule<TCommand, TResponse> consensusModule, ILogger logger)
        : base(consensusModule)
    {
        _logger = logger;
        _cts = new();
    }

    public override void Initialize()
    {
        ElectionTimer.Timeout += OnElectionTimerTimeout;
        BackgroundJobQueue.RunInfinite(RunQuorum, _cts.Token);
    }

    private async Task<RequestVoteResponse?[]> SendRequestVotes(List<IPeer> peers, CancellationToken token)
    {
        // Отправляем запрос всем пирам.
        // Стандартный Scatter/Gather паттерн
        var request = new RequestVoteRequest(CandidateId: Id,
            CandidateTerm: CurrentTerm, LastLogEntryInfo: PersistenceFacade.LastEntry);

        var requests = new Task<RequestVoteResponse?>[peers.Count];
        for (var i = 0; i < peers.Count; i++)
        {
            requests[i] = peers[i].SendRequestVote(request, token);
        }

        return await Task.WhenAll(requests);
    }

    /// <summary>
    /// Запустить раунды кворума и попытаться получить большинство голосов.
    /// Выполняется в фоновом потоке
    /// </summary>
    /// <remarks>
    /// Дополнительные раунды нужны, когда какой-то узел не отдал свой голос.
    /// Всем отправившим ответ узлам (отдавшим голос или нет) запросы больше не посылаем.
    /// Грубо говоря, этот метод работает пока все узлы не ответят
    /// </remarks>
    private async Task RunQuorum()
    {
        var token = _cts.Token;
        try
        {
            await RunQuorumInner(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.Debug("Сбор кворума прерван - задача отменена");
        }
        catch (ObjectDisposedException)
        {
            _logger.Verbose("Источник токенов удален во время отправки запросов");
        }
        catch (Exception unhandled)
        {
            _logger.Fatal(unhandled, "Поймано необработанное исключение во время запуска кворума");
        }
    }

    private async Task RunQuorumInner(CancellationToken token)
    {
        _logger.Debug("Запускаю кворум для получения большинства голосов");
        var term = CurrentTerm;

        // Список из узлов, на которые нужно отправить запросы.
        // В начале инициализируется всеми узлами кластера, 
        // когда ответил (хоть как-то) будет удален из списка
        var leftPeers = new List<IPeer>(PeerGroup.Peers.Count);
        leftPeers.AddRange(PeerGroup.Peers);

        // Вспомогательный список из узлов, которые не ответили.
        // Используется для обновления leftPeers
        var notResponded = new List<IPeer>();

        // Количество узлов, которые отдали за нас голос
        var votes = 0;
        _logger.Debug("Начинаю раунд кворума для терма {Term}. Отправляю запросы на узлы: {Peers}", term,
            leftPeers.Select(x => x.Id));
        while (!QuorumReached() && !token.IsCancellationRequested)
        {
            var responses = await SendRequestVotes(leftPeers, token);
            if (token.IsCancellationRequested)
            {
                _logger.Debug("Операция была отменена во время отправки запросов. Завершаю кворум");
                return;
            }

            for (var i = 0; i < responses.Length; i++)
            {
                var response = responses[i];
                if (response is null)
                {
                    notResponded.Add(leftPeers[i]);
                    _logger.Verbose("Узел {NodeId} не вернул ответ", leftPeers[i].Id);
                }
                else if (response.VoteGranted)
                {
                    votes++;
                    _logger.Verbose("Узел {NodeId} отдал голос за", leftPeers[i].Id);
                }
                else if (CurrentTerm < response.CurrentTerm)
                {
                    _logger.Verbose("Узел {NodeId} имеет более высокий Term. Перехожу в состояние Follower",
                        leftPeers[i].Id);
                    _cts.Cancel();

                    var followerState = ConsensusModule.CreateFollowerState();
                    if (ConsensusModule.TryUpdateState(followerState, this))
                    {
                        ConsensusModule.ElectionTimer.Start();
                        ConsensusModule.PersistenceFacade.UpdateState(response.CurrentTerm, null);
                    }

                    return;
                }
                else
                {
                    _logger.Verbose("Узел {NodeId} не отдал голос за", leftPeers[i].Id);
                }
            }

            ( leftPeers, notResponded ) = ( notResponded, leftPeers );
            notResponded.Clear();

            if (leftPeers.Count == 0 && !QuorumReached())
            {
                _logger.Debug(
                    "Кворум не достигнут и нет узлов, которым можно послать запросы. Дожидаюсь завершения таймаута выбора для перехода в следующий терм");
                return;
            }
        }

        _logger.Debug("Кворум собран. Получено {VotesCount} голосов. Посылаю команду перехода в состояние Leader",
            votes);

        if (token.IsCancellationRequested)
        {
            _logger.Debug("Токен был отменен. Кворум не достигнут");
            return;
        }

        var leaderState = ConsensusModule.CreateLeaderState();
        if (ConsensusModule.TryUpdateState(leaderState, this))
        {
            ConsensusModule.HeartbeatTimer.Start();
            ConsensusModule.ElectionTimer.Stop();
        }

        bool QuorumReached()
        {
            return PeerGroup.IsQuorumReached(votes);
        }
    }

    private void OnElectionTimerTimeout()
    {
        ElectionTimer.Timeout -= OnElectionTimerTimeout;

        _logger.Debug("Сработал Election Timeout. Перехожу в новый терм");

        var candidateState = ConsensusModule.CreateCandidateState();
        if (ConsensusModule.TryUpdateState(candidateState, this))
        {
            ConsensusModule.ElectionTimer.Stop();
            ConsensusModule.PersistenceFacade.UpdateState(ConsensusModule.CurrentTerm.Increment(), ConsensusModule.Id);
            ConsensusModule.ElectionTimer.Start();
        }
    }

    public override AppendEntriesResponse Apply(AppendEntriesRequest request)
    {
        if (request.Term < CurrentTerm)
        {
            return AppendEntriesResponse.Fail(CurrentTerm);
        }

        var followerState = ConsensusModule.CreateFollowerState();
        ConsensusModule.TryUpdateState(followerState, this);
        return followerState.Apply(request);
    }

    public override SubmitResponse<TResponse> Apply(SubmitRequest<TCommand> request)
    {
        return SubmitResponse<TResponse>.NotLeader;
    }

    public override RequestVoteResponse Apply(RequestVoteRequest request)
    {
        // Мы в более актуальном Term'е
        if (request.CandidateTerm < CurrentTerm)
        {
            return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: false);
        }

        if (CurrentTerm < request.CandidateTerm)
        {
            var followerState = ConsensusModule.CreateFollowerState();
            if (ConsensusModule.TryUpdateState(followerState, this))
            {
                ConsensusModule.PersistenceFacade.UpdateState(request.CandidateTerm, request.CandidateId);
                ElectionTimer.Start();
            }

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
            !PersistenceFacade.Conflicts(request.LastLogEntryInfo))
        {
            var followerState = ConsensusModule.CreateFollowerState();
            if (ConsensusModule.TryUpdateState(followerState, this))
            {
                ElectionTimer.Start();
                ConsensusModule.PersistenceFacade.UpdateState(request.CandidateTerm, request.CandidateId);
            }

            return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: true);
        }

        // Кандидат только что проснулся и не знает о текущем состоянии дел. 
        // Обновим его
        return new RequestVoteResponse(CurrentTerm: CurrentTerm, VoteGranted: false);
    }

    public override void Dispose()
    {
        try
        {
            _cts.Cancel();
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        ElectionTimer.Timeout -= OnElectionTimerTimeout;
    }

    public override InstallSnapshotResponse Apply(InstallSnapshotRequest request, CancellationToken token)
    {
        if (request.Term < CurrentTerm)
        {
            return new InstallSnapshotResponse(CurrentTerm);
        }

        var followerState = ConsensusModule.CreateFollowerState();
        return followerState.Apply(request, token);
    }
}