using System.Windows.Markup;
using System.Windows.Media;

namespace ScreenPinNotes.Services;

public static class FontCatalog
{
    public sealed record Entry(string Source, string DisplayName);
    private static Task<Entry[]>? _loading;
    private static readonly object Gate = new();

    public static Task<Entry[]> LoadAsync()
    {
        lock (Gate)
        {
            if (_loading == null || _loading.IsFaulted || _loading.IsCanceled)
                _loading = Task.Run(() => Fonts.SystemFontFamilies.Select(f =>
                {
                    var name = f.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("ja-jp"), out var ja)
                        ? ja : f.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out var en) ? en : f.Source;
                    return new Entry(f.Source, name);
                }).DistinctBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
                  .OrderBy(f => f.DisplayName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ja-JP"), true)).ToArray());
            return _loading;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> SymbolFonts = new();
    private static Task<Entry[]>? _filtered;
    public static Task<Entry[]> FilterAsync(Entry[] fonts)
    {
        lock (Gate)
            return _filtered ??= FilterCoreAsync(fonts);
    }
    private static Task<Entry[]> FilterCoreAsync(Entry[] fonts) => Task.Run(() => fonts.Where(f =>
        !SymbolFonts.GetOrAdd(f.Source, source =>
        {
            try
            {
                var faces = new System.Windows.Media.FontFamily(source).GetTypefaces().ToArray();
                // Unknown/unavailable faces stay in the list. Never scan glyph maps.
                return faces.Length > 0 && faces.All(t => t.TryGetGlyphTypeface(out var glyph) && glyph.Symbol);
            }
            catch { return false; }
        })).ToArray());
}
