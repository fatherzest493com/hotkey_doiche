using System;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace HotkeyManager
{
    public enum HotkeyType
    {
        None,      // Không có hotkey nào được nhấn
        Show2FA,   // Ctrl+K rồi Ctrl+8 trong thời gian 2.5 giây
        ToggleTray // Ctrl+K rồi Ctrl+T trong thời gian 2.5 giây
    }

    public partial class Form1 : Form
    {
        // DLL import để kiểm tra phím nhấn
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(Keys vKey);

        private System.Windows.Forms.Timer hotkeyTimer = null!;
        private bool hasPressedK = false;
        private DateTime lastK = DateTime.MinValue;
        private NotifyIcon trayIcon = null!;
        private AppConfig config = null!;
        private SecurityManager security = null!;

        public Form1()
        {
            InitializeComponent();
            config = AppConfig.Load(); // Tải cài đặt  
            security = new SecurityManager(config); // Khởi tạo security manager
            InitializeHiddenForm();
            InitTimer();
            InitTrayIcon();
        }

        void InitializeHiddenForm()
        {
            // Ẩn form chính
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Visible = false;
        }

        void InitTimer()
        {
            hotkeyTimer = new System.Windows.Forms.Timer { Interval = 50 };
            hotkeyTimer.Tick += HotkeyTimer_Tick;
            hotkeyTimer.Start();
        }

        void InitTrayIcon()
        {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Application; // Sử dụng icon mặc định
            trayIcon.Text = "Hotkey Manager - Chạy ngầm";
            trayIcon.Visible = config.ShowTrayIcon; // Sử dụng cài đặt từ config

            // Tạo context menu cho tray icon
            var contextMenu = new ContextMenuStrip();
            
            var statusItem = new ToolStripMenuItem($"Trạng thái: {security.GetStatusMessage()}");
            statusItem.Enabled = false; // Chỉ hiển thị, không click được
            
            var toggleTrayItem = new ToolStripMenuItem("Ẩn tray icon");
            toggleTrayItem.Click += (s, e) => ToggleTrayIcon();
            
            var helpItem = new ToolStripMenuItem("Hướng dẫn");
            helpItem.Click += (s, e) => ShowHelp();
            
            var resetSecurityItem = new ToolStripMenuItem("Reset bảo mật");
            resetSecurityItem.Click += (s, e) => ResetSecurity();
            resetSecurityItem.Enabled = security.FailedAttempts > 0; // Chỉ hiện khi có lỗi
            
            var exitItem = new ToolStripMenuItem("Thoát");
            exitItem.Click += (s, e) => {
                trayIcon.Visible = false;
                Application.Exit();
            };
            
            contextMenu.Items.Add(statusItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(toggleTrayItem);
            contextMenu.Items.Add(helpItem);
            contextMenu.Items.Add(resetSecurityItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);
            trayIcon.ContextMenuStrip = contextMenu;

            // Double-click để hiển thị thông báo
            trayIcon.DoubleClick += (s, e) => ShowHelp();
        }



        private async void HotkeyTimer_Tick(object? sender, EventArgs e)
        {
            var hotkeyResult = CheckHotkeySequence(ref hasPressedK, ref lastK);
            
            if (hotkeyResult == HotkeyType.Show2FA)
            {
                hotkeyTimer.Stop(); // Dừng timer tạm thời khi kích hoạt hotkey

                // Kiểm tra security lockdown trước khi hiển thị popup
                if (security.IsLocked)
                {
                    ShowNotification($"🔒 SECURITY LOCKDOWN!\n{security.GetStatusMessage()}");
                    await Task.Delay(500);
                    hotkeyTimer.Start();
                    return;
                }

                string? entered2FA = TwoFAPopup.ShowPopup(security);

                if (!string.IsNullOrEmpty(entered2FA))
                {
                    if (Verify2FA(entered2FA))
                    {
                        // ✅ Nhập đúng 2FA
                        security.OnSuccessfulAuth();
                        ShowNotification("✅ 2FA xác thực thành công!");
                        await SendD1RequestAsync();
                    }
                    else
                    {
                        // ❌ Nhập sai 2FA
                        bool isSecurityBreach = await security.OnFailedAuthAsync(entered2FA);
                        
                        if (isSecurityBreach)
                        {
                            ShowNotification($"🚨 BẢO MẬT VI PHẠM!\n{security.GetStatusMessage()}");
                        }
                        else
                        {
                            ShowNotification($"❌ Mã 2FA không đúng!\n{security.GetStatusMessage()}");
                        }
                    }
                }

                await Task.Delay(500);
                hotkeyTimer.Start(); // Khởi động lại timer
            }
            else if (hotkeyResult == HotkeyType.ToggleTray)
            {
                ToggleTrayIcon();
                await Task.Delay(500);
            }
        }

        /// <summary>
        /// Toggle tray icon visibility
        /// </summary>
        private void ToggleTrayIcon()
        {
            config.ToggleTrayIcon();
            trayIcon.Visible = config.ShowTrayIcon;
            
            // Hiển thị notification để người dùng biết
            ShowNotification(config.ShowTrayIcon ? "Tray icon đã được hiển thị" : 
                           "Tray icon đã được ẩn. Nhấn Ctrl+K rồi Ctrl+T để hiển thị lại.");
        }

        /// <summary>
        /// Hiển thị hướng dẫn sử dụng
        /// </summary>
        private void ShowHelp()
        {
            ShowNotification("🔐 Hotkey Manager\n" +
                           "• Ctrl+K → Ctrl+8: Hiển thị popup 2FA\n" +
                           "• Ctrl+K → Ctrl+T: Ẩn/hiện tray icon\n" +
                           "• Click chuột phải tray: Menu tùy chọn");
        }

        /// <summary>
        /// Hiển thị Windows notification
        /// </summary>
        private void ShowNotification(string message)
        {
            if (config.ShowTrayIcon && trayIcon.Visible)
            {
                trayIcon.ShowBalloonTip(4000, "Hotkey Manager", message, ToolTipIcon.Info);
            }
            else
            {
                // Nếu tray icon bị ẩn, sử dụng MessageBox tạm thời
                MessageBox.Show(message, "Hotkey Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Reset security state (chỉ dành cho admin/debug)
        /// </summary>
        private void ResetSecurity()
        {
            var result = MessageBox.Show(
                "Bạn có chắc muốn reset trạng thái bảo mật?\n\n" +
                "⚠️ Thao tác này sẽ:\n" +
                "• Xóa tất cả lần nhập sai\n" +
                "• Mở khóa nếu đang bị lockdown\n" +
                "• Ghi log sự kiện reset\n\n" +
                "Chỉ thực hiện khi bạn chắc chắn!",
                "🔧 Reset Security", 
                MessageBoxButtons.YesNo, 
                MessageBoxIcon.Warning
            );

            if (result == DialogResult.Yes)
            {
                // Reset security state thông qua reflection hoặc public method
                // Tạm thời tạo SecurityManager mới
                security = new SecurityManager(config);
                ShowNotification("🔄 Trạng thái bảo mật đã được reset!");
            }
        }

        static HotkeyType CheckHotkeySequence(ref bool hasPressedK, ref DateTime lastK)
        {
            // Phát hiện Ctrl+K
            if ((GetAsyncKeyState(Keys.ControlKey) & 0x8000) != 0 &&
                (GetAsyncKeyState(Keys.K) & 0x8000) != 0)
            {
                if (!hasPressedK)
                {
                    hasPressedK = true;
                    lastK = DateTime.Now;
                }
            }

            // Kiểm tra các hotkey combination trong thời gian 2.5 giây
            if (hasPressedK && (DateTime.Now - lastK).TotalSeconds < 2.5)
            {
                // Ctrl+K rồi Ctrl+8 (hiển thị 2FA popup)
                if ((GetAsyncKeyState(Keys.ControlKey) & 0x8000) != 0 &&
                    (GetAsyncKeyState(Keys.D8) & 0x8000) != 0)
                {
                    hasPressedK = false;
                    return HotkeyType.Show2FA;
                }

                // Ctrl+K rồi Ctrl+T (toggle tray icon)
                if ((GetAsyncKeyState(Keys.ControlKey) & 0x8000) != 0 &&
                    (GetAsyncKeyState(Keys.T) & 0x8000) != 0)
                {
                    hasPressedK = false;
                    return HotkeyType.ToggleTray;
                }
            }

            // Reset nếu quá thời gian chờ
            if (hasPressedK && (DateTime.Now - lastK).TotalSeconds >= 2.5)
            {
                hasPressedK = false;
            }

            return HotkeyType.None;
        }

        class TwoFAPopup : Form
        {
            private TextBox tb = null!;
            private Button btnOK = null!;
            private Label statusLabel = null!;
            [Browsable(false)]
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public string? Code { get; private set; } = null;

            public TwoFAPopup(SecurityManager? securityManager = null)
            {
                Width = 300;
                Height = 180;
                StartPosition = FormStartPosition.CenterScreen;
                TopMost = true;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowInTaskbar = false;
                Text = "🔐 2FA Authentication";
                KeyPreview = true;

                // Header label
                Label headerLbl = new Label  { Text = "Nhập mã 2FA:",  Left = 20,   Width = 250, Font = new System.Drawing.Font("Segoe UI", 10, FontStyle.Bold) };
                
                // Input section
                Label lbl = new Label { Text = "Mã:", Left = 20, Top = 45, Width = 35 };
                tb = new TextBox 
                { 
                    Left = 60, Top = 42, Width = 200, PasswordChar = '•', Font = new System.Drawing.Font("Segoe UI", 14), MaxLength = 10
                };
                
                // Security status
                statusLabel = new Label
                {
                    Left = 20, Top = 75, Width = 250, Height = 25, Font = new System.Drawing.Font("Segoe UI", 9), ForeColor = Color.DarkBlue, Text = securityManager?.GetStatusMessage() ?? "🔓 Bảo mật bình thường"
                };

                // Buttons
                btnOK = new Button 
                { 
                    Text = "Xác nhận", Left = 90, Top = 110, Width = 80, Height = 35, Font = new System.Drawing.Font("Segoe UI", 10), BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
                };
                
                var btnCancel = new Button
                {
                    Text = "Hủy", Left = 180, Top = 110, Width = 80, Height = 35, Font = new System.Drawing.Font("Segoe UI", 10), BackColor = Color.Gray, ForeColor = Color.White, FlatStyle = FlatStyle.Flat
                };

                btnOK.Click += (s, e) => { Code = tb.Text; DialogResult = DialogResult.OK; Close(); };
                btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

                Controls.Add(headerLbl);
                Controls.Add(lbl);
                Controls.Add(tb);
                Controls.Add(statusLabel);
                Controls.Add(btnOK);
                Controls.Add(btnCancel);

                // Auto timeout
                var tmr = new System.Windows.Forms.Timer { Interval = 30000 };
                tmr.Tick += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                tmr.Start();

                // Enter key handling
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) btnOK.PerformClick(); };
                
                // ESC key handling
                this.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape)
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                    }
                };
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                tb.Focus();
                tb.Select();
            }

            public static string? ShowPopup(SecurityManager? securityManager = null)
            {
                using (var box = new TwoFAPopup(securityManager))
                {
                    var result = box.ShowDialog();
                    return result == DialogResult.OK ? box.Code : null;
                }
            }
        }

        bool Verify2FA(string inputCode) => inputCode == config.Valid2FA;

        async Task<bool> SendD1RequestAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", config.ApiToken);
                    HttpResponseMessage resp = await client.GetAsync(config.ApiEndpoint);
                    var body = await resp.Content.ReadAsStringAsync();
                    File.AppendAllText("request_log.txt", DateTime.Now + " - " + body + Environment.NewLine);
                    return resp.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("request_log.txt", DateTime.Now + " - ERROR: " + ex.Message + Environment.NewLine);
                return false;
            }
        }
    }
}
