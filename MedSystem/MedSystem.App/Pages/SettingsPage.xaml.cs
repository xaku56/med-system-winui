using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Data;
using MedSystem.Data.Repositories;
using Windows.Storage.Pickers;

namespace MedSystem.App.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            ThemeRadios.SelectedIndex = ThemeHelper.Current switch
            {
                ElementTheme.Light => 1,
                ElementTheme.Dark => 2,
                _ => 0,
            };
            var retentionDays = TrashRepository.GetRetentionDays();
            NeverDeleteTrashCheckBox.IsChecked = retentionDays == 0;
            TrashRetentionBox.Value = retentionDays == 0
                ? TrashRepository.DefaultRetentionDays
                : retentionDays;
            TrashRetentionBox.IsEnabled = retentionDays != 0;
            _loading = false;
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            VersionText.Text = $"Версия: {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            DbPathText.Text = $"База данных: {Db.DbPath}";
            UpdateBackupStatus();
        }

        private void NeverDeleteTrashCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            TrashRetentionBox.IsEnabled = NeverDeleteTrashCheckBox.IsChecked != true;
        }

        private async void SaveTrashRetentionButton_Click(object sender, RoutedEventArgs e)
        {
            if (NeverDeleteTrashCheckBox.IsChecked != true &&
                (double.IsNaN(TrashRetentionBox.Value) ||
                 TrashRetentionBox.Value != Math.Truncate(TrashRetentionBox.Value)))
            {
                await ShowMessageAsync("Проверьте срок хранения", "Количество дней должно быть целым числом.");
                return;
            }

            var retentionDays = NeverDeleteTrashCheckBox.IsChecked == true
                ? 0
                : (int)TrashRetentionBox.Value;

            try
            {
                TrashRepository.SetRetentionDays(retentionDays);
                await ShowMessageAsync(
                    "Настройки сохранены",
                    retentionDays == 0
                        ? "Автоматическая очистка корзины отключена."
                        : $"Записи будут храниться в корзине {retentionDays} дн.");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Не удалось сохранить настройку", ex.Message);
            }
        }

        private void ThemeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            ThemeHelper.Apply(ThemeRadios.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default,
            });
        }

        private async void CreateBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = $"med_system_{DateTime.Now:yyyy-MM-dd_HH-mm}",
            };
            picker.FileTypeChoices.Add("База данных SQLite", new List<string> { ".db" });
            InitializePicker(picker);

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            SetBackupBusy(true);
            try
            {
                await Task.Run(() => BackupService.CreateManualBackup(file.Path));
                UpdateBackupStatus();
                await ShowMessageAsync("Резервная копия создана", $"Копия сохранена:\n{file.Path}");
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Не удалось создать копию", ex.Message);
            }
            finally
            {
                SetBackupBusy(false);
            }
        }

        private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".db");
            InitializePicker(picker);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            var confirmation = new ContentDialog
            {
                Title = "Восстановить базу данных?",
                Content = "Текущая база будет сохранена в страховочную копию, затем заменена выбранной. После восстановления приложение закроется.",
                PrimaryButtonText = "Восстановить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                return;

            SetBackupBusy(true);
            try
            {
                var safetyBackup = await Task.Run(() => BackupService.RestoreBackup(file.Path));
                await ShowMessageAsync(
                    "База восстановлена",
                    $"Перед восстановлением создана страховочная копия:\n{safetyBackup}\n\nПриложение сейчас закроется. Запустите его снова.");
                App.CurrentWindow?.Close();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Не удалось восстановить базу", ex.Message);
            }
            finally
            {
                SetBackupBusy(false);
            }
        }

        private void UpdateBackupStatus()
        {
            var latestPath = BackupService.GetLatestBackupPath();
            BackupStatusText.Text = latestPath == null
                ? "Автоматических копий пока нет."
                : $"Последняя внутренняя копия: {File.GetLastWriteTime(latestPath):dd.MM.yyyy HH:mm}\n{latestPath}";
        }

        private void SetBackupBusy(bool isBusy)
        {
            CreateBackupButton.IsEnabled = !isBusy;
            RestoreBackupButton.IsEnabled = !isBusy;
            BackupProgress.IsActive = isBusy;
            BackupProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InitializePicker(object picker)
        {
            if (App.CurrentWindow == null)
                throw new InvalidOperationException("Главное окно приложения ещё не создано.");

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "Понятно",
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            await dialog.ShowAsync();
        }
    }
}
