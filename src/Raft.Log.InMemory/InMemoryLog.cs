using Raft.Core.Log;

namespace Raft.Log.InMemory;

public class InMemoryLog: ILog
{
    private List<LogEntry> _log;
    public IReadOnlyList<LogEntry> Entries => _log;

    public void AppendUpdateRange(IEnumerable<LogEntry> entries, int startIndex)
    {
        if (_log.Count < startIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex,
                "Размер лога меньше начального индекса добавления новых записей");
        }

        // Стартовый индекс может указывать на конец лога, тогда просто добавим новые записи
        if (startIndex == _log.Count)
        {
            _log.AddRange(entries);
        }
        else
        {
            // В противном случае, нужно не только добавить новые записи, 
            // но и обновить сам лог, т.к. добавленные записи, должны быть последними

            // Новый записи полностью входят в старые записи
            // TODO: оптимизировать
            // var previous = _log.GetRange(0, startIndex);
            // _log.Take(startIndex).Concat(entries).ToList()
            // previous.AddRange(entries.ToArray());
            _log = _log.Take(startIndex - 1)
                       .Concat(entries)
                       .ToList();
        }
    }

    public LogEntryInfo Append(LogEntry entry)
    {
        _log.Add(entry);
        return new LogEntryInfo(entry.Term, _log.Count - 1);
    }
    
    public InMemoryLog(IEnumerable<LogEntry> entries)
    {
        _log = new(entries);
    }

    public InMemoryLog()
    {
        _log = new();
    }
    
    public bool IsConsistentWith(LogEntryInfo prefix)
    {
        if (prefix.IsTomb)
        {
            // Лог отправителя был изначально пуст
            return true;
        }

        if (prefix.Index < Entries.Count && // Наш лог не меньше (используется PrevLogEntry, поэтому нет +1)
            prefix.Term == Entries[prefix.Index].Term) // Термы записей одинковые
        {
            return true;
        }
        
        return false;
    }

    public LogEntryInfo LastEntry => _log.Count > 0
                                         ? new LogEntryInfo(_log[^1].Term, _log.Count - 1)
                                         : LogEntryInfo.Tomb;
    public int CommitIndex { get; set; }
    public int LastApplied { get; set; }
    
    public LogEntry this[int index] => _log[index];

    public IReadOnlyList<LogEntry> GetFrom(int index)
    {
        if (_log.Count < index)
        {
            return Array.Empty<LogEntry>();
        }

        if (index == LogEntryInfo.Tomb.Index)
        {
            return _log;
        }
        
        return _log.GetRange(index, _log.Count - index);
    }

    public void Commit(int index)
    {
        CommitIndex = index;
    }

    public LogEntryInfo GetPrecedingEntryInfo(int nextIndex)
    {
        if (nextIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nextIndex), nextIndex, "Следующий индекс для отправки должен быть только положительными");
        }

        if (nextIndex == 0)
        {
            return LogEntryInfo.Tomb;
        }

        if (nextIndex == LogEntryInfo.Tomb.Index)
        {
            return LogEntryInfo.Tomb;
        }

        if (nextIndex == _log.Count + 1)
        {
            return new(_log[^1].Term, nextIndex - 1);
        }

        return new( _log[nextIndex - 1].Term, nextIndex - 1 );
    }
}