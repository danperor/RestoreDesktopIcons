namespace RestoreDesktopIcons.Models;

public record IconItemModel
{
    /// <summary>
    /// 人类可读的显示名称（如 "此电脑"、"微信"、"Edge"）
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 唯一主键：物理绝对路径或 Shell 虚拟项 GUID (例如 ::{645FF040-5081-101B-9F08-00AA002F954E})
    /// </summary>
    public string ParsingName { get; init; } = string.Empty;

    /// <summary>
    /// 二进制 PIDL Base64 序列化指纹（底层壳层唯一结构）
    /// </summary>
    public string? PidlBase64 { get; init; }

    /// <summary>
    /// 若为快捷方式 (.lnk)，其指向的实际程序路径（防止用户重命名快捷方式后丢失）
    /// </summary>
    public string? ShortcutTarget { get; init; }

    /// <summary>
    /// 虚拟屏幕绝对 X 坐标
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// 虚拟屏幕绝对 Y 坐标
    /// </summary>
    public int Y { get; set; }

    public override string ToString() => $"'{DisplayName}' -> ({X}, {Y}) [{ParsingName}]";
}
