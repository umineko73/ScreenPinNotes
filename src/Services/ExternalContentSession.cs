namespace ScreenPinNotes.Services;

public readonly record struct ExternalContentReadResult(bool Success, string Content);

/// <summary>
/// Coalesces refreshes into one reader. Only the latest requested result may reach the UI.
/// The dispatcher callback is supplied by the view; this class has no WPF dependency.
/// </summary>
public sealed class ExternalContentSession(
    Func<CancellationToken, Task<ExternalContentReadResult>> read,
    Func<Action, Task> dispatch,
    Action<string> apply,
    Func<int> minIntervalMs,
    Func<int> retryIntervalMs) : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private bool _running;
    private long _generation;
    private long? _lastRead;
    private Task _completion = Task.CompletedTask;

    /// <summary>The current refresh/retry loop, also useful when shutting down or testing.</summary>
    public Task Completion { get { lock (_gate) return _completion; } }

    public void Request()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _generation++;
            if (_running) return;
            _running = true;
            _completion = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        var retry = false;
        try
        {
            while (true)
            {
                var interval = retry ? Math.Max(200, retryIntervalMs()) : Math.Max(0, minIntervalMs());
                var delay = _lastRead is long last
                    ? Math.Max(0, interval - (Environment.TickCount64 - last)) : 0;
                if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), _lifetime.Token).ConfigureAwait(false);
                _lifetime.Token.ThrowIfCancellationRequested();
                long generation;
                lock (_gate) generation = _generation;
                _lastRead = Environment.TickCount64;
                ExternalContentReadResult result;
                // Opening a file can itself block (for example on a network share).
                // Even the synchronous prefix of the reader runs away from the UI.
                try { result = await Task.Run(() => read(_lifetime.Token), _lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    ErrorReporter.ReportNonFatal("Read external content", ex);
                    result = new(false, "");
                }
                _lifetime.Token.ThrowIfCancellationRequested();
                if (result.Success)
                {
                    await dispatch(() =>
                    {
                        lock (_gate)
                        {
                            if (_disposed || generation != _generation) return;
                            apply(result.Content);
                        }
                    }).ConfigureAwait(false);
                }
                lock (_gate)
                {
                    if (_disposed) return;
                    if (result.Success && generation == _generation)
                    {
                        _running = false;
                        return;
                    }
                }
                retry = !result.Success;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lock (_gate) _running = false;
            ErrorReporter.ReportNonFatal("Refresh external content", ex);
        }
        finally
        {
            // On the successful path _running was reset under the same lock as Request.
            // Do not reset it again: a subsequent request may already own a new loop.
            lock (_gate)
            {
                if (_disposed) _running = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
        }
        // The reader may still be observing the token; do not dispose its source here.
        _lifetime.Cancel();
    }
}
