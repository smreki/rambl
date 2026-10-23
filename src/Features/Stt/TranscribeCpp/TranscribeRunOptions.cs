using System.Runtime.InteropServices;

namespace Rambl.Features.Stt.TranscribeCpp;

/// <summary>The run params of one transcription: plain transcription, no timestamps, and the language when one is given.</summary>
/// <remarks>The library copies the language before the call returns, so the UTF-8 copy only lives until this is disposed.</remarks>
internal sealed class TranscribeRunOptions : IDisposable
{
    private nint language;

    /// <summary>The params, passed by reference to the library.</summary>
    public TranscribeNative.RunParams Value;

    /// <summary>Fills the params with the library's defaults and the settings Rambl uses.</summary>
    /// <param name="languageCode">A code from the model's own language list, or null to let the model decide.</param>
    public TranscribeRunOptions(string? languageCode)
    {
        TranscribeNative.RunParamsInit(ref Value);

        // Only the text is used, so no timestamps are asked for (the default asks for the finest the model has).
        Value.Timestamps = 0;
        if (languageCode is not null)
        {
            language = Marshal.StringToCoTaskMemUTF8(languageCode);
            Value.Language = language;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref language, 0);
        if (owned != 0)
            Marshal.FreeCoTaskMem(owned);
    }
}
