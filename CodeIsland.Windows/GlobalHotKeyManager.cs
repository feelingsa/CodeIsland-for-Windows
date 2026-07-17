using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Interop;

namespace CodeIsland.Windows;

[SupportedOSPlatform("windows")]
public sealed class GlobalHotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private readonly HwndSource _source;
    private readonly Action _toggle;
    private readonly Action _approve;
    private readonly Action _deny;
    private bool _disposed;

    public bool ToggleRegistered { get; }
    public bool ApproveRegistered { get; }
    public bool DenyRegistered { get; }

    public GlobalHotKeyManager(HwndSource source, Action toggle, Action approve, Action deny)
    {
        _source = source;
        _toggle = toggle;
        _approve = approve;
        _deny = deny;
        _source.AddHook(WindowProc);
        ToggleRegistered = Register(1, (uint)'I');
        ApproveRegistered = Register(2, (uint)'A');
        DenyRegistered = Register(3, (uint)'D');
    }

    private bool Register(int id, uint key) => RegisterHotKey(_source.Handle, id, ModControl | ModShift, key);

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotKey) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case 1: _toggle(); handled = true; break;
            case 2: _approve(); handled = true; break;
            case 3: _deny(); handled = true; break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.RemoveHook(WindowProc);
        UnregisterHotKey(_source.Handle, 1);
        UnregisterHotKey(_source.Handle, 2);
        UnregisterHotKey(_source.Handle, 3);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
