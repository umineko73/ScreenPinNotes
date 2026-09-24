// ScreenPinNotes - a desktop sticky notes app for Windows 11
// Copyright (C) 2026 umineko73
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.IO;
using System.Text.Json;

namespace ScreenPinNotes.Services;

/// <summary>One render's vault lookup. Never searches outside the enclosing vault.</summary>
public sealed class ObsidianAttachments
{
    private readonly string noteDirectory;
    private readonly string? vault;
    private Dictionary<string, string?>? files;

    public ObsidianAttachments(string notePath)
    {
        noteDirectory = Path.GetDirectoryName(Path.GetFullPath(notePath))!;
        for (var directory = new DirectoryInfo(noteDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".obsidian")))
            { vault = directory.FullName; break; }
    }

    public string? Resolve(string target)
    {
        if (vault == null || Path.IsPathRooted(target) || target.Contains("://")) return null;
        target = Uri.UnescapeDataString(target).Replace('/', Path.DirectorySeparatorChar);
        string? Existing(string directory, string relative)
        {
            var path = Path.GetFullPath(Path.Combine(directory, relative));
            var root = vault.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
        }
        try
        {
            var direct = Existing(noteDirectory, target) ?? Existing(vault, target);
            if (direct != null) return direct;
            var configPath = Path.Combine(vault, ".obsidian", "app.json");
            if (File.Exists(configPath) && new FileInfo(configPath).Length <= 65536)
            {
                try
                {
                    using var config = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (config.RootElement.TryGetProperty("attachmentFolderPath", out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        var folder = value.GetString()!;
                        var relative = folder == "." || folder.StartsWith("./", StringComparison.Ordinal);
                        direct = Existing(relative ? noteDirectory : vault, Path.Combine(folder, target));
                        if (direct != null) return direct;
                    }
                }
                catch (JsonException) { /* The vault can still be searched. */ }
            }
            // A render builds at most one bounded index. Ambiguous names stay unresolved.
            if (target.Contains(Path.DirectorySeparatorChar)) return null;
            files ??= BuildIndex();
            return files.GetValueOrDefault(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return null; }
    }

    private Dictionary<string, string?> BuildIndex()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System, MaxRecursionDepth = 32 };
        var count = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(vault!, "*", options))
        {
            // An incomplete index cannot establish uniqueness.
            if (++count > 10000) return new(StringComparer.OrdinalIgnoreCase);
            if (!LinkDetector.IsRenderableImageTarget(path) || !File.Exists(path)) continue;
            var name = Path.GetFileName(path);
            if (!result.TryAdd(name, path)) result[name] = null;
        }
        return result;
    }
}
