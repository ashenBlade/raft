using Consensus.Core;
using Consensus.Core.Log;
using Consensus.Persistence.Snapshot;
using Moq;

namespace Consensus.Persistence.Tests;

[Trait("Category", "Raft")]
public class StoragePersistenceManagerTests
{
    private static LogEntry EmptyLogEntry(int term) => new(new Term(term), Array.Empty<byte>());

    [Fact]
    public void Append__СПустымЛогом__ДолженДобавитьЗаписьВБуферВПамяти()
    {
        var buffer = new List<LogEntry>();
        var log = new StoragePersistenceManager(Helpers.NullStorage, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        var entry = EmptyLogEntry(1);
        log.Append(entry);

        Assert.Contains(buffer, e => e == entry);
    }

    [Fact]
    public void Append__СПустымЛогом__НеДолженЗаписыватьЗаписьВLogStorage()
    {
        var mock = new Mock<ILogStorage>();
        mock.Setup(s => s.Append(It.IsAny<LogEntry>())).Verifiable();
        mock.Setup(s => s.AppendRange(It.IsAny<IEnumerable<LogEntry>>())).Verifiable();

        var log = new StoragePersistenceManager(mock.Object, NullMetadataStorage.Instance,
            NullSnapshotStorage.Instance);
        log.Append(EmptyLogEntry(1));

        mock.Verify(x => x.Append(It.IsAny<LogEntry>()), Times.Never());
        mock.Verify(x => x.AppendRange(It.IsAny<IEnumerable<LogEntry>>()), Times.Never());
    }

    [Fact]
    public void Append__СПустымЛогом__ДолженВернутьПравильнуюЗапись()
    {
        var entry = new LogEntry(new Term(1), new byte[] {1, 2, 3, 4});
        var expected = new LogEntryInfo(entry.Term, 0);
        var log = new StoragePersistenceManager(Mock.Of<ILogStorage>(), NullMetadataStorage.Instance,
            NullSnapshotStorage.Instance);

        var actual = log.Append(entry);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Append__КогдаВБуфереЕстьЭлементы__ДолженВернутьПравильнуюЗапись()
    {
        var buffer = new List<LogEntry>()
        {
            new(new Term(1), new byte[] {1, 2, 3}), new(new Term(2), new byte[] {4, 5, 6}),
        };
        var entry = new LogEntry(new Term(3), new byte[] {7, 8, 9});
        var expected = new LogEntryInfo(entry.Term, 2);
        var log = new StoragePersistenceManager(Mock.Of<ILogStorage>(), NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        var actual = log.Append(entry);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Append__КогдаБуферПустНоВХранилищеЕстьЭлементы__ДолженВернутьПравильнуюЗапись()
    {
        var storageMock = new Mock<ILogStorage>();
        var storageSize = 4;
        storageMock.SetupGet(x => x.Count).Returns(storageSize);
        var entry = new LogEntry(new Term(3), new byte[] {7, 8, 9});
        var expected = new LogEntryInfo(entry.Term, storageSize);
        var log = new StoragePersistenceManager(storageMock.Object, NullMetadataStorage.Instance,
            NullSnapshotStorage.Instance);
        var actual = log.Append(entry);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Append__КогдаВБуфереИХранилищеЕстьЭлементы__ДолженВернутьПравильнуюЗапись()
    {
        var buffer = new List<LogEntry>() {EmptyLogEntry(2), EmptyLogEntry(2), EmptyLogEntry(10),};
        var storageMock = new Mock<ILogStorage>();
        var storageSize = 4;
        storageMock.SetupGet(x => x.Count).Returns(storageSize);
        var entry = new LogEntry(new Term(11), new byte[] {7, 8, 9});
        var expected = new LogEntryInfo(entry.Term, storageSize + buffer.Count);
        var log = new StoragePersistenceManager(storageMock.Object, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        var actual = log.Append(entry);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void Commit__СЭлементамиВБуфере__ДолженЗаписатьЗаписиВLogStorage(int entriesCount)
    {
        var mock = new Mock<ILogStorage>();
        var entries = Enumerable.Range(1, entriesCount)
                                .Select(EmptyLogEntry)
                                .ToArray();

        mock.Setup(x => x.AppendRange(Match.Create<IEnumerable<LogEntry>>(c => c.SequenceEqual(entries))))
            .Verifiable();

        var log = new StoragePersistenceManager(mock.Object, NullMetadataStorage.Instance,
            NullSnapshotStorage.Instance);
        log.InsertRange(entries, 0);
        log.Commit(entries.Length - 1);

        mock.Verify(x => x.AppendRange(Match.Create<IEnumerable<LogEntry>>(c => c.SequenceEqual(entries))),
            Times.Once());
    }

    [Fact]
    public void Commit__КогдаВБуфере1ЭлементХранилищеПусто__ДолженЗаписатьЭлементВХранилище()
    {
        var entry = new LogEntry(new Term(1), Array.Empty<byte>());
        var mock = new Mock<ILogStorage>();
        mock.Setup(x =>
                 x.AppendRange(Match.Create<IEnumerable<LogEntry>>(entries => entries.SequenceEqual(new[] {entry}))))
            .Verifiable();
        mock.SetupGet(x => x.Count)
            .Returns(0);

        var log = new StoragePersistenceManager(mock.Object, NullSnapshotStorage.Instance,
            new List<LogEntry>() {entry}, NullMetadataStorage.Instance);

        log.Commit(0);

        mock.Verify(x =>
                x.AppendRange(Match.Create<IEnumerable<LogEntry>>(entries => entries.SequenceEqual(new[] {entry}))),
            Times.Once());
    }

    [Theory]
    [InlineData(6, 3)]
    [InlineData(6, 4)]
    [InlineData(6, 0)]
    [InlineData(6, 5)]
    [InlineData(1, 0)]
    [InlineData(10, 5)]
    [InlineData(10, 9)]
    [InlineData(10, 3)]
    public void Commit__СНеПустымБуфером__ДолженУдалитьЗаписанныеВЛогЗаписи(int elementsCount, int index)
    {
        var buffer = Enumerable.Range(1, elementsCount)
                               .Select(EmptyLogEntry)
                               .ToList();

        // Пропускаем index + 1 элементов, т.к. индексация начинается с 0
        var expected = buffer.Skip(index + 1)
                             .ToList();

        var log = new StoragePersistenceManager(Helpers.NullStorage, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);
        log.Commit(index);

        Assert.Equal(expected, buffer, LogEntryEqualityComparer.Instance);
    }

    [Fact]
    public void GetFrom__КогдаЗаписиПолностьюВПамяти__ДолженВернутьХранившиесяЗаписиВБуфере()
    {
        var buffer = new List<LogEntry>()
        {
            EmptyLogEntry(1),
            EmptyLogEntry(2),
            EmptyLogEntry(3),
            EmptyLogEntry(4),
            EmptyLogEntry(5),
        };
        var expected = buffer.Skip(2).ToList();
        var index = 2;
        var log = new StoragePersistenceManager(Helpers.NullStorage, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        var actual = log.GetFrom(index);

        Assert.Equal(expected, actual, LogEntryEqualityComparer.Instance);
    }

    [Theory]
    [InlineData(3, 3, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(4, 1, 3)]
    [InlineData(4, 1, 2)]
    [InlineData(4, 1, 1)]
    [InlineData(4, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(10, 10, 9)]
    [InlineData(10, 10, 8)]
    [InlineData(10, 10, 7)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    public void GetFrom__КогдаЧастьЗаписейВБуфереЧастьВLogStorage__ДолженВернутьТребуемыеЗаписи(
        int storageCount,
        int bufferCount,
        int index)
    {
        var storage = Enumerable.Range(1, storageCount)
                                .Select(EmptyLogEntry)
                                .ToList();
        var buffer = Enumerable.Range(storageCount + 1, bufferCount)
                               .Select(EmptyLogEntry)
                               .ToList();

        var expected = storage.Concat(buffer)
                              .Skip(index + 1)
                              .ToList();

        var mock = new Mock<ILogStorage>();
        mock.Setup(x => x.ReadFrom(index)).Returns(storage.Skip(index + 1).ToList());
        mock.SetupGet(x => x.Count).Returns(storageCount);

        var log = new StoragePersistenceManager(mock.Object, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        var actual = log.GetFrom(index);

        Assert.Equal(expected, actual, LogEntryEqualityComparer.Instance);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(5, 2)]
    [InlineData(2, 5)]
    [InlineData(5, 5)]
    public void GetFrom__КогдаБуферИLogStorageНеПустыИндекс0__ДолженВернутьВсеЗаписи(int storageCount, int bufferCount)
    {
        var storage = Enumerable.Range(1, storageCount)
                                .Select(EmptyLogEntry)
                                .ToList();
        var buffer = Enumerable.Range(storageCount + 1, bufferCount)
                               .Select(EmptyLogEntry)
                               .ToList();

        var expected = storage.Concat(buffer)
                              .ToList();

        var mock = new Mock<ILogStorage>();
        mock.Setup(x => x.ReadFrom(0)).Returns(storage);
        mock.SetupGet(x => x.Count).Returns(storageCount);

        var log = new StoragePersistenceManager(mock.Object, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        var actual = log.GetFrom(0);

        Assert.Equal(expected, actual, LogEntryEqualityComparer.Instance);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void GetPrecedingEntryInfo__КогдаЗаписиВБуфере__ДолженВернутьТребуемуюЗапись(int bufferCount)
    {
        var buffer = Enumerable.Range(1, bufferCount)
                               .Select(EmptyLogEntry)
                               .ToList();

        var log = new StoragePersistenceManager(Helpers.NullStorage, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        for (int nextIndex = 1; nextIndex <= bufferCount; nextIndex++)
        {
            var expected = new LogEntryInfo(buffer[nextIndex - 1].Term, nextIndex - 1);
            var actual = log.GetPrecedingEntryInfo(nextIndex);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void GetPrecedingEntryInfo__КогдаВсеЗаписиВХранилище__ДолженВернутьТребуемуюЗапись(int storageCount)
    {
        var storage = Enumerable.Range(1, storageCount)
                                .Select(EmptyLogEntry)
                                .ToList();

        var mock = new Mock<ILogStorage>();

        mock.Setup(x => x.GetInfoAt(It.IsAny<int>()))
            .Returns<int>((index) => new LogEntryInfo(storage[index].Term, index));
        mock.SetupGet(x => x.Count).Returns(storageCount);

        var log = new StoragePersistenceManager(mock.Object, NullMetadataStorage.Instance,
            NullSnapshotStorage.Instance);

        for (int nextIndex = 1; nextIndex <= storageCount; nextIndex++)
        {
            var expected = new LogEntryInfo(storage[nextIndex - 1].Term, nextIndex - 1);
            var actual = log.GetPrecedingEntryInfo(nextIndex);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 5)]
    [InlineData(5, 1)]
    [InlineData(1, 5)]
    public void GetPrecedingEntryInfo__КогдаЗаписьНаГраницеХранилищаИБуфера__ДолженВернутьТребуемуюЗапись(
        int storageCount,
        int bufferCount)
    {
        var storage = Enumerable.Range(1, storageCount)
                                .Select(EmptyLogEntry)
                                .ToList();
        var buffer = Enumerable.Range(storageCount + 1, bufferCount)
                               .Select(EmptyLogEntry)
                               .ToList();

        var mock = new Mock<ILogStorage>();
        mock.Setup(x => x.GetInfoAt(It.IsAny<int>()))
            .Returns<int>(index => new LogEntryInfo(storage[index].Term, index));
        mock.SetupGet(x => x.Count).Returns(storageCount);

        var expected = new LogEntryInfo(storage[^1].Term, storageCount - 1);
        var nextIndex = storageCount;

        var log = new StoragePersistenceManager(mock.Object, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        var actual = log.GetPrecedingEntryInfo(nextIndex);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void InsertRange__ВКонецЛогаСПустымБуфером__ДолженДобавитьЗаписиВБуфер(int elementsCount)
    {
        var expected = Enumerable.Range(1, elementsCount)
                                 .Select(EmptyLogEntry)
                                 .ToList();

        var buffer = new List<LogEntry>();

        var log = new StoragePersistenceManager(Helpers.NullStorage, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        log.InsertRange(expected, 0);

        Assert.Equal(expected, buffer);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(3, 3)]
    [InlineData(2, 4)]
    [InlineData(2, 1)]
    [InlineData(5, 1)]
    [InlineData(1, 5)]
    public void InsertRange__ВКонецЛогаСНеПустымБуфером__ДолженДобавитьЗаписиВБуфер(int bufferCount, int elementsCount)
    {
        var buffer = Enumerable.Range(1, bufferCount)
                               .Select(EmptyLogEntry)
                               .ToList();

        var toInsert = Enumerable.Range(bufferCount + 1, elementsCount)
                                 .Select(EmptyLogEntry)
                                 .ToList();

        var expected = buffer.Concat(toInsert)
                             .ToList();

        var log = new StoragePersistenceManager(Helpers.NullStorage, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        log.InsertRange(toInsert, bufferCount);

        Assert.Equal(expected, buffer);
    }

    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(5, 5, 3)]
    [InlineData(5, 5, 2)]
    [InlineData(5, 5, 1)]
    [InlineData(5, 2, 1)]
    [InlineData(5, 1, 1)]
    [InlineData(5, 4, 1)]
    [InlineData(1, 4, 0)]
    [InlineData(3, 4, 0)]
    [InlineData(4, 4, 0)]
    [InlineData(6, 4, 0)]
    [InlineData(6, 4, 5)]
    [InlineData(10, 4, 5)]
    public void InsertRange__ВнутрьНеПустогоЛога__ДолженВставитьИЗатеретьСтарыеЗаписи(
        int bufferCount,
        int toInsertCount,
        int insertIndex)
    {
        var buffer = Enumerable.Range(1, bufferCount)
                               .Select(EmptyLogEntry)
                               .ToList();

        var toInsert = Enumerable.Range(bufferCount + 1, toInsertCount)
                                 .Select(EmptyLogEntry)
                                 .ToList();

        var expected = buffer.Take(insertIndex)
                             .Concat(toInsert)
                             .ToList();

        var log = new StoragePersistenceManager(Helpers.NullStorage, NullSnapshotStorage.Instance, buffer,
            NullMetadataStorage.Instance);

        log.InsertRange(toInsert, insertIndex);

        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void SaveSnapshot__КогдаФайлаСнапшотаНеБыло__ДолженСоздатьНовыйФайлСнапшота()
    {
        var (_, snapshot, tempDir) = Helpers.CreateBaseMockFileSystem(createEmptySnapshotFile: false);

        var manager =
            new StoragePersistenceManager(Helpers.NullStorage, NullMetadataStorage.Instance,
                new FileSystemSnapshotStorage(snapshot, tempDir));

        var entry = new LogEntryInfo(new Term(1), 1);
        var data = new byte[] {1, 2, 3};

        manager.SaveSnapshot(entry, new StubSnapshot(data));

        var fileSystemStream = snapshot.OpenRead();
        var (actualLastEntry, actualData) = Helpers.ReadSnapshot(fileSystemStream);
        Assert.Equal(entry, actualLastEntry);
        Assert.Equal(data, actualData);
    }

    [Fact]
    public void SaveSnapshot__КогдаФайлСнапшотаСуществовалПустой__ДолженПерезаписатьСтарыйФайл()
    {
        var (_, snapshot, tempDir) = Helpers.CreateBaseMockFileSystem(createEmptySnapshotFile: true);
        var entry = new LogEntryInfo(new Term(1), 1);
        var data = new byte[] {1, 2, 4};

        var manager =
            new StoragePersistenceManager(Helpers.NullStorage, NullMetadataStorage.Instance,
                new FileSystemSnapshotStorage(snapshot, tempDir));
        manager.SaveSnapshot(entry, new StubSnapshot(data));

        var fileSystemStream = snapshot.OpenRead();
        var (actualLastEntry, actualData) = Helpers.ReadSnapshot(fileSystemStream);
        Assert.Equal(entry, actualLastEntry);
        Assert.Equal(data, actualData);
    }

    [Fact]
    public void SaveSnapshot__КогдаФайлСнапшотаСуществовалСДанными__ДолженПерезаписатьСтарыйФайл()
    {
        var (_, snapshot, tempDir) = Helpers.CreateBaseMockFileSystem(createEmptySnapshotFile: true);
        var originalData = new byte[] {123, 123, 4, 1, 65, 86, 035, 37, 75};
        using (var s = snapshot.OpenWrite())
        {
            s.Write(originalData);
        }

        var entry = new LogEntryInfo(new Term(1), 1);
        var data = new byte[] {1, 2, 4};

        var manager =
            new StoragePersistenceManager(Helpers.NullStorage, NullMetadataStorage.Instance,
                new FileSystemSnapshotStorage(snapshot, tempDir));
        manager.SaveSnapshot(entry, new StubSnapshot(data));

        var fileSystemStream = snapshot.OpenRead();
        var (actualLastEntry, actualData) = Helpers.ReadSnapshot(fileSystemStream);
        Assert.Equal(entry, actualLastEntry);
        Assert.Equal(data, actualData);
    }
}