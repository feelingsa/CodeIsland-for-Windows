using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodeIsland.Windows;

[SupportedOSPlatform("windows")]
public static class FullscreenDetector
{
    public static bool IsFullscreenForeground(IntPtr ignoredWindow)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ignoredWindow) return false;
        if (!GetWindowRect(foreground, out var window)) return false;
        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeGetMonitorInfo(monitor, ref info)) return false;
        return IsSameBounds(ToRectangle(window), ToRectangle(info.Monitor));
    }

    public static bool IsSameBounds(Rectangle window, Rectangle monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    private const uint MonitorDefaultToNearest = 2;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool NativeGetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    private static Rectangle ToRectangle(NativeRect rect) =>
        Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
