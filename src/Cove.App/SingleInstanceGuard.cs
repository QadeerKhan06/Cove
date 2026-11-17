namespace Cove.App;

/// <summary>
/// Keeps Cove to one running copy per Windows session, and lets a second copy ask the first
/// one to open the Menu before it exits.
/// </summary>
/// <remarks>
/// Uses two named kernel objects that any process in the same session can open by name:
/// a mutex whose existence means "Cove is running", and an auto-reset event that a second copy
/// sets to mean "open the Menu". The <c>Local\</c> prefix scopes both to the current session,
/// so another user signed in on the same PC gets their own Cove.
/// See <see cref="Mutex"/> and <see cref="EventWaitHandle"/> in the .NET docs.
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly EventWaitHandle _showMenuSignal;
    private readonly Mutex _instanceMutex;
    private RegisteredWaitHandle? _listener;
    private bool _disposed;

    /// <param name="appId">
    /// A name unique to this app, used to build the kernel object names. Every copy of the app
    /// must pass the same value; tests pass their own so they never meet a real Cove.
    /// </param>
    public SingleInstanceGuard(string appId)
    {
        // Create the event before the mutex: a second copy only signals after it finds the
        // mutex, so the event always exists by then and the signal can't be lost.
        _showMenuSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{appId}.ShowMenu");

        // We never lock the mutex; whether it already existed is the whole answer. Not owning it
        // avoids the thread-affinity rules and "abandoned mutex" errors that come with locking.
        _instanceMutex = new Mutex(false, $@"Local\{appId}.Instance", out bool createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>True if no other copy with the same app id was running when this guard was created.</summary>
    public bool IsFirstInstance { get; }

    /// <summary>
    /// Raised when another copy calls <see cref="SignalFirstInstance"/>. Runs on a thread-pool
    /// thread, so dispatch to the UI thread before touching WPF objects.
    /// </summary>
    public event EventHandler? ShowMenuRequested;

    /// <summary>Starts waiting for signals from other copies. First instance only.</summary>
    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsFirstInstance)
        {
            throw new InvalidOperationException("Only the first instance listens for signals.");
        }

        // A thread-pool wait needs no thread of its own, and the auto-reset event re-arms after
        // each signal, so this one registration handles every later launch.
        _listener ??= ThreadPool.RegisterWaitForSingleObject(
            _showMenuSignal,
            (_, _) => ShowMenuRequested?.Invoke(this, EventArgs.Empty),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Asks the first instance to open the Menu. Second instance only.</summary>
    public void SignalFirstInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsFirstInstance)
        {
            throw new InvalidOperationException("The first instance has no other copy to signal.");
        }

        _showMenuSignal.Set();
    }

    /// <summary>
    /// Stops listening and closes both kernel objects, so the next launch becomes the first instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener?.Unregister(null);
        _instanceMutex.Dispose();
        _showMenuSignal.Dispose();
    }
}
