using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Avalonia;

namespace TwentyMate.Core;

/// <summary>
/// Resolves the app's UI language and pushes its strings into <see cref="Application.Resources"/>
/// as plain <see cref="string"/> values, so XAML can bind to them with <c>DynamicResource</c> the
/// same way <see cref="ThemeManager"/> pushes brushes — no window needs to be recreated to pick up
/// a language switch.
/// </summary>
public static class LocalizationManager
{
    private const string FallbackCode = "en";
    private const uint MuiLanguageName = 0x8;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserPreferredUILanguages(
        uint dwFlags, out uint pulNumLanguages, char[]? pwszLanguagesBuffer, ref uint pcchLanguagesBuffer);

    /// <summary>The BCP-47 tag each shipped language ships its JSON strings under.</summary>
    private static readonly Dictionary<AppLanguage, string> LanguageCodes = new()
    {
        [AppLanguage.English] = "en",
        [AppLanguage.Russian] = "ru",
        [AppLanguage.Spanish] = "es",
        [AppLanguage.German] = "de",
        [AppLanguage.French] = "fr",
        [AppLanguage.PortugueseBr] = "pt-BR",
    };

    private static Dictionary<string, string> _strings = new();

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(FallbackCode);

    public static event EventHandler? Changed;

    public static void Initialize(AppLanguage preference) => Apply(preference);

    public static void Apply(AppLanguage preference)
    {
        var code = preference == AppLanguage.System ? ResolveSystemCode() : CodeFor(preference);

        Culture = SafeGetCulture(code);
        _strings = LoadStrings(code);

        // Only the UI culture is touched: SettingsWindow.NormalizeTime relies on the invariant
        // HH:mm format via the thread's CurrentCulture, and that must never shift with the
        // display language.
        Thread.CurrentThread.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;

        PushToResources();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Looks up a translated string, formatting it with <paramref name="args"/> if given.</summary>
    public static string T(string key, params object[] args)
    {
        if (!_strings.TryGetValue(key, out var format)) return key;
        return args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
    }

    private static string CodeFor(AppLanguage language) =>
        LanguageCodes.GetValueOrDefault(language, FallbackCode);

    /// <summary>
    /// System preference: first the OS display language, then Windows' ordered list of preferred
    /// UI languages, then English — each checked against the shipped set in turn.
    /// </summary>
    private static string ResolveSystemCode()
    {
        var candidates = new List<string> { CultureInfo.CurrentUICulture.Name };
        candidates.AddRange(GetWindowsPreferredUILanguages());

        foreach (var candidate in candidates)
        {
            var match = MatchShipped(candidate);
            if (match is not null) return match;
        }

        return FallbackCode;
    }

    /// <summary>Matches a BCP-47 tag against the shipped set: exact tag first, then neutral (two-letter) culture.</summary>
    private static string? MatchShipped(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var exact = LanguageCodes.Values.FirstOrDefault(code => string.Equals(code, tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        string neutral;
        try
        {
            neutral = CultureInfo.GetCultureInfo(tag).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            var dash = tag.IndexOf('-');
            neutral = dash > 0 ? tag[..dash] : tag;
        }

        // Covers both plain two-letter codes ("es") and the one shipped variant, pt-BR,
        // matching on its "pt-" prefix so a bare "pt" preference still resolves.
        return LanguageCodes.Values.FirstOrDefault(code =>
            string.Equals(code, neutral, StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Windows' actual ordered preferred-UI-languages list (Settings &gt; Time &amp; Language &gt;
    /// Language), not just the single display language <see cref="CultureInfo.CurrentUICulture"/>
    /// reports. Two-call buffer-size pattern; the result is a double-null-terminated multi-string.
    /// </summary>
    private static List<string> GetWindowsPreferredUILanguages()
    {
        try
        {
            uint bufferSize = 0;
            if (!GetUserPreferredUILanguages(MuiLanguageName, out _, null, ref bufferSize) || bufferSize == 0)
                return [];

            var buffer = new char[bufferSize];
            if (!GetUserPreferredUILanguages(MuiLanguageName, out _, buffer, ref bufferSize))
                return [];

            var raw = new string(buffer, 0, (int)bufferSize);
            return raw.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch
        {
            // A failure here must never block startup — just behave as if there were no candidates.
            return [];
        }
    }

    private static CultureInfo SafeGetCulture(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(FallbackCode);
        }
    }

    private static Dictionary<string, string> LoadStrings(string code)
    {
        return LoadEmbedded(code)
            ?? (code != FallbackCode ? LoadEmbedded(FallbackCode) : null)
            ?? new Dictionary<string, string>();
    }

    private static Dictionary<string, string>? LoadEmbedded(string code)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith($".{code}.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null) return null;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return null;

            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.DictionaryStringString);
        }
        catch
        {
            return null;
        }
    }

    private static void PushToResources()
    {
        if (Application.Current is not { } app) return;

        foreach (var (key, value) in _strings) app.Resources[key] = value;
    }
}
