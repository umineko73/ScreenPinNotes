using System.Windows.Threading;

namespace ScreenPinNotes.Services;

/// <summary>UI-thread save lifecycle. Failures remain pending and retry at a bounded interval.</summary>
public sealed class DeferredNoteSave : IDisposable
{
    private readonly Action _save;
    private readonly Func<int> _debounceMs;
    private readonly DispatcherTimer _timer;
    private long _pendingSince;
    private bool _pending;
    private bool _disabled;

    public DeferredNoteSave(Action save, Func<int> debounceMs, Dispatcher dispatcher)
    {
        _save = save;
        _debounceMs = debounceMs;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Tick += OnTick;
    }

    public void Request()
    {
        if (_disabled) return;
        if (!_pending) _pendingSince = Environment.TickCount64;
        _pending = true;
        var remaining = Math.Max(1, 5000 - (Environment.TickCount64 - _pendingSince));
        Schedule(Math.Min(Math.Max(1, _debounceMs()), remaining));
    }

    public void Flush()
    {
        if (_pending) Save();
    }

    public void Save()
    {
        if (_disabled) return;
        _timer.Stop();
        try
        {
            _save();
            _pending = false;
        }
        catch
        {
            _pending = true;
            Schedule(Math.Max(1000, _debounceMs()));
            throw;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try { Flush(); }
        catch (Exception ex) { ErrorReporter.ReportNonFatal("Deferred save", ex); }
    }

    private void Schedule(long milliseconds)
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _timer.Start();
    }

    public void Dispose()
    {
        _disabled = true;
        _pending = false;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
