﻿using TaskFlux.Commands.Count;
using TaskFlux.Commands.CreateQueue;
using TaskFlux.Commands.Dequeue;
using TaskFlux.Commands.Enqueue;
using TaskFlux.Models;
using TaskFlux.PriorityQueue;
using Xunit;

namespace TaskFlux.Commands.Serialization.Tests;

// ReSharper disable StringLiteralTypo
[Trait("Category", "Serialization")]
public class CommandSerializerTests
{
    private static readonly CommandSerializer Serializer = new();

    private static void AssertBase(Command command)
    {
        var serialized = Serializer.Serialize(command);
        var actual = Serializer.Deserialize(serialized);
        Assert.Equal(command, actual, CommandEqualityComparer.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("queue")]
    [InlineData("adfdfff")]
    [InlineData("what??")]
    [InlineData("queue.number.uno1")]
    [InlineData("queue.number.uno2")]
    [InlineData("hello-world")]
    [InlineData("hello-world.com")]
    [InlineData("queue:2:help")]
    [InlineData("default.1.oops")]
    [InlineData("main.DEV_OPS")]
    public void CountCommand__Serialization(string queueName)
    {
        AssertBase(new CountCommand(QueueNameParser.Parse(queueName)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("sample-queue")]
    [InlineData("xxx")]
    [InlineData("nice.nice.uwu")]
    [InlineData("hello,world")]
    public void DequeueCommand__Serialization(string queueName)
    {
        AssertBase(new DequeueCommand(QueueNameParser.Parse(queueName)));
    }

    [Theory]
    [InlineData(1, 0, "")]
    [InlineData(1, 1, "default")]
    [InlineData(1, 10, "what??")]
    [InlineData(0, 1, "queueNumberOne")]
    [InlineData(0, 0, "asdf")]
    [InlineData(0, 2, "nice.uwu")]
    [InlineData(0, 123, "supra222")]
    [InlineData(123, 100, "queue")]
    [InlineData(int.MaxValue, 0, "default.queue")]
    [InlineData(int.MaxValue, byte.MaxValue, "what.is.it")]
    [InlineData(-1, 1, "abc-sss.u343e")]
    [InlineData(long.MaxValue, 0, "queue")]
    [InlineData(long.MinValue, 0, "")]
    [InlineData(long.MinValue, 1, "nope")]
    [InlineData(long.MaxValue, 1, "uiii")]
    [InlineData(long.MaxValue, 100, "q123oeire")]
    [InlineData(( long ) int.MaxValue + 1, 100, "!dfd...dsf")]
    [InlineData(long.MaxValue - 1, 2, "asdfv")]
    [InlineData(-1, byte.MaxValue, "dfdq135f")]
    public void EnqueueCommand__Serialization(long key, int payloadLength, string queueName)
    {
        var buffer = new byte[payloadLength];
        Random.Shared.NextBytes(buffer);
        AssertBase(new EnqueueCommand(key, buffer, QueueNameParser.Parse(queueName)));
    }

    public record struct CreateQueueCommandArgument(string QueueName,
                                                    PriorityQueueCode Code,
                                                    int? MaxQueueSize,
                                                    int? MaxPayloadSize,
                                                    (long, long)? PriorityRange);

    public static IEnumerable<object[]> CreateQueueCommandData => new[]
    {
        new object[] {new CreateQueueCommandArgument("", PriorityQueueCode.Heap4Arity, null, null, null)},
        new object[]
        {
            new CreateQueueCommandArgument("hello", PriorityQueueCode.QueueArray, null, 1024,
                ( long.MinValue, long.MaxValue ))
        },
        new object[]
        {
            new CreateQueueCommandArgument("task:queue:1", PriorityQueueCode.Heap4Arity, int.MaxValue, null,
                ( -1L, 10L ))
        },
        new object[]
        {
            new CreateQueueCommandArgument("queue-name", PriorityQueueCode.Heap4Arity, null, 1024 * 1024 * 2,
                null)
        },
        new object[]
        {
            new CreateQueueCommandArgument("orders:2023-11-04", PriorityQueueCode.QueueArray, null, 1024 * 1024,
                ( -10L, 10L ))
        }
    };

    [Theory]
    [MemberData(nameof(CreateQueueCommandData))]
    public void CreateQueueCommand__Serialization(CreateQueueCommandArgument argument)
    {
        var (queueName, code, maxQueueSize, maxPayloadSize, priorityRange) = argument;
        AssertBase(new CreateQueueCommand(name: QueueNameParser.Parse(queueName),
            code: code,
            maxQueueSize: maxQueueSize,
            maxPayloadSize: maxPayloadSize,
            priorityRange: priorityRange));
    }
}