using Moq;
using Serilog.Core;
using TaskFlux.Consensus.Commands.AppendEntries;
using TaskFlux.Consensus.Commands.InstallSnapshot;
using TaskFlux.Consensus.Commands.RequestVote;
using TaskFlux.Consensus.Persistence;
using TaskFlux.Consensus.Persistence.Log;
using TaskFlux.Consensus.Persistence.LogFileCheckStrategy;
using TaskFlux.Consensus.Persistence.Metadata;
using TaskFlux.Consensus.Persistence.Snapshot;
using TaskFlux.Consensus.Tests.Infrastructure;
using TaskFlux.Consensus.Tests.Stubs;
using TaskFlux.Core;

namespace TaskFlux.Consensus.Tests;

[Trait("Category", "Raft")]
public class CandidateStateTests
{
    private static readonly NodeId NodeId = new(1);
    private static readonly PeerGroup EmptyPeerGroup = new(Array.Empty<IPeer>());

    private static readonly IDeltaExtractor<int> NullDeltaExtractor =
        new Mock<IDeltaExtractor<int>>()
           .Apply(m =>
            {
                var delta = new byte[] {1};
                m.Setup(x => x.TryGetDelta(It.IsAny<int>(), out delta))
                 .Returns(true);
            })
           .Object;

    private static RaftConsensusModule CreateCandidateNode(Term term,
                                                           ITimer? electionTimer = null,
                                                           IBackgroundJobQueue? jobQueue = null,
                                                           IEnumerable<IPeer>? peers = null,
                                                           ILogFileSizeChecker? fileSizeChecker = null,
                                                           IApplicationFactory? applicationFactory = null)
    {
        return CreateCandidateNode(term.Value, electionTimer, jobQueue, peers, fileSizeChecker, applicationFactory);
    }

    private static RaftConsensusModule CreateCandidateNode(long term,
                                                           ITimer? electionTimer = null,
                                                           IBackgroundJobQueue? jobQueue = null,
                                                           IEnumerable<IPeer>? peers = null,
                                                           ILogFileSizeChecker? fileSizeChecker = null,
                                                           IApplicationFactory? applicationFactory = null)
    {
        var facade = CreateStoragePersistenceFacade();
        facade.Metadata.SetupMetadataTest(term, null);
        electionTimer ??= Mock.Of<ITimer>();
        var timerFactory = electionTimer is null
                               ? Helpers.NullTimerFactory
                               : new ConstantTimerFactory(electionTimer);
        jobQueue ??= Mock.Of<IBackgroundJobQueue>();
        var peerGroup = peers != null
                            ? new PeerGroup(peers.ToArray())
                            : EmptyPeerGroup;
        var node = new RaftConsensusModule(NodeId, peerGroup,
            Logger.None, timerFactory, jobQueue,
            facade, NullDeltaExtractor, applicationFactory ?? Helpers.NullApplicationFactory);
        node.SetStateTest(node.CreateCandidateState());
        return node;

        FileSystemPersistenceFacade CreateStoragePersistenceFacade()
        {
            var fs = Helpers.CreateFileSystem();
            var logStorage = SegmentedFileLog.Initialize(fs.DataDirectory, Logger.None);
            var metadataStorage = MetadataFile.Initialize(fs.DataDirectory);
            var snapshotStorage = SnapshotFile.Initialize(fs.DataDirectory);
            if (fileSizeChecker is null)
            {
                return new FileSystemPersistenceFacade(logStorage, metadataStorage, snapshotStorage, Logger.None);
            }

            return new FileSystemPersistenceFacade(logStorage, metadataStorage, snapshotStorage, Logger.None);
        }
    }

    private const int DefaultTerm = 1;

    [Fact]
    public void ElectionTimout__КогдаЕдинственныйВКластере__ДолженПерейтиВСледующийТермИСтатьЛидером()
    {
        var electionTimer = new Mock<ITimer>();
        electionTimer.SetupAdd(x => x.Timeout += null);
        var currentTerm = new Term(1);
        var expectedTerm = currentTerm.Increment();
        var node = CreateCandidateNode(DefaultTerm, electionTimer.Object);

        electionTimer.Raise(x => x.Timeout += null);

        Assert.Equal(expectedTerm, node.CurrentTerm);
        Assert.Equal(NodeRole.Leader, node.CurrentRole);
    }

    [Fact]
    public void ElectionTimeout__КогдаНиктоНеОтдалГолос__ДолженПерейтиВСледующийТермИОстатьсяКандидатом()
    {
        var electionTimer = new Mock<ITimer>();
        electionTimer.SetupAdd(x => x.Timeout += null);
        var currentTerm = new Term(1);
        var expectedTerm = currentTerm.Increment();
        var stubJobQueue = Helpers.NullBackgroundJobQueue;
        var node = CreateCandidateNode(DefaultTerm, electionTimer.Object, jobQueue: stubJobQueue);

        electionTimer.Raise(x => x.Timeout += null);

        Assert.Equal(expectedTerm, node.CurrentTerm);
        Assert.Equal(NodeRole.Leader, node.CurrentRole);
    }

    [Fact]
    public void КогдаДругихУзловНет__ДолженСтатьЛидеромПоТаймауту()
    {
        var oldTerm = new Term(1);
        var timer = new Mock<ITimer>();
        using var node = CreateCandidateNode(oldTerm.Value, electionTimer: timer.Object);

        timer.Raise(t => t.Timeout += null);

        Assert.Equal(NodeRole.Leader, node.CurrentRole);
    }

    [Fact]
    public void Кворум__КогдаЕдинственныйДругойУзелНеОтдалГолос__ДолженОстатьсяКандидатом()
    {
        var queue = new SingleRunBackgroundJobQueue();
        var term = new Term(DefaultTerm);
        using var node = CreateCandidateNode(term, jobQueue: queue,
            peers: new[] {new StubQuorumPeer(new RequestVoteResponse(term, false))});

        queue.Run();

        Assert.Equal(NodeRole.Candidate, node.CurrentRole);
    }

    private static readonly NodeId AnotherNodeId = new(NodeId.Id + 1);

    private class StubQuorumPeer : IPeer
    {
        private readonly RequestVoteResponse _response;
        public NodeId Id => AnotherNodeId;

        public StubQuorumPeer(RequestVoteResponse response)
        {
            _response = response;
        }

        public StubQuorumPeer(Term term, bool voteGranted)
        {
            _response = new RequestVoteResponse(term, voteGranted);
        }

        public AppendEntriesResponse SendAppendEntries(AppendEntriesRequest request, CancellationToken token)
        {
            throw new Exception("Кандидат не должен отсылать AppendEntries");
        }

        public RequestVoteResponse SendRequestVote(RequestVoteRequest request, CancellationToken token)
        {
            return _response;
        }

        public InstallSnapshotResponse SendInstallSnapshot(
            InstallSnapshotRequest request,
            CancellationToken token)
        {
            throw new Exception("Кандидат не должен отсылать InstallSnapshot");
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    public void Кворум__СНесколькимиУзлами__ДолженСтатьЛидеромКогдаСобралКворум(
        int grantedVotesCount,
        int nonGrantedVotesCount)
    {
        // Кворум достигается только если было получено n/2 согласий (округление в нижнюю сторону),
        // где n - кол-во других узлов (без нас)
        var term = new Term(2);
        var peers = Enumerable.Range(0, grantedVotesCount)
                              .Select(_ => new StubQuorumPeer(term, true))
                              .Concat(Enumerable.Range(0, nonGrantedVotesCount)
                                                .Select(_ => new StubQuorumPeer(term, false)));

        var queue = new AwaitingTaskBackgroundJobQueue();
        using var node = CreateCandidateNode(term, jobQueue: queue, peers: peers);

        queue.RunWait();

        Assert.Equal(NodeRole.Leader, node.CurrentRole);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    public void Кворум__СНесколькимиУзлами__ДолженОстатьсяКандидатомЕслиНеСобралКворум(
        int grantedVotesCount,
        int nonGrantedVotesCount)
    {
        var term = new Term(2);
        var peers = Enumerable.Range(0, grantedVotesCount)
                              .Select(_ => new StubQuorumPeer(term, true))
                              .Concat(Enumerable.Range(0, nonGrantedVotesCount)
                                                .Select(_ => new StubQuorumPeer(term, false)));

        var queue = new SingleRunBackgroundJobQueue();
        using var node = CreateCandidateNode(term, jobQueue: queue, peers: peers);

        queue.Run();

        Assert.Equal(NodeRole.Candidate, node.CurrentRole);
    }

    [Fact]
    public void Кворум__КогдаУзелОтветилБольшимТермомИНеОтдалГолос__ДолженСтатьFollower()
    {
        var term = new Term(1);
        var queue = new SingleRunBackgroundJobQueue();
        var newTerm = term.Increment();
        var peer = new StubQuorumPeer(new RequestVoteResponse(newTerm, false));
        using var node = CreateCandidateNode(term, jobQueue: queue, peers: new[] {peer});

        queue.Run();

        Assert.Equal(NodeRole.Follower, node.CurrentRole);
    }

    [Fact]
    public void Кворум__КогдаУзелОтветилБольшимТермомИНеОтдалГолос__ДолженОбноситьТерм()
    {
        var term = new Term(1);
        var queue = new SingleRunBackgroundJobQueue();
        var newTerm = term.Increment();
        var peer = new StubQuorumPeer(new RequestVoteResponse(newTerm, false));
        using var node = CreateCandidateNode(term, jobQueue: queue, peers: new[] {peer});

        queue.Run();

        Assert.Equal(newTerm, node.CurrentTerm);
    }

    [Fact]
    public void RequestVote__СБолееВысокимТермом__ДолженСтатьFollower()
    {
        var term = new Term(2);
        using var node = CreateCandidateNode(term);
        var newTerm = term.Increment();
        var request = new RequestVoteRequest(AnotherNodeId, newTerm, LogEntryInfo.Tomb);
        var response = node.Handle(request);

        Assert.Equal(NodeRole.Follower, node.CurrentRole);
        Assert.True(response.VoteGranted);
        Assert.Equal(newTerm, response.CurrentTerm);
    }

    [Fact]
    public void RequestVote__СБолееВысокимТермом__ДолженОбновитьСвойТерм()
    {
        var term = new Term(2);
        using var node = CreateCandidateNode(term);
        var newTerm = term.Increment();
        var request = new RequestVoteRequest(AnotherNodeId, newTerm, LogEntryInfo.Tomb);
        var response = node.Handle(request);

        Assert.Equal(NodeRole.Follower, node.CurrentRole);
        Assert.True(response.VoteGranted);
        Assert.Equal(newTerm, response.CurrentTerm);
        Assert.Equal(newTerm, node.CurrentTerm);
    }

    [Fact]
    public void Heartbeat__СБолееВысокимТермом__ДолженПерейтиВFollower()
    {
        var term = new Term(2);
        using var node = CreateCandidateNode(term);
        var newTerm = term.Increment();

        var request = AppendEntriesRequest.Heartbeat(newTerm, node.Persistence.CommitIndex, AnotherNodeId,
            node.Persistence.LastEntry);
        var response = node.Handle(request);

        Assert.Equal(NodeRole.Follower, node.CurrentRole);
        Assert.True(response.Success);
        Assert.Equal(newTerm, response.Term);
        Assert.Equal(newTerm, node.CurrentTerm);
    }

    private static LogEntry RandomDataEntry(Term term)
    {
        var data = new byte[Random.Shared.Next(0, 128)];
        Random.Shared.NextBytes(data);
        return new LogEntry(term, data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void AppendEntries__КогдаВЗапросеБылиЗаписиИИндексКоммитаРавенТекущему__НеДолженОбновлятьИндексКоммита(
        int entriesCount)
    {
        var term = new Term(2);
        var oldCommitIndex = 2;
        using var node = CreateCandidateNode(term);
        var requestEntries = Enumerable.Range(0, entriesCount)
                                       .Select(_ => RandomDataEntry(term))
                                       .ToArray();
        node.Persistence.SetupTest(new[]
        {
            RandomDataEntry(1), // 0
            RandomDataEntry(1), // 1
            RandomDataEntry(2), // 2
        });
        node.Persistence.SetCommitTest(oldCommitIndex);

        var response = node.Handle(new AppendEntriesRequest(term, oldCommitIndex, AnotherNodeId,
            node.Persistence.LastEntry, requestEntries));

        Assert.True(response.Success);
        var actualCommitIndex = node.Persistence.CommitIndex;
        Assert.Equal(oldCommitIndex, actualCommitIndex);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(5, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 10)]
    public void AppendEntries__КогдаПредыдущийИндексРавенПоследнемуИндексу__ДолженДобавитьЗаписиВКонец(
        int logSize,
        int entriesCount)
    {
        var term = new Term(2);
        using var node = CreateCandidateNode(term);
        var requestEntries = Enumerable.Range(0, entriesCount)
                                       .Select(_ => RandomDataEntry(term))
                                       .ToArray();
        var logEntries = Enumerable.Range(0, logSize)
                                   .Select(_ => RandomDataEntry(term))
                                   .ToArray();
        node.Persistence.SetupTest(logEntries);
        var request = new AppendEntriesRequest(term, Lsn.Tomb, AnotherNodeId,
            node.Persistence.LastEntry, requestEntries);
        var expectedLog = logEntries.Concat(requestEntries).ToArray();

        var response = node.Handle(request);

        Assert.True(response.Success);
        Assert.Equal(expectedLog, node.Persistence.Log.ReadAllTest(), LogEntryComparer);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(9, 20)]
    [InlineData(1233, 1500)]
    public void AppendEntries__КогдаИндексКоммитаВЗапросеБольшеМоего__ДолженЗакоммититьЗаписи(
        int currentCommitIndex,
        int requestCommitIndex)
    {
        var term = new Term(2);
        var logEntries = Enumerable.Range(0, requestCommitIndex + 1)
                                   .Select(_ => RandomDataEntry(term))
                                   .ToArray();
        using var node = CreateCandidateNode(term);
        node.Persistence.SetupTest(logEntries);
        node.Persistence.SetCommitTest(currentCommitIndex);
        var expectedCommitIndex = requestCommitIndex;

        var request = new AppendEntriesRequest(term, requestCommitIndex, AnotherNodeId, node.Persistence.LastEntry,
            Array.Empty<LogEntry>());
        var response = node.Handle(request);

        Assert.True(response.Success);
        Assert.Equal(expectedCommitIndex, node.Persistence.CommitIndex);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 5)]
    [InlineData(5, 2)]
    public void AppendEntries__КогдаЕстьИндексКоммитаИЗаписиДляДобавления__ДолженЗакоммититьИДобавитьЗаписи(
        int commitIndex,
        int enqueueCount)
    {
        // Изначально ничего не закоммичено
        var term = new Term(2);
        var logEntries = Enumerable.Range(0, 10)
                                   .Select(_ => RandomDataEntry(term))
                                   .ToArray();
        var requestEntries = Enumerable.Range(0, enqueueCount)
                                       .Select(_ => RandomDataEntry(term))
                                       .ToArray();
        var expectedCommitIndex = commitIndex;
        var expectedLog = logEntries.Concat(requestEntries).ToArray();

        using var node = CreateCandidateNode(term);
        node.Persistence.SetupTest(logEntries);

        var request =
            new AppendEntriesRequest(term, commitIndex, AnotherNodeId, node.Persistence.LastEntry, requestEntries);
        var response = node.Handle(request);

        Assert.True(response.Success);
        Assert.Equal(expectedCommitIndex, node.Persistence.CommitIndex);
        Assert.Equal(expectedLog, node.Persistence.Log.ReadAllTest(), LogEntryComparer);
    }

    private static readonly LogEntryEqualityComparer LogEntryComparer = new();

    [Fact]
    public void AppendEntries__КогдаЛогКонфликтует__ДолженОтветитьFalse()
    {
        var term = new Term(5);
        using var node = CreateCandidateNode(term);
        var log = new[]
        {
            RandomDataEntry(1), // 0
            RandomDataEntry(2), // 1
            RandomDataEntry(2), // 2
            RandomDataEntry(3), // 3
            RandomDataEntry(3), // 4
            RandomDataEntry(4), // 5
        };
        node.Persistence.Log.SetupLogTest(log);
        /*
         * Конфликт на 5 записи (индекс 4).
         * Наш терм: 3
         * Терм узла: 4
         */
        var prevLogEntry = new LogEntryInfo(new Term(4), 4);
        var enqueueEntries = new[]
        {
            RandomDataEntry(new Term(4)), RandomDataEntry(new Term(4)), RandomDataEntry(new Term(5)),
        };
        var request = new AppendEntriesRequest(term, 0, AnotherNodeId, prevLogEntry, enqueueEntries);

        var response = node.Handle(request);

        Assert.False(response.Success);
        Assert.Equal(log, node.Persistence.Log.ReadAllTest(), LogEntryComparer);
    }

    private LogEntry RandomDataEntry(int term) => RandomDataEntry(new Term(term));

    [Fact]
    public void RequestVote__СБолееВысокимТермомНоКонфликтующимЛогом__ДолженПерейтиВНовыйТермИНеОтдатьГолос()
    {
        var currentTerm = new Term(2);
        var queue = new SingleRunBackgroundJobQueue();
        using var node = CreateCandidateNode(currentTerm, jobQueue: queue);
        var nodeEntries = new[] {RandomDataEntry(1), RandomDataEntry(2), RandomDataEntry(2), RandomDataEntry(2),};
        node.Persistence.InsertRange(nodeEntries, 0);
        var newTerm = currentTerm.Increment();

        // Конфликт на 1 индексе (2 запись) - наш терм = 2, его терм = 1
        var request = new RequestVoteRequest(AnotherNodeId, newTerm, new LogEntryInfo(new Term(1), 1));
        var response = node.Handle(request);

        Assert.False(response.VoteGranted);
        Assert.Equal(newTerm, node.CurrentTerm);
        Assert.Equal(NodeRole.Follower, node.CurrentRole);
    }

    [Fact]
    public void Heartbeat__СБолееВысокимТермомИСобраннымКворумом__ДолженСтатьFollower()
    {
        /*
         * Когда сначала вызываю Handle (после которого становлюсь Follower),
         * а потом начинаю кворум (собирая при этом большинство голосов),
         * должен остаться Follower
         */
        var currentTerm = new Term(2);
        var queue = new SingleRunBackgroundJobQueue();
        using var node = CreateCandidateNode(currentTerm, jobQueue: queue);
        var newTerm = currentTerm.Increment();

        var request = AppendEntriesRequest.Heartbeat(newTerm, -1, AnotherNodeId, LogEntryInfo.Tomb);
        var response = node.Handle(request);
        queue.Run();

        Assert.True(response.Success);
        Assert.Equal(newTerm, response.Term);

        Assert.Equal(NodeRole.Follower, node.CurrentRole);
        Assert.Equal(newTerm, node.CurrentTerm);
    }

    [Fact]
    public void RequestVote__СОдинаковымТермом__ДолженВернутьFalse()
    {
        var currentTerm = new Term(2);
        var queue = new SingleRunBackgroundJobQueue();
        using var node = CreateCandidateNode(currentTerm, jobQueue: queue);

        var request = new RequestVoteRequest(AnotherNodeId, currentTerm, LogEntryInfo.Tomb);
        var response = node.Handle(request);

        Assert.False(response.VoteGranted);
        Assert.Equal(currentTerm, response.CurrentTerm);

        Assert.Equal(NodeRole.Candidate, node.CurrentRole);
        Assert.Equal(currentTerm, node.CurrentTerm);
    }

    [Fact]
    public void СобранныйКворумПослеПолученногоRequestVoteСБольшимТермом__ДолженОстатьсяFollower()
    {
        /*
         * Когда сначала вызываю Handle (после которого становлюсь Follower),
         * а потом начинаю кворум (собирая при этом большинство голосов),
         * должен остаться Follower
         */
        var currentTerm = new Term(2);
        var queue = new SingleRunBackgroundJobQueue();
        using var node = CreateCandidateNode(currentTerm, jobQueue: queue);
        var newTerm = currentTerm.Increment();

        var request = new RequestVoteRequest(AnotherNodeId, newTerm, LogEntryInfo.Tomb);
        var response = node.Handle(request);

        queue.Run();

        Assert.True(response.VoteGranted);
        Assert.Equal(newTerm, response.CurrentTerm);

        Assert.Equal(NodeRole.Follower, node.CurrentRole);
        Assert.Equal(newTerm, node.CurrentTerm);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void ПослеПереходаВLeader__КогдаКворумСобран__ДолженОстановитьElectionТаймер(int votes)
    {
        var term = new Term(1);
        var queue = new AwaitingTaskBackgroundJobQueue();

        var peer = new Mock<IPeer>();
        peer.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .Returns(new RequestVoteResponse(CurrentTerm: term, VoteGranted: true));

        var electionTimer = new Mock<ITimer>().Apply(m =>
        {
            m.Setup(x => x.Stop())
             .Verifiable();
        });

        var peers = Enumerable.Range(0, votes)
                              .Select(_ => new StubQuorumPeer(term, true))
                              .ToArray();

        using var _ = CreateCandidateNode(term,
            electionTimer: electionTimer.Object,
            jobQueue: queue,
            peers: peers);

        queue.RunWait();

        electionTimer.Verify(x => x.Stop(), Times.Once());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 2)]
    [InlineData(5, 1)]
    [InlineData(5, 2)]
    [InlineData(5, 3)]
    [InlineData(5, 4)]
    [InlineData(5, 5)]
    public void Кворум__КогдаБольшинствоГолосовОтдано__ДолженПерейтиВLeader(
        int successResponses,
        int notResponded)
    {
        var term = new Term(2);
        var peers = Enumerable.Range(0, successResponses)
                              .Select(_ => new StubQuorumPeer(term, true))
                              .Concat(Enumerable.Range(0, notResponded)
                                                .Select(_ => new StubQuorumPeer(new Term(term.Value - 1), false)))
                              .ToArray();
        var queue = new AwaitingTaskBackgroundJobQueue();
        using var node = CreateCandidateNode(term, jobQueue: queue, peers: peers);
        queue.RunWait();
        Assert.Equal(NodeRole.Leader, node.CurrentRole);
    }

    /// <summary>
    /// Реализация фоновой очереди задач, которая при запуске прекращает регистрацию новых.
    /// Нужно для того, чтобы после становления лидером не регистрировать новые,
    /// иначе при запуске (<see cref="RunWait"/>) добавляются новые (переход в лидера) и вызывается исключение.
    /// </summary>
    private class AwaitingTaskBackgroundJobQueue : IBackgroundJobQueue
    {
        private readonly List<(IBackgroundJob Job, CancellationToken Token)> _list = new();
        private bool _sealed;

        public void Accept(IBackgroundJob job, CancellationToken token)
        {
            if (!_sealed)
            {
                _list.Add(( job, token ));
            }
        }

        public void RunWait()
        {
            _sealed = true;
            try
            {
                Task.WaitAll(_list.Select(tuple => Task.Run(() => tuple.Job.Run(tuple.Token), tuple.Token)).ToArray());
            }
            catch (AggregateException e) when (e.InnerException is OperationCanceledException)
            {
            }
        }
    }
}