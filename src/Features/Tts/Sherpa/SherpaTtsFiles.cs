using Rambl.Features.Models;
using SherpaOnnx;

namespace Rambl.Features.Tts.Sherpa;

/// <summary>The files of a text-to-speech model that sherpa-onnx loads, found among a downloaded artifact's files.</summary>
/// <param name="Kind">Which configuration the files are loaded with.</param>
/// <param name="Files">The files by their role, relative to the model folder; folders (espeak-ng data) end in a slash.</param>
internal sealed record SherpaTtsFiles(SherpaTtsKind Kind, IReadOnlyDictionary<string, string> Files)
{
    private const string EspeakData = "espeak-ng-data/";

    /// <summary>Finds the files sherpa-onnx needs for a model.</summary>
    /// <param name="model">The catalog model.</param>
    /// <param name="artifact">The downloaded files' catalog entry.</param>
    /// <returns>The files, or null when this is not a kind of model sherpa-onnx is wired up for in Rambl.</returns>
    public static SherpaTtsFiles? Detect(CatalogModel model, ModelArtifact artifact)
    {
        var paths = artifact.Files.Select(file => file.Path).ToList();
        var data = paths.Any(path => path.StartsWith(EspeakData, StringComparison.Ordinal)) ? EspeakData : null;
        Dictionary<string, string?> files = model.Family switch
        {
            "kokoro" => new()
            {
                ["model"] = Find(paths, name => name.EndsWith(".onnx", StringComparison.Ordinal)),
                ["voices"] = Find(paths, name => name == "voices.bin"),
                ["tokens"] = Find(paths, name => name == "tokens.txt"),
                ["lexicon-us"] = Find(paths, name => name == "lexicon-us-en.txt"),
                ["lexicon-gb"] = Find(paths, name => name == "lexicon-gb-en.txt"),
                ["data"] = data,
            },
            "piper" => new()
            {
                ["model"] = Find(paths, name => name.EndsWith(".onnx", StringComparison.Ordinal)),
                ["tokens"] = Find(paths, name => name == "tokens.txt"),
                ["data"] = data,
            },
            "supertonic" => new()
            {
                ["duration"] = Find(paths, name => name.StartsWith("duration_predictor", StringComparison.Ordinal)),
                ["encoder"] = Find(paths, name => name.StartsWith("text_encoder", StringComparison.Ordinal)),
                ["estimator"] = Find(paths, name => name.StartsWith("vector_estimator", StringComparison.Ordinal)),
                ["vocoder"] = Find(paths, name => name.StartsWith("vocoder", StringComparison.Ordinal)),
                ["json"] = Find(paths, name => name == "tts.json"),
                ["indexer"] = Find(paths, name => name == "unicode_indexer.bin"),
                ["style"] = Find(paths, name => name == "voice.bin"),
            },
            _ => [],
        };

        if (files.Count == 0 || files.Values.Any(path => path is null))
            return null;

        var kind = model.Family switch
        {
            "kokoro" => SherpaTtsKind.Kokoro,
            "piper" => SherpaTtsKind.Vits,
            _ => SherpaTtsKind.Supertonic,
        };
        return new(kind, files.ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal));
    }

    /// <summary>Gets every file and folder the model needs, with full paths inside a model folder.</summary>
    /// <param name="directory">The model folder.</param>
    /// <returns>The paths.</returns>
    public IEnumerable<string> All(string directory) => Files.Values.Select(path => Path.Combine(directory, path.TrimEnd('/')));

    /// <summary>Builds the sherpa-onnx configuration that loads the model.</summary>
    /// <param name="directory">The model folder.</param>
    /// <param name="british">Kokoro only: whether to load the British English lexicon instead of the American one.</param>
    /// <param name="threads">How many CPU threads the model may use.</param>
    /// <returns>The configuration.</returns>
    public OfflineTtsConfig CreateConfig(string directory, bool british, int threads)
    {
        string In(string role) => Path.Combine(directory, Files[role].TrimEnd('/'));

        var config = new OfflineTtsConfig();
        config.Model.NumThreads = threads;
        switch (Kind)
        {
            case SherpaTtsKind.Kokoro:
                // espeak-ng spells words the lexicon lacks; its "en" voice is British English.
                config.Model.Kokoro.Model = In("model");
                config.Model.Kokoro.Voices = In("voices");
                config.Model.Kokoro.Tokens = In("tokens");
                config.Model.Kokoro.DataDir = In("data");
                config.Model.Kokoro.Lexicon = In(british ? "lexicon-gb" : "lexicon-us");
                config.Model.Kokoro.Lang = british ? "en" : "en-us";
                break;
            case SherpaTtsKind.Vits:
                config.Model.Vits.Model = In("model");
                config.Model.Vits.Tokens = In("tokens");
                config.Model.Vits.DataDir = In("data");
                break;
            case SherpaTtsKind.Supertonic:
                config.Model.Supertonic.DurationPredictor = In("duration");
                config.Model.Supertonic.TextEncoder = In("encoder");
                config.Model.Supertonic.VectorEstimator = In("estimator");
                config.Model.Supertonic.Vocoder = In("vocoder");
                config.Model.Supertonic.TtsJson = In("json");
                config.Model.Supertonic.UnicodeIndexer = In("indexer");
                config.Model.Supertonic.VoiceStyle = In("style");
                break;
        }

        return config;
    }

    private static string? Find(List<string> paths, Func<string, bool> matches) =>
        paths.FirstOrDefault(path => !path.Contains('/', StringComparison.Ordinal) && matches(path));
}
