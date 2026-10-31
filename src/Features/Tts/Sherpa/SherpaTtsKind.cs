namespace Rambl.Features.Tts.Sherpa;

/// <summary>Which of sherpa-onnx's text-to-speech configurations a set of model files is loaded with.</summary>
internal enum SherpaTtsKind
{
    /// <summary>Kokoro: one model, a voice table, tokens, English lexicons and espeak-ng data.</summary>
    Kokoro,

    /// <summary>A Piper (VITS) voice: one model, tokens and espeak-ng data.</summary>
    Vits,

    /// <summary>Supertonic: four models, its configuration, a character index and a voice table.</summary>
    Supertonic,
}
