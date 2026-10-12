using UnifierTSL.Events.Core;
using UnifierTSL.Servers;

namespace UnifierTSL.Events.Handlers
{
    public readonly struct AddServer(ServerContext server) : IServerEventContent
    {
        public ServerContext Server { get; init; } = server;
    }
    public readonly struct RemoveServer(ServerContext server) : IServerEventContent
    {
        public ServerContext Server { get; init; } = server;
    }
    public readonly struct ServerListChanged() : IEventContent
    {
    }
    /// <summary>
    /// 某个 ServerContext 换掉了自己的世界数据（PocketWorld 世界池复用会用到）。
    /// ★ 插件如果缓存了"本世界"的数据，**必须**在这里清掉，否则会拿上一个世界的缓存去处理新世界。
    /// </summary>
    public readonly struct WorldSwitched(ServerContext server, IWorldDataProvider provider) : IServerEventContent
    {
        public ServerContext Server { get; init; } = server;
        public IWorldDataProvider Provider { get; init; } = provider;
    }
    public class ServerEventBridge
    {
        public readonly ReadonlyEventNoCancelProvider<AddServer> AddServer = new();
        public readonly ReadonlyEventNoCancelProvider<RemoveServer> RemoveServer = new();
        public readonly ReadonlyEventNoCancelProvider<ServerListChanged> ServerListChanged = new();
        public readonly ReadonlyEventNoCancelProvider<WorldSwitched> WorldSwitched = new();
    }
}
