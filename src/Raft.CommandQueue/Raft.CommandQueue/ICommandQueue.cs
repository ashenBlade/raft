using Raft.Core.Commands;

namespace Raft.CommandQueue;

public interface ICommandQueue
{
    public void Enqueue(ICommand command);
    public T Enqueue<T>(ICommand<T> command);
}