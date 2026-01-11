using Avalonia.Controls;
using Avalonia.Interactivity;
using AvalonClient.ViewModels;
using System.ComponentModel;

namespace AvalonClient.Views.Pages;

public partial class GamePage : UserControl
{
    private readonly Button[,] _enemyBtn = new Button[GameViewModel.N, GameViewModel.N];
    private readonly Button[,] _selfBtn  = new Button[GameViewModel.N, GameViewModel.N];

    public GamePage()
    {
        InitializeComponent();
        BuildGrids();
        this.DataContextChanged += (_, _) => HookVm();
    }

    private void HookVm()
    {
        if (DataContext is GameViewModel vm)
        {
            vm.PropertyChanged += VmOnPropertyChanged;
            Refresh(vm);
        }
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GameViewModel vm) return;

        if (e.PropertyName == nameof(GameViewModel.EnemyRows) ||
            e.PropertyName == nameof(GameViewModel.SelfRows) ||
            e.PropertyName == nameof(GameViewModel.MyTurn))
        {
            Refresh(vm);
        }
    }

    private void BuildGrids()
    {
        EnemyGrid.Children.Clear();
        SelfGrid.Children.Clear();

        for (int y = 0; y < GameViewModel.N; y++)
        for (int x = 0; x < GameViewModel.N; x++)
        {
            // enemy cell (clickable)
            var eb = new Button
            {
                Width = 34,
                Height = 34,
                Padding = new Avalonia.Thickness(0),
                Margin = new Avalonia.Thickness(1),
                Tag = (x, y),
                Content = "."
            };
            eb.Click += EnemyClick;
            _enemyBtn[x, y] = eb;
            EnemyGrid.Children.Add(eb);

            // self cell (view only)
            var sb = new Button
            {
                Width = 34,
                Height = 34,
                Padding = new Avalonia.Thickness(0),
                Margin = new Avalonia.Thickness(1),
                IsEnabled = false,
                Content = "."
            };
            _selfBtn[x, y] = sb;
            SelfGrid.Children.Add(sb);
        }
    }

    private void EnemyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameViewModel vm) return;
        if (sender is not Button b) return;

        var (x, y) = ((int X, int Y))b.Tag!;
        vm.ShootAt(x, y);
    }

    private void Refresh(GameViewModel vm)
    {
        // update button contents from rows
        var er = vm.EnemyRows;
        var sr = vm.SelfRows;

        for (int y = 0; y < GameViewModel.N; y++)
        {
            var rowE = er[y];
            var rowS = sr[y];

            for (int x = 0; x < GameViewModel.N; x++)
            {
                _enemyBtn[x, y].Content = rowE[x].ToString();
                _enemyBtn[x, y].IsEnabled = vm.MyTurn; // only clickable on your turn

                _selfBtn[x, y].Content = rowS[x].ToString();
            }
        }
    }
}
