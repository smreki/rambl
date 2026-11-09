using Microsoft.Win32.SafeHandles;

namespace Rambl.Features.Inference.LlamaCpp;

/// <summary>Owns a native model session, including its context and chat templates.</summary>
internal sealed class LlamaModelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal LlamaModelHandle(nint session) : base(ownsHandle: true) => SetHandle(session);

    protected override bool ReleaseHandle()
    {
        LlamaNative.Free(handle);
        return true;
    }
}
