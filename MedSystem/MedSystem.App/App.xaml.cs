using Microsoft.UI.Xaml;
using System;
using MedSystem.Data;

namespace MedSystem.App
{
    public partial class App : Application
    {
        private Window? _window;
        public static Window? CurrentWindow { get; private set; }
        public static string? StartupWarning { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Упакованное приложение: база хранится в LocalFolder пользователя
            // (папка установки MSIX доступна только для чтения)
            Db.DbPath = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                "med_system.db");

            try
            {
                BackupService.CreateAutomaticBackupIfDue();
            }
            catch (Exception ex)
            {
                StartupWarning = $"Не удалось создать автоматическую резервную копию: {ex.Message}";
            }

            DatabaseInitializer.Initialize();

            _window = new MainWindow();
            CurrentWindow = _window;
            _window.Activate();
        }
    }
}
