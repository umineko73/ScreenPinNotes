using System.IO;
using ScreenPinNotes.Models;
using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class NoteSaveCoordinatorTests
{
    [Fact]
    public void UnchangedNotesAreSkipped_DirectAndNestedEditsAreSaved()
    {
        var writes = new List<NoteSnapshot>();
        var saves = new NoteSaveCoordinator(writes.Add);
        var note = new StickyNote { Content = "one", Reminder = new() { NextAt = DateTime.Today } };
        Assert.True(saves.Save(note));
        Assert.False(saves.Save(note));
        note.IsHidden = true;
        Assert.True(saves.Save(note));
        note.Reminder.NextAt = DateTime.Today.AddDays(1);
        Assert.True(saves.Save(note));
        note.Content = "two";
        Assert.True(saves.Save(note));
        Assert.Equal(4, writes.Count);
        Assert.Equal("one", writes[0].Content);
        Assert.Equal("two", writes[^1].Content);
    }

    [Fact]
    public void FailureRemainsDirty_AndDoesNotPreventOtherNotesSaving()
    {
        var fail = true;
        var written = new List<string>();
        var saves = new NoteSaveCoordinator(snapshot =>
        {
            if (snapshot.Id == "first" && fail) throw new IOException("Unavailable");
            written.Add(snapshot.Id);
        });
        var first = new StickyNote { Id = "first" };
        var second = new StickyNote { Id = "second" };
        var error = Assert.Throws<AggregateException>(() => NoteSaveCoordinator.SaveAll(
            [() => saves.Save(first), () => saves.Save(second)]));
        Assert.Single(error.InnerExceptions);
        Assert.Equal(["second"], written);
        fail = false;
        Assert.True(saves.Save(first));
        Assert.False(saves.Save(second));
        Assert.Equal(["second", "first"], written);
    }
}
