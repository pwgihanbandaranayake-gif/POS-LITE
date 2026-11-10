using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Markup;

namespace PosSystem.Helpers;

/// <summary>
/// System language: English (en), Sinhala (si), Tamil (ta).
/// Text is translated by its English wording, so anything without a translation simply stays English.
///   XAML  : Text="{s:Tr 'Save settings'}"
///   C#    : Lang.T("Save settings"), Lang.F("Not enough stock for '{0}'.", name)
/// Messages that contain values (e.g. "Bill MAIN-0001 saved") are matched against the
/// "{0}" templates in the translation tables.
/// The choice is saved per computer in %LocalAppData%\PosSystem\ui.json.
/// </summary>
public static class Lang
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("si", "සිංහල"), ("ta", "தமிழ்")
    };

    public static string Current { get; private set; } = "en";

    /// <summary>Raised when the user asks to apply a new language now (the shell logs out and reopens the screens).</summary>
    public static event Action? ApplyRequested;

    private static Dictionary<string, string> _table = new(StringComparer.Ordinal);
    private static List<(Regex Pattern, string Translation)> _templates = new();

    private static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PosSystem", "ui.json");

    /// <summary>Reads the saved language at start-up.</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
                if (doc.RootElement.TryGetProperty("Language", out var l)) Use(l.GetString() ?? "en");
                return;
            }
        }
        catch { /* fall back to English */ }
        Use("en");
    }

    /// <summary>Switches the language and remembers it for this computer.</summary>
    public static void Set(string code)
    {
        Use(code);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { Language = Current }));
        }
        catch { /* not critical */ }
    }

    public static void RequestApply() => ApplyRequested?.Invoke();

    private static void Use(string code)
    {
        Current = code is "si" or "ta" ? code : "en";
        _table = Current switch
        {
            "si" => Translations.Sinhala,
            "ta" => Translations.Tamil,
            _ => new Dictionary<string, string>()
        };
        // entries with {0}, {1} ... become patterns for messages that contain values
        _templates = _table.Where(kv => kv.Key.Contains("{0}")).OrderByDescending(kv => Regex.Replace(kv.Key, @"\{\d+}", "").Length)
            .Select(kv => (new Regex("^" + Regex.Replace(Regex.Escape(kv.Key), @"\\\{(\d+)}", "(?<p$1>.*?)") + "$",
                                     RegexOptions.Singleline | RegexOptions.CultureInvariant), kv.Value))
            .ToList();
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(Current == "en" ? "en-US" : Current == "si" ? "si-LK" : "ta-LK");
    }

    /// <summary>Translate one text. Unknown text is returned unchanged (English).</summary>
    public static string T(string? english)
    {
        if (string.IsNullOrEmpty(english) || Current == "en") return english ?? "";
        if (_table.TryGetValue(english, out var t)) return t;
        var trimmed = english.Trim();
        if (trimmed != english && _table.TryGetValue(trimmed, out t)) return english.Replace(trimmed, t);
        foreach (var (pattern, translation) in _templates)
        {
            var m = pattern.Match(english);
            if (!m.Success) continue;
            var result = translation;
            for (int i = 0; i < 10; i++)
            {
                var g = m.Groups["p" + i];
                if (g.Success) result = result.Replace("{" + i + "}", T(g.Value));
            }
            return result;
        }
        return english;
    }

    /// <summary>Translate a template then fill in the values: Lang.F("Bill {0} saved", no).</summary>
    public static string F(string englishTemplate, params object?[] args)
    {
        var template = Current == "en" || !_table.TryGetValue(englishTemplate, out var t) ? englishTemplate : t;
        return string.Format(template, args);
    }
}

/// <summary>XAML: Text="{s:Tr 'Save'}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string text) => Text = text;
    [ConstructorArgument("text")] public string Text { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => Lang.T(Text);
}

/// <summary>XAML binding converter for texts that come from the view model: {Binding Title, Converter={StaticResource Tr}}</summary>
public sealed class TrConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? Lang.T(s) : value ?? "";
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
