namespace Raft.Core.Log;

public readonly record struct LogEntryInfo(Term Term, int Index)
{
    private const int TombIndex = -1;
    
    /// <summary>
    /// Пустая запись.
    /// Используется когда в логе нет записей, чтобы не вводить <c>null</c>
    /// </summary>
    public static readonly LogEntryInfo Tomb = new(Term.Start, TombIndex);
    
    /// <summary>
    /// Является ли запись меткой пустого лога.
    /// Такая запись сигнализирует о том, что в логе нет записей
    /// </summary>
    public bool IsTomb => Index == TombIndex;
}