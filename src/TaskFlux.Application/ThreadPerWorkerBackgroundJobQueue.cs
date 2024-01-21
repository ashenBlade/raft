using System.Collections.Concurrent;
using Serilog;
using TaskFlux.Consensus;

namespace TaskFlux.Application;

public class ThreadPerWorkerBackgroundJobQueue : IBackgroundJobQueue, IDisposable
{
    private readonly ILogger _logger;
    private readonly IApplicationLifetime _lifetime;
    private readonly CancellationTokenSource _cts = new();

    private readonly (Thread Thread, BlockingCollection<(IBackgroundJob Job, CancellationToken Token)> Queue)?[]
        _workers;

    public ThreadPerWorkerBackgroundJobQueue(int clusterSize,
                                             int currentNodeId,
                                             ILogger logger,
                                             IApplicationLifetime lifetime)
    {
        _logger = logger;
        _lifetime = lifetime;
        _workers = CreateWorkers(clusterSize, currentNodeId);
    }

    private (Thread, BlockingCollection<(IBackgroundJob Job, CancellationToken Token)>)?[] CreateWorkers(
        int clusterSize,
        int currentNodeId)
    {
        var array = new (Thread, BlockingCollection<(IBackgroundJob Job, CancellationToken Token)>)?[clusterSize];
        for (var i = 0; i < array.Length; i++)
        {
            if (i == currentNodeId)
            {
                continue;
            }

            var thread = new Thread(ThreadWorker);
            var queue = new BlockingCollection<(IBackgroundJob Job, CancellationToken Token)>();
            array[i] = ( thread, queue );
        }

        return array;
    }

    public void Start()
    {
        for (var i = 0; i < _workers.Length; i++)
        {
            if (_workers[i] is var (thread, _))
            {
                thread.Start(i);
            }
        }
    }

    public void Accept(IBackgroundJob job, CancellationToken token)
    {
        var index = job.NodeId.Id;
        // На 0 можно не проверять - ID узла отрицательным быть не может
        if (_workers.Length <= index)
        {
            throw new ArgumentOutOfRangeException(nameof(job.NodeId), job.NodeId,
                "Указанный ID узла выходит за рамки кластера");
        }

        if (_workers[index] is not var (_, collection))
        {
            throw new ArgumentOutOfRangeException(nameof(job.NodeId), job.NodeId,
                "Обработчика текущего узла не предусмотрено");
        }

        try
        {
            collection.Add(( job, token ), _cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ThreadWorker(object? o)
    {
        var nodeId = ( int ) o!;
        var collection = _workers[nodeId]!.Value.Queue;

        var backgroundJobQueueToken = _cts.Token;
        try
        {
            _logger.Debug("Очередь фоновых задач для узла {NodeId} запущена", nodeId);
            foreach (var (job, jobToken) in collection.GetConsumingEnumerable(backgroundJobQueueToken))
            {
                _logger.Debug("Получена задача {Job} для узла {NodeId}", job, nodeId);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(backgroundJobQueueToken, jobToken);
                try
                {
                    job.Run(cts.Token);
                }
                catch (OperationCanceledException)
                    when (jobToken.IsCancellationRequested)
                {
                }

                _logger.Debug("Задача {Job} для узла {NodeId} завершилась", job, nodeId);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Во время работы фонового обработчика возникло необработанное исключение");
            _lifetime.StopAbnormal();
        }

        _logger.Debug("Очередь фоновых задач для узла {NodeId} завершает работу", nodeId);
    }

    public void Dispose()
    {
        _cts.Dispose();

        Array.ForEach(_workers, static w =>
        {
            if (w is var (thread, collection))
            {
                collection.Dispose();
                thread.Join();
            }
        });
    }

    public void Stop()
    {
        _cts.Cancel();
        Array.ForEach(_workers, static w =>
        {
            if (w is var (_, queue))
            {
                queue.CompleteAdding();
            }
        });
    }
}