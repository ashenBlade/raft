using Consensus.Core.Log;
using Consensus.Core.Persistence;

namespace Consensus.Persistence;

/// <summary>
/// Интерфейс хранилище снапшотов машины состояний.
/// По факту, предоставляет интерфейс доступа к файлу.
/// </summary>
public interface ISnapshotStorage
{
    /// <summary>
    /// Создать временный файл снапшота
    /// </summary>
    /// <returns>Поток для записи нового файла</returns>
    public ISnapshotFileWriter CreateTempSnapshotFile();

    /// <summary>
    /// Информация о последней записи лога, которая была применена к снапшоту
    /// </summary>
    /// <remarks><c>null</c> - означает отсуствие снапшота</remarks>
    public LogEntryInfo? LastLogEntry { get; }

    /// <summary>
    /// Сделан ли для машины состояний снапшот
    /// </summary>
    public bool HasSnapshot => LastLogEntry is not null;

    /// <summary>
    /// Получить снапшот, хранящийся в файле на диске
    /// </summary>
    /// <returns>Объект снапшота</returns>
    /// <exception cref="InvalidOperationException">
    /// Файла снапшота не существует
    /// </exception>
    public ISnapshot GetSnapshot();
}