using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MyToDoWidget.Services;

/// <summary>Move the settings caption without entering Windows' native Snap move loop.</summary>
internal sealed class DetachedWindowMovement
{
    private readonly Window _window;
    private HwndSource? _source;
    private DesktopAppearance.PointerMove? _move;
    private IntPtr _handle;
    public DetachedWindowMovement(Window window)
    {
        _window = window;
        window.SourceInitialized += SourceInitialized;
        window.Closed += WindowClosed;
    }
    private void SourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_handle); _source?.AddHook(WindowProc);
    }
    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int Caption = 2;
        if (message == 0x00A1 && wParam.ToInt64() == Caption) // WM_NCLBUTTONDOWN
        {
            _window.Activate();
            var move = DesktopAppearance.BeginPointerMove(_window);
            if (move is not null)
            {
                _move = move; SetCapture(hwnd);
                if (GetCapture() != hwnd) _move = null;
            }
            handled = true; // Never pass the caption drag to DefWindowProc.
        }
        else if (message == 0x00A3 && wParam.ToInt64() == Caption) handled = true; // No double-click maximize.
        else if (message == 0x0112 && (wParam.ToInt64() & 0xFFF0) is 0xF030 or 0xF010) handled = true; // SC_MAXIMIZE / native SC_MOVE
        else if (_move is not null)
        {
            if (message == 0x0200) // WM_MOUSEMOVE
            {
                if ((GetAsyncKeyState(1) & 0x8000) == 0) Finish(false);
                else _move.Update();
                handled = true;
            }
            else if (message is 0x0202 or 0x00A2) // Mouse button up in client/non-client area.
            { _move.Update(); Finish(false); handled = true; }
            else if (message == 0x0100 && wParam.ToInt64() == 27) // Escape cancels this drag.
            { Finish(true); handled = true; }
            else if (message is 0x0215 or 0x001F) Finish(false); // Capture changed / cancel mode.
        }
        return IntPtr.Zero;
    }
    private void Finish(bool cancel)
    {
        var move = _move; _move = null; // ReleaseCapture sends WM_CAPTURECHANGED synchronously.
        if (cancel) move?.Restore();
        if (GetCapture() == _handle) ReleaseCapture();
    }
    private void WindowClosed(object? sender, EventArgs e)
    {
        Finish(false);
        if (_source is not null)
        {
            try { _source.RemoveHook(WindowProc); }
            catch (ObjectDisposedException) { }
            _source = null;
        }
        _window.SourceInitialized -= SourceInitialized; _window.Closed -= WindowClosed;
    }
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
}
