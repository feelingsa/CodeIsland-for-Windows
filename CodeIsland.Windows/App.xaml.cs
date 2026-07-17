using System.Configuration;
using System.Data;
using System.Windows;

using System.Threading;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace CodeIsland.Windows;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private NotifyIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, "CodeIsland.Windows.SingleInstance", out var created);
        if (!created)
        {
            Shutdown(2);
            return;
        }

        _window = new MainWindow();
        _window.Show();
        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "CodeIsland"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开面板", null, (_, _) => ShowPanel());
        menu.Items.Add("收起面板", null, (_, _) => _window.Hide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowPanel();
        base.OnStartup(e);
    }

    private void ShowPanel()
    {
        if (_window is null) return;
        _window.Show();
        _window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

