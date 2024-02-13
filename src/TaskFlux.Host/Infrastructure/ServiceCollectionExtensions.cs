using Serilog;
using TaskFlux.Application;
using TaskFlux.Consensus;
using TaskFlux.Core.Commands;
using TaskFlux.Host.Configuration;
using TaskFlux.Persistence;
using TaskFlux.Transport.Tcp;
using IApplicationLifetime = TaskFlux.Application.IApplicationLifetime;

namespace TaskFlux.Host.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNodeStateObserverHostedService(this IServiceCollection sc,
                                                                       FileSystemPersistenceFacade persistence,
                                                                       RaftConsensusModule<Command, Response> module)
    {
        return sc.AddHostedService(_ =>
            new NodeStateObserverBackgroundService(module, persistence, TimeSpan.FromSeconds(5),
                Log.ForContext("SourceContext", "StateObserver")));
    }

    public static IServiceCollection AddTcpRequestModule(this IServiceCollection sc, IApplicationLifetime lifetime)
    {
        return sc.AddHostedService(sp => new TcpAdapterBackgroundService(sp.GetRequiredService<IRequestAcceptor>(),
            sp.GetRequiredService<ApplicationOptions>().TcpModule,
            sp.GetRequiredService<IApplicationInfo>(),
            lifetime,
            Log.ForContext<TcpAdapterBackgroundService>()));
    }
}