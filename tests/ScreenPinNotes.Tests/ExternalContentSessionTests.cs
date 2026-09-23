using ScreenPinNotes.Services;

namespace ScreenPinNotes.Tests;

public class ExternalContentSessionTests
{
    private static Task Dispatch(Action action) { action(); return Task.CompletedTask; }

    [Fact]
    public async Task MultipleRequestsShareOneReader_AndDiscardObsoleteResults()
    {
        var first = new TaskCompletionSource<ExternalContentReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<string>();
        var reads = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new ExternalContentSession(_ =>
        {
            if (++reads != 1) return Task.FromResult(new ExternalContentReadResult(true, "latest"));
            started.SetResult();
            return first.Task;
        },
            Dispatch, applied.Add, () => 0, () => 200);
        session.Request();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Request();
        session.Request();
        Assert.Equal(1, reads);
        first.SetResult(new(true, "obsolete"));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, reads);
        Assert.Equal(["latest"], applied);
    }

    [Fact]
    public async Task DisposedSessionCannotApplyAnInFlightRead()
    {
        var read = new TaskCompletionSource<ExternalContentReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<string>();
        var session = new ExternalContentSession(_ => read.Task, Dispatch, applied.Add, () => 0, () => 200);
        session.Request();
        session.Dispose();
        read.SetResult(new(true, "late"));
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        session.Request();
        Assert.Empty(applied);
    }

    [Fact]
    public async Task FailureRetriesWithoutAnotherNotification_AndKeepsOldContent()
    {
        var reads = 0;
        var applied = new List<string>();
        using var session = new ExternalContentSession(_ => Task.FromResult(
            ++reads == 1 ? new ExternalContentReadResult(false, "error") : new(true, "recovered")),
            Dispatch, applied.Add, () => 0, () => 200);
        session.Request();
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["recovered"], applied);
        Assert.Equal(2, reads);
    }
}
