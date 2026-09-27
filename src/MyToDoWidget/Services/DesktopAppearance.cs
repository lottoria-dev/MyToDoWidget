using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace MyToDoWidget.Services;

public static class DesktopAppearance
{
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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
