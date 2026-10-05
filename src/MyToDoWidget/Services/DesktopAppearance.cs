using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace MyToDoWidget.Services;

public static class DesktopAppearance
{
    // Screen-pixel coordinates avoid a native move loop and its edge Snap UI.
    public sealed class PointerMove
    {
        private readonly IntPtr _handle;
        private readonly int _left, _top, _pointerX, _pointerY;
        public bool HasMoved { get; private set; }

        internal PointerMove(IntPtr handle, int left, int top, int pointerX, int pointerY)
        {
            _handle = handle; _left = left; _top = top; _pointerX = pointerX; _pointerY = pointerY;
        }

        public void Update()
        {
            if (!GetCursorPos(out var pointer)) return;
            int dx = pointer.X - _pointerX, dy = pointer.Y - _pointerY;
            if (dx == 0 && dy == 0 && !HasMoved) return;
            if (SetWindowPos(_handle, IntPtr.Zero, _left + dx, _top + dy, 0, 0, 0x0015)) HasMoved = true;
        }

        public void Restore() => SetWindowPos(_handle, IntPtr.Zero, _left, _top, 0, 0, 0x0015);
    }

    public static PointerMove? BeginPointerMove(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect) || !GetCursorPos(out var pointer)) return null;
        return new PointerMove(handle, rect.Left, rect.Top, pointer.X, pointer.Y);
    }

    public static DesktopPosition? CapturePosition(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return null;
        var screen = Forms.Screen.FromHandle(handle);
        var area = screen.WorkingArea;
        return new DesktopPosition(screen.DeviceName, area.Width, area.Height,
            rect.Left - area.Left, rect.Top - area.Top);
    }

    public static string DisplayLayout() => string.Join(";", Forms.Screen.AllScreens
        .OrderBy(s => s.DeviceName).Select(s => $"{s.DeviceName}:{s.WorkingArea}"));

    public static bool RestorePosition(Window window, DesktopPosition preferred)
    {
        if (!preferred.IsValid) return false;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == preferred.DisplayName);
        if (screen is null) return false; // An absent monitor must not replace the saved preference.
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return false;
        var area = screen.WorkingArea;
        // Moving onto another DPI monitor can change native size. Recheck after the move.
        for (int pass = 0; pass < 2; pass++)
        {
            var point = preferred.Resolve(area.Left, area.Top, area.Width, area.Height,
                rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (rect.Left == point.X && rect.Top == point.Y) return true;
            if (!SetWindowPos(handle, IntPtr.Zero, point.X, point.Y, 0, 0, 0x0015)) return false;
            if (!GetWindowRect(handle, out rect)) return false;
        }
        return true;
    }

    public static void PlaceSettingsBeside(Window settings, Window owner)
    {
        var hwnd = new WindowInteropHelper(settings).Handle;
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        if (!GetWindowRect(hwnd, out var dialog) || !GetWindowRect(ownerHandle, out var widget)) return;
        var area = Forms.Screen.FromHandle(ownerHandle).WorkingArea;
        int width = dialog.Right - dialog.Left, height = dialog.Bottom - dialog.Top;
        int gap = (int)Math.Round(12 * VisualTreeHelper.GetDpi(owner).DpiScaleX);
        int x = widget.Right + gap;
        if (x + width > area.Right) x = widget.Left - gap - width;
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        int y = Math.Clamp(widget.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0015); // Keep size, z-order and focus.
    }

    public static void EnsureVisible(Window window, bool forcePrimary = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        var bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        bool visible = Forms.Screen.AllScreens.Any(screen =>
        {
            var intersection = System.Drawing.Rectangle.Intersect(screen.WorkingArea, bounds);
            return intersection.Width >= 100 && intersection.Height >= Math.Min(80, bounds.Height)
                && rect.Top >= screen.WorkingArea.Top && rect.Top < screen.WorkingArea.Bottom - 40;
        });
        if (visible && !forcePrimary) return;
        var area = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).WorkingArea;
        int width = Math.Min(bounds.Width, area.Width), height = Math.Min(bounds.Height, area.Height);
        SetWindowPos(hwnd, IntPtr.Zero, area.Right - width - Math.Min(24, Math.Max(0, area.Width - width)),
            area.Top + Math.Min(48, Math.Max(0, area.Height - height)), width, height, 0x0014);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
