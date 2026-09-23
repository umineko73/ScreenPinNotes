using System.Text.Json;
using ScreenPinNotes.Models;

namespace ScreenPinNotes.Services;

/// <summary>Immutable persistence data: later model edits cannot change an in-flight write.</summary>
public sealed record NoteSnapshot(string Id, string Metadata, string Content)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static NoteSnapshot Capture(StickyNote note)
        => new(note.Id, JsonSerializer.Serialize(note, Options), note.Content);
}

/// <summary>Tracks successfully persisted values, including changes made directly to a model.</summary>
public sealed class NoteSaveCoordinator(Action<NoteSnapshot> write)
{
    private readonly Dictionary<string, NoteSnapshot> _saved = new(StringComparer.Ordinal);

    public void Remember(NoteSnapshot snapshot) => _saved[snapshot.Id] = snapshot;
    public void Forget(string id) => _saved.Remove(id);

    public bool Save(StickyNote note)
    {
        var snapshot = NoteSnapshot.Capture(note);
        if (_saved.TryGetValue(note.Id, out var previous) && previous == snapshot)
            return false;
        write(snapshot);
        // A failed write must never mark the note as saved.
        Remember(snapshot);
        return true;
    }

    /// <summary>Attempt every note before reporting failures to the caller.</summary>
    public static void SaveAll(IEnumerable<Action> saves)
    {
        List<Exception>? failures = null;
        foreach (var save in saves)
        {
            try { save(); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures != null)
            throw new AggregateException("Some notes could not be saved.", failures);
    }
}
