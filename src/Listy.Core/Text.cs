using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace Listy.Core;

public sealed class Text : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> Catalogs =
        new Dictionary<string, Dictionary<string, string>> { ["pl"] = Load("pl"), ["en"] = Load("en") };
    public static Text Instance { get; } = new();
    public static string Language { get; private set; } = "pl";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string this[string key] => Catalogs[Language].TryGetValue(key, out var value) ? value : Catalogs["pl"].GetValueOrDefault(key, key);
    public static string T(string key, params object?[] args) => args.Length == 0
        ? Instance[key] : string.Format(CultureInfo.CurrentCulture, Instance[key], args);
    public static IReadOnlyDictionary<string, string> Catalog(string language) => Catalogs[language];

    public static void SetLanguage(string language)
    {
        Language = language == "en" ? "en" : "pl";
        var culture = CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : "pl-PL");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
    }

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(Text).Assembly.GetManifestResourceStream($"Listy.Core.Translations.{language}.json")
            ?? throw new InvalidOperationException("Missing translation catalog: " + language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}

public static class LanguagePreference
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Listy", "language.txt");
    public static string Load(string? path = null)
    {
        try { return File.ReadAllText(path ?? DefaultPath).Trim() == "en" ? "en" : "pl"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "pl"; }
    }
    public static void Save(string language, string? path = null)
    {
        var target = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, language == "en" ? "en" : "pl");
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
