using TaskFlux.Models;
using TaskFlux.Models.TestHelpers;
using TaskFlux.Serialization;
using TaskQueue.Core;

namespace TaskQueue.Serialization.Tests;

[Trait("Category", "Serialization")]
public class FileTaskQueueSnapshotSerializerTests
{
    public static readonly FileTaskQueueSnapshotSerializer Serializer = new(new StubTaskQueueFactory());

    private static void AssertBase(StubTaskQueue queue)
    {
        var stream = new MemoryStream();
        Serializer.Serialize(stream, new[] {queue});
        stream.Position = 0;
        var actual = Serializer.Deserialize(stream).Single();
        Assert.Equal(queue, actual, TaskQueueEqualityComparer.Instance);
    }

    private static void AssertBase(IEnumerable<StubTaskQueue> queues)
    {
        var stream = new MemoryStream();
        var expected = queues.ToHashSet();
        Serializer.Serialize(stream, expected);
        stream.Position = 0;
        var actual = Serializer.Deserialize(stream).ToHashSet();
        Assert.Equal(expected, actual, TaskQueueEqualityComparer.Instance);
    }

    private static readonly QueueName DefaultName = QueueNameParser.Parse("hello");

    private static IEnumerable<(long, byte[])> EmptyQueueData => Enumerable.Empty<(long, byte[])>();

    [Fact]
    public void Serialize__КогдаПереданаПустаяОчередьБезПредела()
    {
        AssertBase(new StubTaskQueue(DefaultName, 0, null, null, EmptyQueueData));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    [InlineData(100)]
    [InlineData(128)]
    [InlineData(1000)]
    [InlineData(98765423)]
    public void Serialize__КогдаПереданаПустаяОчередьСПределом(int limit)
    {
        AssertBase(new StubTaskQueue(DefaultName, limit, null, null, EmptyQueueData));
    }

    [Theory]
    [InlineData(0, new byte[0])]
    [InlineData(1, new byte[] {123})]
    [InlineData(-1, new[] {byte.MaxValue})]
    [InlineData(long.MaxValue, new byte[] {byte.MaxValue, 0, 0, 0, 0, 0, 0, 0})]
    [InlineData(long.MinValue, new byte[] {5, 65, 22, 75, 97, 32, 200})]
    [InlineData(123123, new byte[] {byte.MaxValue, 1, 2, 3, 4, 5, 6, byte.MinValue})]
    public void Serialize__КогдаПереданаОчередьС1ЭлементомБезПредела(long priority, byte[] data)
    {
        AssertBase(new StubTaskQueue(DefaultName, null, null, null, new[] {( priority, data )}));
    }

    private static readonly Random Random = new Random(87);

    private IEnumerable<(long, byte[])> CreateRandomQueueElements(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var buffer = new byte[Random.Next(0, 100)];
            Random.NextBytes(buffer);
            yield return ( Random.NextInt64(), buffer );
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(128)]
    public void Serialize__КогдаЭлементовНесколько(int count)
    {
        AssertBase(new StubTaskQueue(DefaultName, 0, null, null, CreateRandomQueueElements(count)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(100)]
    public void Serialize__КогдаПереданоНесколькоПустыхОчередей__ДолженПравильноДесериализовать(int count)
    {
        var queues = Enumerable.Range(0, count)
                               .Select(_ =>
                                    new StubTaskQueue(QueueNameHelpers.CreateRandomQueueName(), 0,
                                        null, null,
                                        Array.Empty<(long, byte[])>()));
        AssertBase(queues);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void Serialize__КогдаПереданоНесколькоНеПустыхОчередей__ДолженПравильноДесериализовать(int count)
    {
        var queues = Enumerable.Range(0, count)
                               .Select(_ => new StubTaskQueue(QueueNameHelpers.CreateRandomQueueName(), 0,
                                    null, null,
                                    CreateRandomQueueElements(Random.Next(0, 255))));
        AssertBase(queues);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void Serialize__КогдаПереданоНесколькоОграниченныхОчередей__ДолженПравильноДесериализовать(
        int count)
    {
        var queues = Enumerable.Range(0, count)
                               .Select(_ =>
                                {
                                    var limit = Random.Next(0, 255);
                                    var data = CreateRandomQueueElements(Random.Next(0, limit));
                                    return new StubTaskQueue(QueueNameHelpers.CreateRandomQueueName(), limit, null,
                                        null,
                                        data);
                                });
        AssertBase(queues);
    }

    [Theory]
    [InlineData(0L, 3L)]
    [InlineData(-10L, 10L)]
    [InlineData(long.MinValue, long.MaxValue)]
    [InlineData(0, 0)]
    [InlineData(long.MinValue / 2, long.MaxValue / 2)]
    [InlineData(0, long.MaxValue)]
    public void Serialize__КогдаПереданУказанныйДиапазонПриоритетов__ДолженПравильноДесерилазовать(long min, long max)
    {
        AssertBase(new StubTaskQueue(QueueName.Default,
            maxSize: null,
            priority: ( min, max ),
            maxPayloadSize: null,
            elements: null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    [InlineData(1024 * 1024)]
    [InlineData(1024 * 1024 * 2)]
    public void Serialize__КогдаПереданМаксимальныйРазмерСообщения__ДолженПравильноДесериализовать(int maxPayloadSize)
    {
        AssertBase(new StubTaskQueue(QueueName.Default,
            maxSize: null,
            priority: null,
            maxPayloadSize: maxPayloadSize,
            elements: null));
    }
}