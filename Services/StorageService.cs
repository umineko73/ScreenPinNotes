using System.IO;
using System.Text.Json;
using ScreenStickyNotes.Models;

namespace ScreenStickyNotes.Services;

public class StorageService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenStickyNotes");
    private static readonly string DataFile = Path.Combine(DataDir, "notes.json");
    private static readonly string TempFile = DataFile + ".tmp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public List<StickyNote> Load()
    {
        if (!File.Exists(DataFile)) return [];
        try
        {
            var json = File.ReadAllText(DataFile);
            return JsonSerializer.Deserialize<List<StickyNote>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<StickyNote> notes)
    {
        Directory.CreateDirectory(DataDir);
        var json = JsonSerializer.Serialize(notes.ToList(), JsonOptions);
        File.WriteAllText(TempFile, json);
        File.Move(TempFile, DataFile, overwrite: true);
    }
}
