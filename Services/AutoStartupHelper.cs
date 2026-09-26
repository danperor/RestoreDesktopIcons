using Microsoft.Win32;

namespace RestoreDesktopIcons.Services;

/// <summary>
/// 开机自启动辅助类（通过当前用户注册表 Run 键）
/// </summary>
public static class AutoStartupHelper
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "RestoreDesktopIcons";

    /// <summary>
    /// 查询当前是否已设置开机自启动
    /// </summary>
    public static bool IsAutoStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? value = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("查询注册表开机自启动配置失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 设置或取消开机自启动
    /// </summary>
    public static bool SetAutoStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                    AppLogger.Info($"已设置开机自启动(静默托盘模式): {exePath} --minimized");
                }
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                    AppLogger.Info("已取消开机自启动");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"设置开机自启动异常 (enable={enable})", ex);
            return false;
        }
    }
}
