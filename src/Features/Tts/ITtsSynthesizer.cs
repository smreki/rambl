using Rambl.Features.Models;

namespace Rambl.Features.Tts;

/// <summary>A loaded text-to-speech model that turns text into audio.</summary>
/// <remarks>One text is spoken at a time; the caller makes sure calls do not overlap.</remarks>
public interface ITtsSynthesizer : IDisposable
{
    /// <summary>Speaks a text. It blocks until the audio is ready, so call it off the UI thread.</summary>
    /// <param name="text">The text, ideally one or a few sentences.</param>
    /// <param name="voice">One of the model's voices.</param>
    /// <param name="language">The language code of the text, for example <c>de</c>; a model with one language ignores it.</param>
    /// <param name="speed">How fast to speak; 1 is the voice's normal pace.</param>
    /// <param name="cancellationToken">Stops speaking; the audio made so far is dropped.</param>
    /// <returns>The audio.</returns>
    /// <exception cref="TtsException">The model could not speak the text.</exception>
    /// <exception cref="OperationCanceledException">Speaking was cancelled.</exception>
    TtsAudio Synthesize(string text, CatalogVoice voice, string language, float speed, CancellationToken cancellationToken);
}
