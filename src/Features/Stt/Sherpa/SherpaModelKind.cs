namespace Rambl.Features.Stt.Sherpa;

/// <summary>Which of sherpa-onnx's model configurations a set of model files is loaded with.</summary>
internal enum SherpaModelKind
{
    /// <summary>A NeMo transducer (Parakeet, Nemotron): encoder, decoder, joiner and tokens.</summary>
    Transducer,

    /// <summary>A Whisper model (Whisper, Distil-Whisper): encoder, decoder and tokens, offline only.</summary>
    Whisper,
}
