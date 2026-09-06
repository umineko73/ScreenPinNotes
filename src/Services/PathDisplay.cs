using System.Globalization;

namespace ScreenPinNotes.Services;

public static class PathDisplay
{
    public static string Fit(string path, double width, Func<string, double> measure)
    {
        if (width <= 0) return "";
        if (measure(path) <= width) return path;
        const string prefix = "…/";
        // Prefer complete trailing directory names before shortening the filename itself.
        for (var i = 0; i < path.Length - 1; i++)
            if (path[i] is '/' or '\\')
            {
                var candidate = prefix + path[(i + 1)..];
                if (measure(candidate) <= width) return candidate;
            }
        var name = path[(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1)..];
        foreach (var index in StringInfo.ParseCombiningCharacters(name))
        {
            var candidate = "…" + name[index..];
            if (measure(candidate) <= width) return candidate;
        }
        return measure("…") <= width ? "…" : "";
    }
}
