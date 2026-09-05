// ScreenPinNotes - Copyright (C) 2026 umineko73
// Licensed under GPL-3.0-or-later.
using System.Globalization;
using System.IO;
using System.Resources;

namespace ScreenPinNotes.Services;

public static class LocalizationService
{
    private static readonly ResourceManager Resources = new("ScreenPinNotes.Resources.Strings", typeof(LocalizationService).Assembly);
    public sealed record LanguageOption(string Code, string NativeName);
    public static IReadOnlyList<LanguageOption> Languages { get; } = DiscoverLanguages();

    private static IReadOnlyList<LanguageOption> DiscoverLanguages()
    {
        var result = new List<LanguageOption> { new("en", "English") };
        var assembly = typeof(LocalizationService).Assembly;
        using var stream = assembly.GetManifestResourceStream("ScreenPinNotes.Languages")
            ?? throw new InvalidOperationException("Missing language catalog.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } name)
        {
            if (!name.StartsWith("Strings.", StringComparison.Ordinal)) continue;
            try
            {
                var culture = CultureInfo.GetCultureInfo(name["Strings.".Length..]);
                if (Resources.GetResourceSet(culture, true, false) != null)
                    result.Add(new(culture.Name, culture.NativeName));
            }
            catch (CultureNotFoundException) { }
        }
        return result.OrderBy(l => l.Code, StringComparer.Ordinal).ToArray();
    }

    public static string ResolveLanguage(string? language, string fallback = "en")
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(language ?? "");
            while (!string.IsNullOrEmpty(culture.Name))
            {
                var found = Languages.FirstOrDefault(l => string.Equals(l.Code, culture.Name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found.Code;
                culture = culture.Parent;
            }
        }
        catch (CultureNotFoundException) { }
        return fallback;
    }

    public static string T(string key) => T(key, App.Current.Settings.Language);
    public static string T(string key, string language)
        => Resources.GetString(key, CultureInfo.GetCultureInfo(ResolveLanguage(language))) ?? key;
}
