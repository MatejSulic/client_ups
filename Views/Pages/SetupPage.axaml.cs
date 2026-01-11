using Avalonia.Controls;
using Avalonia.Interactivity;
using AvalonClient.ViewModels;
using System.ComponentModel;

namespace AvalonClient.Views.Pages;

public partial class SetupPage : UserControl
{
    private readonly Button[,] _btn = new Button[SetupViewModel.N, SetupViewModel.N];

    public SetupPage()
    {
        InitializeComponent();
        BuildBoard();
        this.DataContextChanged += (_, _) => HookVm();
    }

    private void HookVm()
    {
        if (DataContext is SetupViewModel vm)
        {
            vm.PropertyChanged += VmOnPropertyChanged;
            RefreshBoard(vm);
        }
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SetupViewModel vm) return;
        if (e.PropertyName == nameof(SetupViewModel.SelfRows) ||
            e.PropertyName == nameof(SetupViewModel.Status))
        {
            RefreshBoard(vm);
        }
    }

    private void BuildBoard()
    {
        BoardGrid.Children.Clear();

        for (int y = 0; y < SetupViewModel.N; y++)
        {
            for (int x = 0; x < SetupViewModel.N; x++)
            {
                var b = new Button
                {
                    Width = 34,
                    Height = 34,
                    Padding = new Avalonia.Thickness(0),
                    Margin = new Avalonia.Thickness(1),
                    Tag = (x, y),
                    Content = "."
                };

                b.Click += CellClick;

                _btn[x, y] = b;
                BoardGrid.Children.Add(b);
            }
        }
    }

    private void CellClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SetupViewModel vm) return;
        if (sender is not Button b) return;

        var (x, y) = ((int X, int Y))b.Tag!;
        vm.PlaceAt(x, y);
        RefreshBoard(vm);
    }

    private void RefreshBoard(SetupViewModel vm)
    {
        var rows = vm.SelfRows;
        if (rows.Count != SetupViewModel.N) return;

        for (int y = 0; y < SetupViewModel.N; y++)
        {
            var row = rows[y];
            if (row.Length != SetupViewModel.N) continue;

            for (int x = 0; x < SetupViewModel.N; x++)
                _btn[x, y].Content = row[x].ToString();
        }
    }
}
