using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoxelLauncher
{
    public class AppSettings
    {
        public bool CheckForUpdates { get; set; } = true;
        public bool ShowLoginNotification { get; set; } = true;
        public bool DisableAllNotifications { get; set; } = false;
        public string MinecraftFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
    }

    public static class SettingsHelper
    {
        private static readonly string ConfigPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelClient", "settings.json");

        public static async Task<AppSettings> LoadAsync()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = await File.ReadAllTextAsync(ConfigPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public static async Task SaveAsync(AppSettings settings)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(ConfigPath, json);
            }
            catch { }
        }
    }
}