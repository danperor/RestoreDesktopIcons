using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using RestoreDesktopIcons.Models;

namespace RestoreDesktopIcons.Services;

public static class LayoutStorageService
{
    private static readonly string StorageFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layouts.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public static List<LayoutSnapshotModel> LoadSnapshots()
    {
        if (!File.Exists(StorageFilePath))
            return [];

        try
        {
            string json = File.ReadAllText(StorageFilePath);
            var list = JsonSerializer.Deserialize<List<LayoutSnapshotModel>>(json, JsonOptions);
            return list ?? [];
        }
        catch (Exception ex)
        {
            AppLogger.Error($"读取桌面图标快照失败 (文件路径: {StorageFilePath})", ex);
            return [];
        }
    }

    public static void SaveSnapshots(List<LayoutSnapshotModel> snapshots)
    {
        try
        {
            string tempFile = StorageFilePath + ".tmp";
            string json = JsonSerializer.Serialize(snapshots, JsonOptions);
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, StorageFilePath, overwrite: true);
            AppLogger.Info($"成功保存快照列表至磁盘，当前共 {snapshots.Count} 个快照。");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"保存桌面图标快照失败 (目标路径: {StorageFilePath})", ex);
        }
    }

    private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettingsModel LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
            return new AppSettingsModel();

        try
        {
            string json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettingsModel>(json, JsonOptions) ?? new AppSettingsModel();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"读取用户配置失败，使用默认配置 (路径: {SettingsFilePath})", ex);
            return new AppSettingsModel();
        }
    }

    public static void SaveSettings(AppSettingsModel settings)
    {
        try
        {
            string tempFile = SettingsFilePath + ".tmp";
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, SettingsFilePath, overwrite: true);
            AppLogger.Info($"成功保存用户配置至磁盘: UseRealDesktopCapture={settings.UseRealDesktopCapture}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"保存用户配置失败 (路径: {SettingsFilePath})", ex);
        }
    }
}
