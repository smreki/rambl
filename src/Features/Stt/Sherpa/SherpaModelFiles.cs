using Rambl.Features.Models;

namespace Rambl.Features.Stt.Sherpa;

/// <summary>The files of a model that sherpa-onnx loads, found among a downloaded artifact's files.</summary>
/// <param name="Kind">Which configuration the files are loaded with.</param>
/// <param name="Encoder">The encoder, relative to the model folder.</param>
/// <param name="Decoder">The decoder.</param>
/// <param name="Joiner">The joiner of a transducer; null for a Whisper model, which has none.</param>
/// <param name="Tokens">The token list.</param>
internal sealed record SherpaModelFiles(SherpaModelKind Kind, string Encoder, string Decoder, string? Joiner, string Tokens)
{
    // The transducer families run from encoder, decoder, joiner and tokens.txt; Whisper models from an encoder, a decoder
    // and a token list whose names carry a prefix (distil-large-v3.5-encoder.int8.onnx, turbo-tokens.txt).
    private static readonly string[] TransducerFamilies = ["parakeet", "nemotron"];
    private const string WhisperFamily = "whisper";

    /// <summary>Gets every file the model needs.</summary>
    public IEnumerable<string> All => Joiner is null ? [Encoder, Decoder, Tokens] : [Encoder, Decoder, Joiner, Tokens];

    /// <summary>Finds the files sherpa-onnx needs for a model.</summary>
    /// <param name="model">The catalog model.</param>
    /// <param name="artifact">The downloaded files' catalog entry.</param>
    /// <returns>The files, or null when this is not a kind of model sherpa-onnx is wired up for in Rambl.</returns>
    public static SherpaModelFiles? Detect(CatalogModel model, ModelArtifact artifact)
    {
        var paths = artifact.Files.Select(file => file.Path).ToList();
        if (TransducerFamilies.Contains(model.Family, StringComparer.Ordinal))
        {
            var encoder = Find(paths, name => name.StartsWith("encoder", StringComparison.Ordinal) && name.EndsWith(".onnx", StringComparison.Ordinal));
            var decoder = Find(paths, name => name.StartsWith("decoder", StringComparison.Ordinal) && name.EndsWith(".onnx", StringComparison.Ordinal));
            var joiner = Find(paths, name => name.StartsWith("joiner", StringComparison.Ordinal) && name.EndsWith(".onnx", StringComparison.Ordinal));
            var tokens = Find(paths, name => name == "tokens.txt");
            return encoder is null || decoder is null || joiner is null || tokens is null
                ? null
                : new(SherpaModelKind.Transducer, encoder, decoder, joiner, tokens);
        }

        if (model.Family == WhisperFamily)
        {
            var encoder = Find(paths, name => name.Contains("encoder", StringComparison.Ordinal) && name.EndsWith(".onnx", StringComparison.Ordinal));
            var decoder = Find(paths, name => name.Contains("decoder", StringComparison.Ordinal) && name.EndsWith(".onnx", StringComparison.Ordinal));
            var tokens = Find(paths, name => name.EndsWith("tokens.txt", StringComparison.Ordinal));
            return encoder is null || decoder is null || tokens is null ? null : new(SherpaModelKind.Whisper, encoder, decoder, null, tokens);
        }

        return null;
    }

    /// <summary>Gets the files' full paths inside a model folder.</summary>
    /// <param name="directory">The model folder.</param>
    /// <returns>The files with their paths resolved.</returns>
    public SherpaModelFiles In(string directory) =>
        new(
            Kind,
            Path.Combine(directory, Encoder),
            Path.Combine(directory, Decoder),
            Joiner is null ? null : Path.Combine(directory, Joiner),
            Path.Combine(directory, Tokens)
        );

    private static string? Find(List<string> paths, Func<string, bool> matches) => paths.FirstOrDefault(path => matches(Path.GetFileName(path)));
}
