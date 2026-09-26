namespace RestoreDesktopIcons.Models;

public record RectModel(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);

    public bool Contains(int x, int y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;

    public override string ToString() => $"[{Left},{Top} -> {Right},{Bottom} ({Width}x{Height})]";
}
