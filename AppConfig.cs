using System.Text.Json;

namespace HotkeyManager
{
    /// <summary>
    /// Quản lý cài đặt ứng dụng
    /// </summary>
    public class AppConfig
    {
        public bool ShowTrayIcon { get; set; } = true;
        public string Valid2FA { get; set; } = "246813";
        public string ApiEndpoint { get; set; } = "https://anki-cloud-api.scbip9.workers.dev";
        public string ApiToken { get; set; } = "Bearer YOUR_TOKEN";

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HotkeyManager",
            "config.json"
        );

        /// <summary>
        /// Tải cài đặt từ file
        /// </summary>
        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    return config ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nhưng không crash ứng dụng
                File.AppendAllText("error_log.txt", 
                    $"{DateTime.Now} - Config Load Error: {ex.Message}{Environment.NewLine}");
            }
            
            return new AppConfig();
        }

        /// <summary>
        /// Lưu cài đặt vào file
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                File.AppendAllText("error_log.txt", 
                    $"{DateTime.Now} - Config Save Error: {ex.Message}{Environment.NewLine}");
            }
        }

        /// <summary>
        /// Toggle tray icon setting
        /// </summary>
        public void ToggleTrayIcon()
        {
            ShowTrayIcon = !ShowTrayIcon;
            Save();
        }
    }
} 