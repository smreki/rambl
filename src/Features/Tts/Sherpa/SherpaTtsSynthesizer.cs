using Rambl.Features.Models;
using SherpaOnnx;

namespace Rambl.Features.Tts.Sherpa;

/// <summary>A text-to-speech model loaded into sherpa-onnx.</summary>
/// <remarks>
/// Kokoro's American and British voices need different lexicons, which are fixed when the model is loaded, so the
/// British copy is loaded the first time a British voice speaks. Supertonic is told the text's language with each
/// request; the other models speak their one language.
/// </remarks>
internal sealed class SherpaTtsSynthesizer : ITtsSynthesizer
{
    private readonly SherpaTtsFiles files;
    private readonly string directory;
    private readonly int threads;
    private readonly OfflineTts main;
    private OfflineTts? british;

    public SherpaTtsSynthesizer(SherpaTtsFiles files, string directory, int threads)
    {
        this.files = files;
        this.directory = directory;
        this.threads = threads;
        main = new OfflineTts(files.CreateConfig(directory, british: false, threads));
    }

    public TtsAudio Synthesize(string text, CatalogVoice voice, string language, float speed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tts = files.Kind == SherpaTtsKind.Kokoro && voice.Language == "en-GB"
            ? british ??= new OfflineTts(files.CreateConfig(directory, british: true, threads))
            : main;

        var config = new OfflineTtsGenerationConfig { Sid = voice.Speaker, Speed = speed };
        if (files.Kind == SherpaTtsKind.Supertonic)
            config.Extra["lang"] = language;

        // Returning 0 from the progress callback stops generating, which is how a request is cancelled.
        var audio = tts.GenerateWithConfig(text, config, (_, _, _, _) => cancellationToken.IsCancellationRequested ? 0 : 1);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (audio.Handle == IntPtr.Zero)
                throw new TtsException("The voice could not read this text.");

            return new TtsAudio(audio.Samples, audio.SampleRate);
        }
        finally
        {
            if (audio.Handle != IntPtr.Zero)
                audio.Dispose();
        }
    }

    public void Dispose()
    {
        main.Dispose();
        british?.Dispose();
    }
}
