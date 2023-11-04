using TaskFlux.Commands.Error;
using TaskFlux.Commands.Ok;
using TaskFlux.Commands.Visitors;
using TaskQueue.Core;

namespace TaskFlux.Commands.CreateQueue;

public class CreateQueueCommand : UpdateCommand
{
    public override CommandType Type => CommandType.CreateQueue;
    public QueueName Name { get; }
    public int? Size { get; }

    private ITaskQueue CreateTaskQueue()
    {
        var builder = new TaskQueueBuilder(Name);
        if (Size is { } size)
        {
            builder.WithMaxSize(size);
        }

        return builder.Build();
    }

    public CreateQueueCommand(QueueName name, int? size)
    {
        Name = name;
        Size = size;
    }

    public override Result Apply(ICommandContext context)
    {
        var manager = context.Node.GetTaskQueueManager();
        if (manager.HasQueue(Name))
        {
            return DefaultErrors.QueueAlreadyExists;
        }

        var queue = CreateTaskQueue();
        if (manager.TryAddQueue(Name, queue))
        {
            return OkResult.Instance;
        }

        return new ErrorResult(ErrorType.Unknown, "Не удалось создать указанную очередь. Неизвестная ошибка");
    }

    public override void ApplyNoResult(ICommandContext context)
    {
        var manager = context.Node.GetTaskQueueManager();
        if (!manager.HasQueue(Name))
        {
            return;
        }

        var queue = CreateTaskQueue();
        manager.TryAddQueue(Name, queue);
    }

    public override void Accept(ICommandVisitor visitor)
    {
        visitor.Visit(this);
    }

    public override T Accept<T>(IReturningCommandVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }

    public override ValueTask AcceptAsync(IAsyncCommandVisitor visitor, CancellationToken token = default)
    {
        return visitor.VisitAsync(this, token);
    }
}