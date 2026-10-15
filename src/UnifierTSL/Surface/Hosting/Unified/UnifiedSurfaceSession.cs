using UnifierTSL.Contracts.Projection;
using UnifierTSL.Contracts.Protocol.Payloads;
using UnifierTSL.Contracts.Sessions;

namespace UnifierTSL.Surface.Hosting.Unified;

/// <summary>
/// 单窗口模式下的世界会话：不接管道、不拉子进程、不建新窗口。
/// 交互（输入/输出）由 UnifiedServerSurfaceConsole 直接接到启动器终端，本会话只做占位。
/// </summary>
internal sealed class UnifiedSurfaceSession : ISurfaceSession
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

    public void Start() {
    }

    public void PublishSurfaceHostOperation(SurfaceHostOperation operation) {
    }

    public void PublishProjectionSnapshot(ProjectionSnapshotPayload snapshot) {
    }

    public void PublishSurfaceOperation(SurfaceOperation operation) {
    }

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

    public void Dispose() {
    }
}