using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Rambl.Features.Settings;

/// <summary>Provides trimming-safe TOML metadata for settings documents.</summary>
[TomlSourceGenerationOptions(DuplicateKeyHandling = TomlDuplicateKeyHandling.Error)]
[TomlSerializable(typeof(TomlTable))]
internal partial class SettingsTomlContext : TomlSerializerContext { }
