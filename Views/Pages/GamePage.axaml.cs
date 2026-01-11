using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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

    private static IBrush EnemyBrush(char c)
    {
        // Enemy view:
        // M = water (modrá)
        // H = hit (zelená)
        // K (nebo třeba 'S' pokud bys posílal) = sunk (červená)
        return c switch
        {
            'M' => Brushes.DodgerBlue,
            'H' => Brushes.LimeGreen,
            'K' => Brushes.IndianRed,
            'S' => Brushes.IndianRed, // kdyby sis někdy poslal sunk jako S
            _ => Brushes.LightGray
        };
    }

    private static IBrush SelfBrush(char c)
    {
        // Nechávám basic. Klidně si to můžeš upravit:
        // S = tvoje loď (šedá světle), H = zásah (červená), M = miss (modrá).
        return c switch
        {
            'H' => Brushes.IndianRed,
            'M' => Brushes.DodgerBlue,
            'S' => Brushes.Gainsboro,
            _ => Brushes.LightGray
        };
    }

    private static IBrush ForegroundFor(IBrush bg)
    {
        // Ať je text čitelný na barvách
        if (ReferenceEquals(bg, Brushes.LightGray) || ReferenceEquals(bg, Brushes.Gainsboro))
            return Brushes.Black;
        return Brushes.White;
    }

    private void Refresh(GameViewModel vm)
    {
        var er = vm.EnemyRows;
        var sr = vm.SelfRows;

        for (int y = 0; y < GameViewModel.N; y++)
        {
            var rowE = er[y];
            var rowS = sr[y];

            for (int x = 0; x < GameViewModel.N; x++)
            {
                // Enemy
                char ec = rowE[x];
                var eb = _enemyBtn[x, y];
                eb.Content = ec.ToString();
                eb.IsEnabled = vm.MyTurn; // only clickable on your turn

                var ebg = EnemyBrush(ec);
                eb.Background = ebg;
                eb.Foreground = ForegroundFor(ebg);

                // Self
                char sc = rowS[x];
                var sb = _selfBtn[x, y];
                sb.Content = sc.ToString();

                var sbg = SelfBrush(sc);
                sb.Background = sbg;
                sb.Foreground = ForegroundFor(sbg);
            }
        }
    }
}
