using UnifierTSL.Servers;
using UnifierTSL.Surface.Hosting;
using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;
using UnifierTSL.Surface.Prompting;

namespace UnifierTSL.Surface.Hosting.Server;

public sealed class HeadlessServerSurfaceConsole : ServerSurfaceConsole
{
    private sealed class NullSurfaceSession : ISurfaceSession
    {
        public event Action? PresentationAttached {
            add { }
            remove { }
        }

        public event Action? PresentationDetached {
            add { }
            remove { }
        }

        public event Action? ReleaseRequested {
            add { }
            remove { }
        }

        public event Action<int>? ActivitySelectionRequested {
            add { }
            remove { }
        }

        public event Action<InputEventPayload>? InputReceived {
            add { }
            remove { }
        }

        public event Action<LifecyclePayload>? LifecycleReceived {
            add { }
            remove { }
        }
        public event Action<SurfaceCompletionPayload>? CompletionReceived {
            add { }
            remove { }
        }
        public bool IsPresentationAttached => false;
        public void Start() { }
        public void PublishSurfaceHostOperation(SurfaceHostOperation operation) { }
        public void PublishProjectionSnapshot(ProjectionSnapshotPayload snapshot) { }
        public void PublishSurfaceOperation(SurfaceOperation operation) { }
        public InteractionScope OpenInteractionScope(
            string interactionKind,
            bool isTransient = true) {
            return new InteractionScope {
                Id = InteractionScopeId.New(),
                State = InteractionScopeState.Active,
                Kind = interactionKind,
                IsTransient = isTransient,
            };
        }
        public void Dispose() { }
    }

    private readonly ISurfaceSession session = new NullSurfaceSession();

    public HeadlessServerSurfaceConsole(ServerContext server)
        : base(server, () => PromptSurfaceSpec.CreatePlain()) {

        InitializeSurfaceRuntime();
    }

    protected override ISurfaceSession Session => session;

    public override bool CanReadInput => false;

    // 返回 null 而不是抛异常：调用方（Terraria 的 DedServ 输入循环）会在下一轮检查退出条件，
    // 抛异常则会让该线程带未处理异常终止进程（见 commit 记录）
    public override string? ReadLine() {
        return null;
    }

    public override ConsoleKeyInfo ReadKey() {
        return default;
    }

    public override ConsoleKeyInfo ReadKey(bool intercept) {
        return default;
    }

    public override int Read() {
        return -1;
    }

    protected override string? ReadLineCore(PromptSurfaceSpec prompt) {
        return null;
    }
}
