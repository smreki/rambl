using Microsoft.Extensions.Logging;
using Rambl.Common.Input;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Serialization;

namespace Rambl.Features.Settings;

/// <summary>Reads and saves user settings in the application's root config.toml file.</summary>
/// <param name="configPath">The absolute configuration file path.</param>
/// <param name="logger">The logger for temporary file cleanup failures.</param>
/// <remarks>Retains unknown TOML keys when saving; formatting is normalized and comments are removed.</remarks>
public sealed class SettingsService(string configPath, ILogger<SettingsService> logger)
    : IDisposable
{
    private readonly string configPath = !string.IsNullOrWhiteSpace(configPath)
        ? Path.GetFullPath(configPath)
        : throw new ArgumentException("A configuration path is required.", nameof(configPath));

    private readonly ILogger<SettingsService> logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Loads settings, creating a file with the default settings when it is absent.</summary>
    /// <returns>The saved settings, using defaults for values that are absent or unrecognized.</returns>
    /// <exception cref="IOException">The file cannot be read or created.</exception>
    /// <exception cref="InvalidDataException">The configuration has invalid setting types.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the configuration file is denied.</exception>
    /// <exception cref="TomlException">The configuration contains invalid TOML.</exception>
    public async Task<AppSettings> LoadAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentAsync().ConfigureAwait(false);
            if (document is not null)
                return ReadSettings(document);

            var settings = new AppSettings();
            document = [];

            WriteSettings(document, settings);
            await WriteDocumentAsync(document).ConfigureAwait(false);
            return settings;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>Applies a change to the saved settings, preserving unrelated keys and replacing the file after writing succeeds.</summary>
    /// <param name="update">Returns the new settings from the settings currently in the file.</param>
    /// <returns>The settings that were saved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="update"/> returned null or an undefined theme, dictation position, or activation mode, a feedback volume outside 0–100, an input channel below 1, or negative retention days.</exception>
    /// <exception cref="IOException">The configuration cannot be read or saved.</exception>
    /// <exception cref="InvalidDataException">The configuration has invalid setting types.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the configuration file is denied.</exception>
    /// <exception cref="TomlException">The existing configuration contains invalid TOML and cannot be updated.</exception>
    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Re-read to retain unrelated changes made by the user since startup.
            var document = await ReadDocumentAsync().ConfigureAwait(false) ?? [];
            var settings = update(ReadSettings(document));
            if (
                settings is null
                || !Enum.IsDefined(settings.Theme)
                || !Enum.IsDefined(settings.ActivationMode)
                || !Enum.IsDefined(settings.DictationPosition)
                || settings.FeedbackVolume is < 0 or > 100
                || settings.InputChannel is < 1
                || settings.HistoryRetentionDays < 0
            )
                throw new InvalidOperationException("The settings update returned undefined settings.");

            WriteSettings(document, settings);
            await WriteDocumentAsync(document).ConfigureAwait(false);
            return settings;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => gate.Dispose();

    private async Task<TomlTable?> ReadDocumentAsync()
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(configPath).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        SyntaxParser.ParseStrict(text, sourceName: configPath);

        return TomlSerializer.Deserialize(text, SettingsTomlContext.Default.TomlTable)
            ?? [];
    }

    private async Task WriteDocumentAsync(TomlTable document)
    {
        var text = TomlSerializer.Serialize(document, SettingsTomlContext.Default.TomlTable);

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var temporaryPath = $"{configPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, text).ConfigureAwait(false);
            File.Move(temporaryPath, configPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    ex,
                    "Unable to remove temporary settings file {Path}",
                    temporaryPath
                );
            }
        }
    }

    private static AppSettings ReadSettings(TomlTable document)
    {
        var defaults = new AppSettings();
        var appearance = FindTable(document, "appearance");
        var controls = FindTable(document, "controls");
        var audio = FindTable(document, "audio");
        var models = FindTable(document, "models");
        var languageModels = FindTable(document, "language_models");
        var history = FindTable(document, "history");

        return new()
        {
            HistoryEnabled = ReadBool(history, "history", "enabled") ?? defaults.HistoryEnabled,
            HistoryRetentionDays = ReadInt(history, "history", "retention_days") is >= 0 and var days
                ? days
                : defaults.HistoryRetentionDays,
            RecordingsDirectory = NullIfEmpty(ReadString(history, "history", "directory")),
            ActiveModelId = NullIfEmpty(ReadString(models, "models", "active")),
            ActiveLanguageModelId = NullIfEmpty(ReadString(languageModels, "language_models", "active")),
            ModelRuntimes = ReadModelChoices(models, "runtimes"),
            ModelLanguages = ReadModelChoices(models, "languages"),
            OutputDeviceId = NullIfEmpty(ReadString(audio, "audio", "output_device")),
            FeedbackVolume = ReadInt(audio, "audio", "feedback_volume") is { } volume
                ? Math.Clamp(volume, 0, 100)
                : defaults.FeedbackVolume,
            InputDeviceId = NullIfEmpty(ReadString(audio, "audio", "input_device")),
            InputChannel = ReadInt(audio, "audio", "input_channel") is >= 1 and var channel ? channel : null,
            MuteSystemAudio = ReadBool(audio, "audio", "mute_system_audio") ?? defaults.MuteSystemAudio,
            PlayFeedbackSounds = ReadBool(audio, "audio", "play_feedback_sounds") ?? defaults.PlayFeedbackSounds,
            SkipSilence = ReadBool(audio, "audio", "skip_silence") ?? defaults.SkipSilence,
            Theme = ReadString(appearance, "appearance", "theme")?.ToLowerInvariant() switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                null => defaults.Theme,
                _ => AppTheme.Unspecified,
            },
            DictationPosition = ReadString(appearance, "appearance", "dictation_position")?.ToLowerInvariant() switch
            {
                "top-left" => DictationPosition.TopLeft,
                "top-center" => DictationPosition.TopCenter,
                "top-right" => DictationPosition.TopRight,
                "bottom-left" => DictationPosition.BottomLeft,
                "bottom-center" => DictationPosition.BottomCenter,
                "bottom-right" => DictationPosition.BottomRight,
                _ => defaults.DictationPosition,
            },
            ActivationMode = ReadString(controls, "controls", "activation_mode")?.ToLowerInvariant() switch
            {
                "hold" => ActivationMode.HoldToTalk,
                "toggle" => ActivationMode.PressToToggle,
                _ => defaults.ActivationMode,
            },
            DictationShortcut = Shortcut.TryParse(
                ReadString(controls, "controls", "dictation_shortcut"),
                out var dictation
            )
                ? dictation
                : defaults.DictationShortcut,
            CancelShortcut = Shortcut.TryParse(
                ReadString(controls, "controls", "cancel_shortcut"),
                out var cancel
            )
                ? cancel
                : defaults.CancelShortcut,
            AutoPaste = ReadBool(controls, "controls", "auto_paste") ?? defaults.AutoPaste,
        };
    }

    private static void WriteSettings(TomlTable document, AppSettings settings)
    {
        var appearance = GetOrAddTable(document, "appearance");
        appearance["theme"] = settings.Theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            _ => "system",
        };
        appearance["dictation_position"] = settings.DictationPosition switch
        {
            DictationPosition.TopLeft => "top-left",
            DictationPosition.TopRight => "top-right",
            DictationPosition.BottomLeft => "bottom-left",
            DictationPosition.BottomCenter => "bottom-center",
            DictationPosition.BottomRight => "bottom-right",
            _ => "top-center",
        };

        var controls = GetOrAddTable(document, "controls");
        controls["activation_mode"] = settings.ActivationMode switch
        {
            ActivationMode.PressToToggle => "toggle",
            _ => "hold",
        };
        controls["dictation_shortcut"] = settings.DictationShortcut.ToString();
        controls["cancel_shortcut"] = settings.CancelShortcut.ToString();
        controls["auto_paste"] = settings.AutoPaste;

        var audio = GetOrAddTable(document, "audio");
        SetOrRemove(audio, "output_device", settings.OutputDeviceId);
        audio["feedback_volume"] = (long)settings.FeedbackVolume;
        SetOrRemove(audio, "input_device", settings.InputDeviceId);
        SetOrRemove(audio, "input_channel", settings.InputChannel is { } channel ? (long)channel : null);
        audio["mute_system_audio"] = settings.MuteSystemAudio;
        audio["play_feedback_sounds"] = settings.PlayFeedbackSounds;
        audio["skip_silence"] = settings.SkipSilence;

        var history = GetOrAddTable(document, "history");
        history["enabled"] = settings.HistoryEnabled;
        history["retention_days"] = (long)settings.HistoryRetentionDays;
        SetOrRemove(history, "directory", settings.RecordingsDirectory);

        // The tables only exist while there is something in them, so a config that never used them stays free of them.
        var languageModels = FindTable(document, "language_models");
        if (settings.ActiveLanguageModelId is not null)
            languageModels ??= GetOrAddTable(document, "language_models");

        if (languageModels is not null)
        {
            SetOrRemove(languageModels, "active", settings.ActiveLanguageModelId);
            if (languageModels.Count == 0)
                _ = document.Remove("language_models");
        }

        var models = FindTable(document, "models");
        if (settings.ActiveModelId is not null || settings.ModelRuntimes.Count > 0 || settings.ModelLanguages.Count > 0)
            models ??= GetOrAddTable(document, "models");

        if (models is null)
            return;

        SetOrRemove(models, "active", settings.ActiveModelId);
        WriteModelChoices(models, "runtimes", settings.ModelRuntimes);
        WriteModelChoices(models, "languages", settings.ModelLanguages);

        if (models.Count == 0)
            _ = document.Remove("models");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // [models.runtimes] and [models.languages] map a model id to the runtime or language picked for it; entries that are not
    // text are errors, empty ones are ignored.
    private static Dictionary<string, string> ReadModelChoices(TomlTable? models, string tableName)
    {
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        if (FindChildTable(models, tableName) is not { } table)
            return choices;

        foreach (var (modelId, value) in table)
        {
            var choice = value as string ?? throw new InvalidDataException($"models.{tableName}.{modelId} must be a string.");
            if (!string.IsNullOrWhiteSpace(choice))
                choices[modelId] = choice;
        }

        return choices;
    }

    // The table only exists while it has an entry.
    private static void WriteModelChoices(TomlTable models, string tableName, IReadOnlyDictionary<string, string> choices)
    {
        if (choices.Count == 0)
        {
            _ = models.Remove(tableName);
            return;
        }

        var table = new TomlTable();
        foreach (var (modelId, choice) in choices.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            table[modelId] = choice;

        models[tableName] = table;
    }

    private static TomlTable? FindChildTable(TomlTable? parent, string name) =>
        parent is not null && parent.TryGetValue(name, out var value)
            ? value as TomlTable ?? throw new InvalidDataException($"models.{name} must be a TOML table.")
            : null;

    private static void SetOrRemove(TomlTable table, string key, object? value)
    {
        if (value is null)
            _ = table.Remove(key);
        else
            table[key] = value;
    }

    private static string? ReadString(TomlTable? table, string section, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        return value as string
            ?? throw new InvalidDataException($"{section}.{key} must be a string.");
    }

    private static int? ReadInt(TomlTable? table, string section, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        return value is long number
            ? (int)Math.Clamp(number, int.MinValue, int.MaxValue)
            : throw new InvalidDataException($"{section}.{key} must be an integer.");
    }

    private static bool? ReadBool(TomlTable? table, string section, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value))
            return null;

        return value as bool?
            ?? throw new InvalidDataException($"{section}.{key} must be true or false.");
    }

    private static TomlTable? FindTable(TomlTable document, string name) =>
        !document.TryGetValue(name, out var value)
            ? null
            : value as TomlTable ?? throw new InvalidDataException($"{name} must be a TOML table.");

    private static TomlTable GetOrAddTable(TomlTable document, string name)
    {
        if (FindTable(document, name) is { } table)
            return table;

        table = [];
        document[name] = table;
        return table;
    }
}
