using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using TaskFlux.Serialization.Helpers;

namespace Consensus.Raft.Persistence.Snapshot;

/// <summary>
/// Файловый интерфейс взаимодействия с файлами снапшотов
/// </summary>
public class FileSystemSnapshotStorage : ISnapshotStorage
{
    private readonly IFileInfo _snapshotFile;
    private readonly IDirectoryInfo _temporarySnapshotFileDirectory;

    /// <inheritdoc cref="Constants.Marker"/>
    public const int Marker = Constants.Marker;

    public FileSystemSnapshotStorage(IFileInfo snapshotFile, IDirectoryInfo temporarySnapshotFileDirectory)
    {
        ArgumentNullException.ThrowIfNull(snapshotFile);
        ArgumentNullException.ThrowIfNull(temporarySnapshotFileDirectory);
        _snapshotFile = snapshotFile;
        _temporarySnapshotFileDirectory = temporarySnapshotFileDirectory;
    }

    public ISnapshotFileWriter CreateTempSnapshotFile()
    {
        return new FileSystemSnapshotFileWriter(this);
    }

    /// <summary>
    /// Информация о последней записи лога, которая была применена к снапшоту
    /// </summary>
    /// <remarks><see cref="LogEntryInfo.Tomb"/> - означает отсуствие снапшота</remarks>
    public LogEntryInfo LastLogEntry { get; private set; } = LogEntryInfo.Tomb;

    /// <summary>
    /// Получить снапшот, хранящийся в файле на диске
    /// </summary>
    /// <returns>Объект снапшота</returns>
    /// <exception cref="InvalidOperationException">Файла снапшота не существует</exception>
    public ISnapshot GetSnapshot()
    {
        if (!_snapshotFile.Exists)
        {
            throw new InvalidOperationException("Файла снапшота не существует");
        }

        return new FileSystemSnapshot(_snapshotFile);
    }

    private const long SnapshotStartPosition = sizeof(int)  // Маркер
                                             + sizeof(int)  // Терм
                                             + sizeof(int); // Индекс

    private class FileSystemSnapshotFileWriter : ISnapshotFileWriter
    {
        /// <summary>
        /// Временный файл снапшота
        /// </summary>
        private IFileInfo? _temporarySnapshotFile;

        /// <summary>
        /// Поток нового файла снапшота.
        /// Создается во время вызова <see cref="Initialize"/>
        /// </summary>
        private Stream? _temporarySnapshotFileStream;

        /// <summary>
        /// Значение LogEntry, которое было записано в файл снапшота
        /// </summary>
        private LogEntryInfo? _writtenLogEntry;

        /// <summary>
        /// Родительский объект хранилища снапшота.
        /// Для него это все и замутили - ему нужно обновить файл снапшота
        /// </summary>
        private FileSystemSnapshotStorage _parent;

        private enum SnapshotFileState
        {
            /// <summary>
            /// Только начинается работа
            /// </summary>
            Start = 0,

            /// <summary>
            /// Записан заголовок
            /// </summary>
            Initialized = 1,

            /// <summary>
            /// Работа с файлом закончена (вызваны <see cref="FileSystemSnapshotFileWriter.Save"/> или <see cref="FileSystemSnapshotFileWriter.Discard"/>
            /// </summary>
            Finished = 2,
        }

        /// <summary>
        /// Состояние файла снапшота во время его создания (записи в него)
        /// </summary>
        private SnapshotFileState _state = SnapshotFileState.Start;

        public FileSystemSnapshotFileWriter(FileSystemSnapshotStorage parent)
        {
            _parent = parent;
        }

        public void Initialize(LogEntryInfo lastApplied)
        {
            switch (_state)
            {
                case SnapshotFileState.Initialized:
                    throw new InvalidOperationException(
                        "Повторная попытка записать заголовок файла снапшота: файл уже инициализирован");
                case SnapshotFileState.Finished:
                    throw new InvalidOperationException(
                        "Повторная попытка записать заголовок файла снапшота: работа с файлом уже завершена");
                case SnapshotFileState.Start:
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(_state), ( int ) _state, typeof(SnapshotFileState));
            }

            // 1. Создаем новый файл снапшота
            var (file, stream) = CreateAndOpenTemporarySnapshotFile();

            var writer = new StreamBinaryWriter(stream);

            // 2. Записываем маркер файла
            writer.Write(Marker);

            // 3. Записываем заголовок основной информации
            writer.Write(lastApplied.Index);
            writer.Write(lastApplied.Term.Value);

            _state = SnapshotFileState.Initialized;

            _writtenLogEntry = lastApplied;
            _temporarySnapshotFileStream = stream;
            _temporarySnapshotFile = file;
        }

        private (IFileInfo File, Stream Stream) CreateAndOpenTemporarySnapshotFile()
        {
            var tempDir = _parent._temporarySnapshotFileDirectory;
            while (true)
            {
                var file = tempDir.FileSystem.FileInfo.New(GetRandomTempFileName());
                try
                {
                    var stream = file.Open(FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    return ( file, stream );
                }
                catch (IOException)
                {
                }
            }

            string GetRandomTempFileName()
            {
                var fileName = Path.GetRandomFileName();
                return Path.Combine(tempDir.FullName, fileName);
            }
        }

        public void Save()
        {
            switch (_state)
            {
                case SnapshotFileState.Start:
                    throw new InvalidOperationException("Попытка сохранения неинициализированного файла снапшота");
                case SnapshotFileState.Finished:
                    throw new InvalidOperationException(
                        "Нельзя повторно сохранить файл снапшота: работа с файлом закончена");
                case SnapshotFileState.Initialized:
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(_state), ( int ) _state, typeof(SnapshotFileState));
            }

            Debug.Assert(_temporarySnapshotFileStream is not null, "Поток временного файла не должен быть null");
            Debug.Assert(_temporarySnapshotFile is not null, "Объект временного файла не должен быть null");
            Debug.Assert(_writtenLogEntry is not null,
                "Объект информации последней команды снапшота не должен быть null");

            // 1. Сбрасываем все данные на диск 
            _temporarySnapshotFileStream.Flush();

            // 3. Переименовываем новый
            _temporarySnapshotFile.MoveTo(_parent._snapshotFile.FullName, true);

            _temporarySnapshotFileStream.Close();

            _parent.LastLogEntry = _writtenLogEntry.Value;

            _state = SnapshotFileState.Finished;
        }

        public void Discard()
        {
            switch (_state)
            {
                case SnapshotFileState.Finished:
                    return;
                case SnapshotFileState.Start:
                case SnapshotFileState.Initialized:
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(_state), ( int ) _state, typeof(SnapshotFileState));
            }

            _state = SnapshotFileState.Finished;

            _temporarySnapshotFileStream?.Close();
            _temporarySnapshotFileStream = null;

            _temporarySnapshotFile?.Delete();
            _temporarySnapshotFile = null;

            _parent = null!;
        }

        public void WriteSnapshot(ISnapshot snapshot, CancellationToken token)
        {
            switch (_state)
            {
                case SnapshotFileState.Start:
                    throw new InvalidOperationException("Попытка записи снапшота в неинициализированный файл");
                case SnapshotFileState.Finished:
                    throw new InvalidOperationException(
                        "Нельзя записывать данные в файл снапшота: работа с файлом закончена");
                case SnapshotFileState.Initialized:
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(_state), ( int ) _state, typeof(SnapshotFileState));
            }

            Debug.Assert(_temporarySnapshotFileStream is not null, "Поток файла снапшота не должен быть null");

            snapshot.WriteTo(_temporarySnapshotFileStream, token);
        }
    }

    private class FileSystemSnapshot : ISnapshot
    {
        private readonly IFileInfo _snapshotFile;

        public FileSystemSnapshot(IFileInfo snapshotFile)
        {
            _snapshotFile = snapshotFile;
        }

        public void WriteTo(Stream destination, CancellationToken token = default)
        {
            using var fileStream = _snapshotFile.OpenRead();
            fileStream.Position = SnapshotStartPosition;
            destination.CopyTo(destination);
        }
    }

    // Для тестов
    internal (int LastIndex, Term LastTerm, byte[] SnapshotData) ReadAllDataTest()
    {
        var stream = _snapshotFile.OpenRead();
        var reader = new StreamBinaryReader(stream);

        var marker = reader.ReadInt32();
        Debug.Assert(marker == Marker);

        var index = reader.ReadInt32();
        var term = new Term(reader.ReadInt32());

        // Читаем до конца
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ( index, term, memory.ToArray() );
    }

    internal void WriteSnapshotDataTest(Term lastTerm, int lastIndex, ISnapshot snapshot)
    {
        using var stream = _snapshotFile.OpenWrite();
        var writer = new StreamBinaryWriter(stream);

        writer.Write(Marker);
        writer.Write(lastIndex);
        writer.Write(lastTerm.Value);
        snapshot.WriteTo(stream);

        LastLogEntry = new LogEntryInfo(lastTerm, lastIndex);
    }
}