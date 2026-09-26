using System.IO;
using System.Runtime.Versioning;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace RestoreDesktopIcons.Services;

[SupportedOSPlatform("windows5.1.2600")]
public static class ShortcutResolver
{
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");

    public static unsafe string? ResolveShortcutTarget(string shortcutPath)
    {
        if (string.IsNullOrEmpty(shortcutPath) || !File.Exists(shortcutPath) || !shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            Type? shellLinkType = Type.GetTypeFromCLSID(CLSID_ShellLink);
            if (shellLinkType == null) return null;

            var shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
            var persistFile = (IPersistFile)shellLink;

            fixed (char* pPath = shortcutPath)
            {
                persistFile.Load(pPath, 0);
            }

            Span<char> targetBuffer = stackalloc char[512];
            fixed (char* pTarget = targetBuffer)
            {
                shellLink.GetPath(pTarget, 512, null, 0);
            }

            string res = new string(targetBuffer).TrimEnd('\0');
            return string.IsNullOrWhiteSpace(res) ? null : res;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"解析快捷方式物理目标失败: {shortcutPath}", ex);
            return null;
        }
    }
}
