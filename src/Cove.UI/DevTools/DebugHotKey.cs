#if DEBUG
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Cove.UI.DevTools;

/// <summary>
/// A temporary global shortcut for trying the pill by hand before the shortcut service (S-04) and
/// the Menu (C-12) exist. Debug builds only. TODO(C-12): remove.
/// </summary>
/// <remarks>
/// Uses RegisterHotKey on a hidden, message-only window (see RegisterHotKey in Microsoft's docs).
/// A shortcut press also lets Windows hand Cove focus, which is what opening a panel needs.
/// </remarks>
public sealed class DebugHotKey : IDisposable
{
    // Our own number for this shortcut, sent back with each WM_HOTKEY message.
    private const int HotKeyId = 1;

    // HWND_MESSAGE: makes a message-only window, which is never shown and only receives messages.
    private static readonly nint MessageOnlyParent = -3;

    private readonly HwndSource _window;
    private readonly Action _onPressed;
    private readonly bool _registered;

    public DebugHotKey(ModifierKeys modifiers, Key key, Action onPressed)
    {
        ArgumentNullException.ThrowIfNull(onPressed);
        _onPressed = onPressed;
        _window = new HwndSource(new HwndSourceParameters("Cove debug shortcut") { ParentWindow = MessageOnlyParent, WindowStyle = 0 });
        _window.AddHook(OnWindowMessage);

        // NOREPEAT: holding the keys down fires once, not over and over.
        var nativeModifiers = ToNative(modifiers) | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        _registered = PInvoke.RegisterHotKey((HWND)_window.Handle, HotKeyId, nativeModifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!_registered)
        {
            Log.Warning(
                "Couldn't register the debug shortcut {Modifiers}+{Key}; another app may use it (Win32 error {Error})",
                modifiers,
                key,
                Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey((HWND)_window.Handle, HotKeyId);
        }

        _window.RemoveHook(OnWindowMessage);
        _window.Dispose();
    }

    private static HOT_KEY_MODIFIERS ToNative(ModifierKeys modifiers)
    {
        HOT_KEY_MODIFIERS result = 0;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= HOT_KEY_MODIFIERS.MOD_ALT;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= HOT_KEY_MODIFIERS.MOD_WIN;
        }

        return result;
    }

    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == (int)PInvoke.WM_HOTKEY && wParam == HotKeyId)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }
}
#endif
