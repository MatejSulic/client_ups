using Avalonia.Controls;
using AvalonClient.Services;
using AvalonClient.ViewModels;

namespace AvalonClient.Views;

public partial class MainWindow : Window
{
    private readonly ClientSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(_session);
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _session.Dispose();
        base.OnClosed(e);
    }
}
