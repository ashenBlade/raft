using Consensus.Core.Log;
using TaskFlux.Core;

namespace Consensus.Core.Commands.AppendEntries;

public record AppendEntriesRequest(Term Term,
                                   int LeaderCommit,
                                   NodeId LeaderId,
                                   LogEntryInfo PrevLogEntryInfo,
                                   IReadOnlyList<LogEntry> Entries)
{
    public static AppendEntriesRequest Heartbeat(Term term,
                                                 int leaderCommit,
                                                 NodeId leaderId,
                                                 LogEntryInfo prevLogEntryInfo) => new(term, leaderCommit,
        leaderId, prevLogEntryInfo, Array.Empty<LogEntry>());
}