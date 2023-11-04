using TaskFlux.Commands.Error;
using TaskFlux.Commands.Visitors;
using TaskQueue.Core;

namespace TaskFlux.Commands.Enqueue;

public class EnqueueCommand : UpdateCommand
{
    public QueueName Queue { get; }
    public long Key { get; }
    public byte[] Payload { get; }

    public EnqueueCommand(long key, byte[] payload, QueueName queue)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Key = key;
        Payload = payload;
        Queue = queue;
    }

    public override CommandType Type => CommandType.Enqueue;

    public override Result Apply(ICommandContext context)
    {
        var manager = context.Node.GetTaskQueueManager();

        if (!manager.TryGetQueue(Queue, out var queue))
        {
            return DefaultErrors.QueueDoesNotExist;
        }

        var result = queue.Enqueue(Key, Payload);
        if (result.IsSuccess)
        {
            return EnqueueResult.Ok;
        }

        // TODO: учитывать ошибки (не только полный может быть)
        return EnqueueResult.Full;
    }

    public override void ApplyNoResult(ICommandContext context)
    {
        var manager = context.Node.GetTaskQueueManager();

        if (!manager.TryGetQueue(Queue, out var queue))
        {
            return;
        }

        queue.Enqueue(Key, Payload);
    }

    public override void Accept(ICommandVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override ValueTask AcceptAsync(IAsyncCommandVisitor visitor, CancellationToken token = default)
    {
        return visitor.VisitAsync(this, token);
    }

    public override T Accept<T>(IReturningCommandVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}