using TaskFlux.Core;

namespace TaskFlux.Abstractions;

public interface IReadOnlyApplication
{
    public INodeInfo NodeInfo { get; }
    public IClusterInfo ClusterInfo { get; }
    public IApplicationInfo ApplicationInfo { get; }
    public IReadOnlyTaskQueueManager TaskQueueManager { get; }
}