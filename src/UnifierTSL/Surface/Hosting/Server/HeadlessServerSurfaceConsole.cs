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

    // ★ 2026-10-12 修复：原来是【抛异常】。
    //   但 Terraria 的 DedServ 会另起一条线程循环调用 MainSystemContext.ReadLineInput()
    //   → root.Console.ReadLine()；抛异常会让该线程带着【未处理异常】退出，实测直接【终止整个进程】✗。
    //   改成"返回 null / 默认值"：调用方的循环会自然结束、线程干净退出，不会崩。
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
