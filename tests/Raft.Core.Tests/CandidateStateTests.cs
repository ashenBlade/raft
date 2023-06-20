using Moq;
using Raft.Core.Commands.AppendEntries;
using Raft.Core.Commands.RequestVote;
using Raft.Core.Log;
using Raft.Core.Node;

namespace Raft.Core.Tests;

public class CandidateStateTests
{
    private static RaftNode CreateCandidateStateMachine(Term term, NodeId? votedFor, IEnumerable<IPeer>? peers = null, ITimer? electionTimer = null, IJobQueue? jobQueue = null, ILog? log = null)
    {
        var raftStateMachine = Helpers.CreateStateMachine(term, votedFor, peers: peers, electionTimer: electionTimer, heartbeatTimer: null, jobQueue: jobQueue, log: log);
        ( ( INode ) raftStateMachine ).CurrentState = new CandidateState(raftStateMachine, Helpers.NullLogger);
        return raftStateMachine;
    }

    [Fact]
    public void ПриСрабатыванииElectionTimer__ДолженПерейтиВСледующийТерм()
    {
        var oldTerm = new Term(1);
        var electionTimer = new Mock<ITimer>();
        using var raft = CreateCandidateStateMachine(oldTerm, null, electionTimer: electionTimer.Object);
        
        electionTimer.Raise(x => x.Timeout += null);

        var nextTerm = oldTerm.Increment();
        Assert.Equal(nextTerm, raft.CurrentTerm);
    }

    [Fact]
    public void ПриСрабатыванииElectionTimer__ДолженОстатьсяВCandidateСостоянии()
    {
        var oldTerm = new Term(1);
        var electionTimer = new Mock<ITimer>();
        using var raft = CreateCandidateStateMachine(oldTerm, null, electionTimer: electionTimer.Object);
        
        electionTimer.Raise(x => x.Timeout += null);

        Assert.Equal(NodeRole.Candidate, raft.CurrentRole);
    }

    [Fact]
    public async Task КогдаВКластереНетДругихУзлов__ПослеЗапускаКворума__ДолженПерейтиВСостояниеЛидера()
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        using var raft = CreateCandidateStateMachine(oldTerm, null, jobQueue: jobQueue);

        await jobQueue.Run();

        Assert.Equal(NodeRole.Leader, raft.CurrentRole);
    }
    
    [Fact]
    public async Task ПослеЗапускаКворумаИПереходаВСостояниеЛидера__ДолженОстановитьElectionTimeout()
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();
        using var raft = CreateCandidateStateMachine(oldTerm, null, jobQueue: jobQueue, electionTimer: timer.Object);

        await jobQueue.Run();

        timer.Verify(x => x.Stop(), Times.Once());
    }

    // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ПриЗапускеКворума__СЕдинственнымДругимУзлом__ДолженСтатьЛидеромТолькоКогдаДругойУзелОтдалСвойГолос(bool voteGranted)
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();
        
        var peer = new Mock<IPeer>();
        peer.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: voteGranted));
        
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers: new[]{peer.Object},  jobQueue: jobQueue, electionTimer: timer.Object);
        
        await jobQueue.Run();

        if (voteGranted)
        {
            Assert.Equal(NodeRole.Leader, raft.CurrentRole);
        }
        else
        {
            Assert.Equal(NodeRole.Candidate, raft.CurrentRole);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ПриЗапускеКворума__СЕдинственнымДругимУзлом__ДолженПрекратитьОтправлятьЗапросыПослеПолученияОтвета(bool voteGranted)
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();
        
        var peer = new Mock<IPeer>();
        peer.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: voteGranted));
        
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers: new[] {peer.Object}, jobQueue: jobQueue, electionTimer: timer.Object);
        
        await jobQueue.Run();
        
        peer.Verify(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()), Times.Once());
    }
    
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(5, 5)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    public async Task ПриЗапускеКворума__СНесколькимиУзлами__ДолженСтатьЛидеромКогдаСобралКворум(int grantedVotesCount, int nonGrantedVotesCount)
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();


        var peers = Random.Shared.Shuffle(
            Enumerable.Range(0, grantedVotesCount + nonGrantedVotesCount)
                      .Select(i =>
                       {
                           var mock = new Mock<IPeer>();
                           mock.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(),
                                    It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm,
                                    VoteGranted: i < grantedVotesCount));
                           return mock.Object;
                       })
                      .ToArray() );
        
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers: peers, jobQueue: jobQueue, electionTimer: timer.Object);
        
        await jobQueue.Run();
        
        Assert.Equal(NodeRole.Leader, raft.CurrentRole);
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
    public async Task ПриЗапускеКворума__СНесколькимиУзлами__НеДолженСтатьЛидеромКогдаНеСобралКворум(
        int grantedVotesCount,
        int nonGrantedVotesCount)
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();


        var peers = Random.Shared.Shuffle(
            Enumerable.Range(0, grantedVotesCount + nonGrantedVotesCount)
                      .Select(i =>
                       {
                           var mock = new Mock<IPeer>();
                           mock.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm,
                                    VoteGranted: i < grantedVotesCount));
                           return mock.Object;
                       })
                      .ToArray() );
        
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers: peers, jobQueue: jobQueue, electionTimer: timer.Object);
        
        await jobQueue.Run();
        
        Assert.Equal(NodeRole.Candidate, raft.CurrentRole);
    }

    [Fact]
    public async Task ПриЗапускеКворума__СЕдинственнымУзломКоторыйНеОтветил__ДолженСделатьПовторнуюПопытку()
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();

        var mock = new Mock<IPeer>();
        var calledFirstTime = true;
        mock.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequestVoteRequest _, CancellationToken _) =>
             {
                 if (calledFirstTime)
                 {
                     calledFirstTime = false;
                     return null;
                 }
                 return new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: true);
             });
        
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers: new []{mock.Object}, jobQueue: jobQueue, electionTimer: timer.Object);
        
        await jobQueue.Run();
        
        mock.Verify(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
    
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ПриЗапускеКворума__СЕдинственнымУзломКоторыйОтветилТолькоНаВторойПопытке__ДолженСтатьЛидеромТолькоЕслиОтдалГолос(bool voteGranted)
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();

        var mock = new Mock<IPeer>();
        var calledFirstTime = true;
        mock.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequestVoteRequest _, CancellationToken _) =>
             {
                 if (calledFirstTime)
                 {
                     calledFirstTime = false;
                     return null;
                 }
                 return new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: voteGranted);
             });
        
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers: new []{mock.Object}, jobQueue: jobQueue, electionTimer: timer.Object);
        
        await jobQueue.Run();

        Assert.Equal(voteGranted ? NodeRole.Leader : NodeRole.Candidate, raft.CurrentRole);
    }
    
    [Fact]
    public void ПриОбработкеRequestVote__СБолееВысокимТермом__ДолженСтатьFollower()
    {
        var oldTerm = new Term(1);
        using var raft = CreateCandidateStateMachine(oldTerm, null);

        var request = new RequestVoteRequest(CandidateId: new NodeId(2), CandidateTerm: oldTerm.Increment(),
            LastLog: raft.Log.LastLogEntryInfo);

        raft.Handle(request);

        Assert.Equal(NodeRole.Follower, raft.CurrentRole);
    }

    [Fact]
    public void ПриОбработкеRequestVote__СБолееВысокимТермом__ДолженОбновитьСвойТерм()
    {
        var oldTerm = new Term(1);
        using var raft = CreateCandidateStateMachine(oldTerm, null);

        var requestTerm = oldTerm.Increment();
        var request = new RequestVoteRequest(CandidateId: new NodeId(2), CandidateTerm: requestTerm,
            LastLog: raft.Log.LastLogEntryInfo);

        raft.Handle(request);

        Assert.Equal(requestTerm, raft.CurrentTerm);
    }
    
    [Fact]
    public void ПриОбработкеHeartbeat__СБолееВысокимТермом__ДолженПерейтиВFollower()
    {
        var oldTerm = new Term(1);
        using var raft = CreateCandidateStateMachine(oldTerm, null);

        var request = AppendEntriesRequest.Heartbeat(oldTerm.Increment(), raft.Log.CommitIndex,
            new NodeId(2), raft.Log.LastLogEntryInfo);

        raft.Handle(request);

        Assert.Equal(NodeRole.Follower, raft.CurrentRole);
    }
    
    [Fact]
    public void ПриОбработкеHeartbeat__СБолееВысокимТермом__ДолженОбновитьСвойТерм()
    {
        var oldTerm = new Term(1);
        using var raft = CreateCandidateStateMachine(oldTerm, null);

        var leaderTerm = oldTerm.Increment();
        var request = AppendEntriesRequest.Heartbeat(leaderTerm, raft.Log.CommitIndex, new NodeId(2), raft.Log.LastLogEntryInfo);

        raft.Handle(request);

        Assert.Equal(leaderTerm, raft.CurrentTerm);
    }
    
    [Fact]
    public async Task ПриОбработкеHeartbeat__СБолееВысокимТермомИСобраннымКворумом__ДолженСтатьFollower()
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var peer = new Mock<IPeer>();
        peer.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: true));
        using var raft = CreateCandidateStateMachine(oldTerm, null, jobQueue: jobQueue);

        var leaderTerm = oldTerm.Increment();
        var request = AppendEntriesRequest.Heartbeat(leaderTerm, raft.Log.CommitIndex, new NodeId(2), raft.Log.LastLogEntryInfo);

        raft.Handle(request);
        await jobQueue.Run();

        Assert.Equal(NodeRole.Follower, raft.CurrentRole);
    }
    
    [Fact]
    public async Task ПриОбработкеRequestVote__СБолееВысокимТермомИСобраннымКворумом__ДолженСтатьFollower()
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var peer = new Mock<IPeer>();
        peer.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: true));
        using var raft = CreateCandidateStateMachine(oldTerm, null, jobQueue: jobQueue);

        var request = new RequestVoteRequest(CandidateId: new NodeId(2), CandidateTerm: oldTerm.Increment(),
            LastLog: raft.Log.LastLogEntryInfo);

        raft.Handle(request);
        await jobQueue.Run();

        Assert.Equal(NodeRole.Follower, raft.CurrentRole);
    }
    
    
    [Fact]
    public async Task ПриОбработкеRequestVote__СБолееВысокимТермомИСобраннымКворумом__ДолженОбновитьСвойТерм()
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var peer = new Mock<IPeer>();
        var leaderTerm = oldTerm.Increment();
        peer.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: true));
        using var raft = CreateCandidateStateMachine(oldTerm, null, jobQueue: jobQueue);

        var request = new RequestVoteRequest(CandidateId: new NodeId(2), CandidateTerm: leaderTerm,
            LastLog: raft.Log.LastLogEntryInfo);

        raft.Handle(request);
        await jobQueue.Run();

        Assert.Equal(NodeRole.Follower, raft.CurrentRole);
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task ПослеПереходаВLeader__КогдаКворумСобран__ДолженОстановитьElectionТаймер(int votes)
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        
        var peer = new Mock<IPeer>();
        peer.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: true));

        var electionTimer = new Mock<ITimer>().Apply(t =>
        {
            t.Setup(x => x.Stop()).Verifiable();
        });
        
        
        var peers = Enumerable.Range(0, votes)
                              .Select(_ => new Mock<IPeer>()
                                          .Apply(t => t.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(),
                                                            It.IsAny<CancellationToken>()))
                                                       .ReturnsAsync(new RequestVoteResponse(CurrentTerm: oldTerm,
                                                            VoteGranted: true)))
                                          .Object)
                              .ToArray();
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers, electionTimer: electionTimer.Object, jobQueue: jobQueue);

        await jobQueue.Run();
        
        electionTimer.Verify(x => x.Stop(), Times.Once());
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 1)]
    [InlineData(5, 2)]
    [InlineData(5, 3)]
    [InlineData(5, 4)]
    public async Task ПослеОтправкиЗапросов__КворумДостигнутНоНекоторыеУзлыНеОтветили__ДолженПерейтиВСостояниеLeader(int successResponses, int notResponded)
    {
        var oldTerm = new Term(1);
        var jobQueue = new SingleRunJobQueue();
        var timer = new Mock<ITimer>();
        timer.Setup(x => x.Stop()).Verifiable();

        var peers = Random.Shared.Shuffle(
            Enumerable.Range(0, successResponses + notResponded)
                      .Select(i =>
                       {
                           var mock = new Mock<IPeer>();
                           mock.Setup(x => x.SendRequestVote(It.IsAny<RequestVoteRequest>(), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(i < successResponses 
                                                 ? new RequestVoteResponse(CurrentTerm: oldTerm, VoteGranted: true)
                                                 : null);
                           return mock.Object;
                       })
                      .ToArray() );
        
        using var raft = CreateCandidateStateMachine(oldTerm, null, peers: peers, jobQueue: jobQueue, electionTimer: timer.Object);
        
        await jobQueue.Run();
        
        Assert.Equal(NodeRole.Leader, raft.CurrentRole);
    }
}