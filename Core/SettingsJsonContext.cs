using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwentyMate.Core;

/// <summary>
/// Source-generated JSON metadata for settings.json and the embedded language files — under
/// NativeAOT, <see cref="System.Text.Json.JsonSerializer"/>'s default reflection-based
/// resolution isn't available.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AppStats))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class SettingsJsonContext : JsonSerializerContext;
