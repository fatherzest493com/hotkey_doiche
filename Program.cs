namespace HotkeyManager;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        
        // Tạo application context cho ứng dụng tray
        using (var trayContext = new TrayApplicationContext())
        {
            Application.Run(trayContext);
        }
    }
    
    public class TrayApplicationContext : ApplicationContext
    {
        private Form1 hiddenForm;

        public TrayApplicationContext()
        {
            hiddenForm = new Form1();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hiddenForm?.Dispose();
            }
            base.Dispose(disposing);
        }
    }    
}