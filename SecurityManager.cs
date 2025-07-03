namespace HotkeyManager
{
    /// <summary>
    /// Quản lý bảo mật và chống brute force attack
    /// </summary>
    public class SecurityManager
    {
        private readonly AppConfig config;
        private int failedAttempts = 0;
        private DateTime? lockdownUntil = null;
        private bool isLocked = false;

        // Constants for security settings
        private const int MAX_FAILED_ATTEMPTS = 3;
        private const int LOCKDOWN_MINUTES = 30; // Khóa 30 phút sau 3 lần sai
        private static readonly string SecurityLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HotkeyManager",
            "security.log"
        );
        // Console.WriteLine("💫 SecurityLogPath 💫: " + SecurityLogPath);

        public SecurityManager(AppConfig appConfig)
        {
            config = appConfig;
            LoadSecurityState();
        }

        /// Kiểm tra xem có đang bị lock không
        /// </summary>
        public bool IsLocked
        {
            get
            {
                // Kiểm tra xem đã hết thời gian lock chưa
                if (lockdownUntil.HasValue && DateTime.Now >= lockdownUntil.Value)
                {
                    UnlockSecurity();
                    return false;
                }
                return isLocked;
            }
        }

        /// <summary>
        /// Số lần nhập sai hiện tại
        /// </summary>
        public int FailedAttempts => failedAttempts;

        /// <summary>
        /// Thời gian còn lại của lockdown
        /// </summary>
        public TimeSpan? TimeRemaining
        {
            get
            {
                if (!lockdownUntil.HasValue) return null;
                var remaining = lockdownUntil.Value - DateTime.Now;
                return remaining.TotalSeconds > 0 ? remaining : null;
            }
        }

        /// <summary>
        /// Xử lý khi nhập đúng 2FA
        /// </summary>
        public void OnSuccessfulAuth()
        {
            if (failedAttempts > 0)
            {
                LogSecurityEvent($"✅ 2FA successful after {failedAttempts} failed attempts");
                ResetFailedAttempts();
            }
        }

        /// <summary>
        /// Xử lý khi nhập sai 2FA
        /// </summary>
        public async Task<bool> OnFailedAuthAsync(string attemptedCode)
        {
            failedAttempts++;
            LogSecurityEvent($"❌ 2FA failed attempt #{failedAttempts} - Code: {MaskCode(attemptedCode)}");

            SaveSecurityState();

            if (failedAttempts >= MAX_FAILED_ATTEMPTS)
            {
                await HandleSecurityBreachAsync();
                return true; // Đã trigger security breach
            }

            return false; // Chưa đến ngưỡng
        }

        /// <summary>
        /// Xử lý khi vi phạm bảo mật (3 lần sai)
        /// </summary>
        private async Task HandleSecurityBreachAsync()
        {
            isLocked = true;
            lockdownUntil = DateTime.Now.AddMinutes(LOCKDOWN_MINUTES);

            LogSecurityEvent($"🚨 SECURITY BREACH! Lockdown activated until {lockdownUntil:yyyy-MM-dd HH:mm:ss}");

            // Gửi request lên server để hủy/thay đổi mã 2FA
            bool revoked = await RevokeCurrentTwoFAAsync();

            if (revoked)
            {
                LogSecurityEvent("🔒 2FA code revoked successfully on server");
                // Tạo mã 2FA mới
                await GenerateNew2FAAsync();
            }
            else
            {
                LogSecurityEvent("⚠️ Failed to revoke 2FA code on server");
            }

            SaveSecurityState();
        }

        /// <summary>
        /// Gửi request lên server D1 để hủy mã 2FA hiện tại
        /// </summary>
        private async Task<bool> RevokeCurrentTwoFAAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", config.ApiToken);

                    // Tạo request body với thông tin security breach
                    var revokeData = new
                    {
                        action = "revoke_2fa",
                        reason = "security_breach",
                        failed_attempts = failedAttempts,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        client_id = Environment.MachineName + "_" + Environment.UserName
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(revokeData);
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                    // Gửi POST request để revoke
                    var response = await client.PostAsync($"{config.ApiEndpoint}/revoke-2fa", content);

                    LogSecurityEvent($"📡 Revoke request sent - Status: {response.StatusCode}");

                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                LogSecurityEvent($"💥 Error revoking 2FA: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tạo mã 2FA mới từ server
        /// </summary>
        private async Task GenerateNew2FAAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", config.ApiToken);

                    var response = await client.PostAsync($"{config.ApiEndpoint}/generate-new-2fa", null);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        var newData = System.Text.Json.JsonSerializer.Deserialize<dynamic>(responseBody);

                        // Cập nhật config với mã mới (nếu server trả về)
                        LogSecurityEvent($"🔄 New 2FA generated: {responseBody}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogSecurityEvent($"💥 Error generating new 2FA: {ex.Message}");
            }
        }

        /// <summary>
        /// Mở khóa security khi hết thời gian
        /// </summary>
        private void UnlockSecurity()
        {
            isLocked = false;
            lockdownUntil = null;
            ResetFailedAttempts();
            LogSecurityEvent("🔓 Security lockdown expired - System unlocked");
            SaveSecurityState();
        }

        /// <summary>
        /// Reset số lần nhập sai
        /// </summary>
        private void ResetFailedAttempts()
        {
            failedAttempts = 0;
            SaveSecurityState();
        }

        /// <summary>
        /// Che mã code trong log để bảo mật
        /// </summary>
        private string MaskCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "***";
            if (code.Length <= 2) return new string('*', code.Length);
            return code.Substring(0, 1) + new string('*', code.Length - 2) + code.Substring(code.Length - 1);
        }

        /// <summary>
        /// Ghi log sự kiện bảo mật
        /// </summary>
        private void LogSecurityEvent(string message)
        {
            try
            {
                var logDir = Path.GetDirectoryName(SecurityLogPath);
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir!);
                }

                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(SecurityLogPath, logEntry);
            }
            catch
            {
                // Không crash ứng dụng nếu không ghi được log
            }
        }

        /// <summary>
        /// Lưu trạng thái security vào file
        /// </summary>
        private void SaveSecurityState()
        {
            try
            {
                var securityState = new
                {
                    FailedAttempts = failedAttempts,
                    IsLocked = isLocked,
                    LockdownUntil = lockdownUntil?.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                var securityPath = Path.Combine(
                    Path.GetDirectoryName(SecurityLogPath)!,
                    "security_state.json"
                );
                Console.WriteLine("💫 SecurityState: " + securityState);

                var json = System.Text.Json.JsonSerializer.Serialize(securityState, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(securityPath, json);
            }
            catch
            {
                // Silent fail
            }
        }

        /// <summary>
        /// Khôi phục trạng thái security từ file
        /// </summary>
        private void LoadSecurityState()
        {
            try
            {
                var securityPath = Path.Combine(
                    Path.GetDirectoryName(SecurityLogPath)!,
                    "security_state.json"
                );

                if (File.Exists(securityPath))
                {
                    var json = File.ReadAllText(securityPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("FailedAttempts", out var attemptsElement))
                        failedAttempts = attemptsElement.GetInt32();

                    if (root.TryGetProperty("IsLocked", out var lockedElement))
                        isLocked = lockedElement.GetBoolean();

                    if (root.TryGetProperty("LockdownUntil", out var lockdownElement) &&
                        lockdownElement.GetString() is string lockdownStr &&
                        DateTime.TryParse(lockdownStr, out var lockdownTime))
                    {
                        lockdownUntil = lockdownTime;
                    }

                    // Kiểm tra xem lockdown đã hết hạn chưa
                    if (lockdownUntil.HasValue && DateTime.Now >= lockdownUntil.Value)
                    {
                        UnlockSecurity();
                    }
                }
            }
            catch
            {
                // Reset to default state if can't load
                failedAttempts = 0;
                isLocked = false;
                lockdownUntil = null;
            }
        }

        /// <summary>
        /// Lấy thông báo trạng thái cho user
        /// </summary>
        public string GetStatusMessage()
        {
            if (IsLocked)
            {
                var remaining = TimeRemaining;
                if (remaining.HasValue)
                {
                    return $"🔒 Bị khóa do nhập sai 2FA {MAX_FAILED_ATTEMPTS} lần!\n" +
                           $"Thời gian mở khóa: {remaining.Value.Minutes}p {remaining.Value.Seconds}s";
                }
            }

            if (failedAttempts > 0)
            {
                return $"⚠️ Đã nhập sai {failedAttempts}/{MAX_FAILED_ATTEMPTS} lần";
            }

            return "🔓 Bảo mật bình thường";
        }
    }
}