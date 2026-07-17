using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CodeIsland.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _expanded = true;
    private const double ExpandedWidth = 560;
    private const double ExpandedHeight = 160;
    private const double CollapsedWidth = 260;
    private const double CollapsedHeight = 64;

    public MainWindow(DesktopSessionStore sessions)
    {
        InitializeComponent();
        DataContext = sessions;
        Loaded += (_, _) => PositionPanel();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        MouseLeftButtonDown += (_, _) => DragMove();
        MouseDoubleClick += (_, _) => TogglePanel();
    }

    private void PositionPanel()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + 14;
    }

    public void TogglePanel()
    {
        _expanded = !_expanded;
        Width = _expanded ? ExpandedWidth : CollapsedWidth;
        Height = _expanded ? ExpandedHeight : CollapsedHeight;
        PositionPanel();
    }
}
