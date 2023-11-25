using Consensus.Raft.Persistence;
using Serilog;
using TaskFlux.Models;

namespace Consensus.Raft.Tests.Infrastructure;

public class RaftConsensusModule : RaftConsensusModule<int, int>
{
    internal RaftConsensusModule(NodeId id,
                                 PeerGroup peerGroup,
                                 ILogger logger,
                                 ITimerFactory timerFactory,
                                 IBackgroundJobQueue backgroundJobQueue,
                                 StoragePersistenceFacade persistenceFacade,
                                 ICommandSerializer<int> commandSerializer,
                                 IApplicationFactory<int, int> applicationFactory)
        : base(id, peerGroup, logger, timerFactory, backgroundJobQueue, persistenceFacade,
            commandSerializer, applicationFactory)
    {
    }
}