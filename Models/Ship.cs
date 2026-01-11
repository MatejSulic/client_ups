namespace AvalonClient.Models;

public sealed class Ship
{
    public int X { get; set; }   // top-left
    public int Y { get; set; }
    public int Len { get; set; }
    public char Dir { get; set; } // 'H'/'V'

    public override string ToString() => $"{Len}{Dir} @ ({X},{Y})";


    public Ship(int x, int y, int len, char dir)
        {
            X = x;
            Y = y;
            Len = len;
            Dir = dir;
        }
}
