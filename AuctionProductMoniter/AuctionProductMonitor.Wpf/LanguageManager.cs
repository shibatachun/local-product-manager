using System.Globalization;
using System.IO;
using System.Windows;

namespace AuctionProductMonitor.Wpf;

public static class LanguageManager
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuctionProductMonitor", "language.txt");

    public static string CurrentLanguage { get; private set; } = Chinese;

    public static void Initialize()
    {
        var savedLanguage = ReadSavedLanguage();
        ChangeLanguage(savedLanguage, save: false);
    }
    public static void ChangeLanguage(string language, bool save = true)
    {
        language = language == English ? English : Chinese;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var oldDictionary = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);

        if(oldDictionary is not null)
        {
            dictionaries.Remove(oldDictionary);
        }
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{language}.xaml", UriKind.Relative)
        });
        CurrentLanguage = language;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (save)
            SaveLanguage(language);

    }

    public static string Get(string key, params object[] arguments)
    {
        var text = Application.Current.TryFindResource(key)?.ToString() ?? key;
        return arguments.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, arguments);
    }
    private static string ReadSavedLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return Chinese;
            var language = File.ReadAllText(SettingsPath).Trim();
            return language == English ? English : Chinese;
        }
        catch
        {
            return Chinese;
        }
    }
    private static void SaveLanguage(string language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, language);
        }
        catch
        {
            //保存失败不阻止monitor运行
        }
    }
}
