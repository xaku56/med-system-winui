using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MedSystem.App.Pages;
using MedSystem.Data.Repositories;

namespace MedSystem.App
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Title = "Медицинская система";

            // Mica-фон и контент под заголовком окна (Fluent Design)
            SystemBackdrop = new MicaBackdrop();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));

            // SettingsItem создаётся при применении шаблона NavigationView,
            // а в конструкторе он ещё null — переводим по событию Loaded.
            Nav.Loaded += (_, _) =>
            {
                if (Nav.SettingsItem is NavigationViewItem settingsItem)
                {
                    settingsItem.Content = "Настройки";
                    ToolTipService.SetToolTip(settingsItem, "Настройки");
                }
            };

            ThemeHelper.Initialize(this);
            ContentFrame.Navigate(typeof(HomePage));

            if (!string.IsNullOrWhiteSpace(App.StartupWarning))
            {
                StartupInfoBar.Title = "Резервное копирование";
                StartupInfoBar.Message = App.StartupWarning;
                StartupInfoBar.Severity = InfoBarSeverity.Warning;
                StartupInfoBar.IsOpen = true;
            }

            // Автоперевод групп на следующий курс (раз в год после 15 августа)
            DispatcherQueue.TryEnqueue(CheckAcademicYear);
        }

        private void CheckAcademicYear()
        {
            try
            {
                var count = GroupRepository.CheckAndAutoIncrementGroups();
                if (count > 0)
                {
                    StartupInfoBar.Title = "Новый учебный год";
                    StartupInfoBar.Message =
                        $"Начался новый учебный год! Групп переведено на следующий курс: {count}.";
                    StartupInfoBar.Severity = InfoBarSeverity.Informational;
                    StartupInfoBar.IsOpen = true;
                }
            }
            catch
            {
                // Не мешаем запуску приложения
            }
        }

        private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                    ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
                return;

            Type? pageType = tag switch
            {
                "home" => typeof(HomePage),
                "employees" => typeof(EmployeesPage),
                "students" => typeof(StudentsPage),
                "medicines" => typeof(MedicinesPage),
                "appeals" => typeof(AppealsPage),
                _ => null,
            };

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
                ContentFrame.Navigate(pageType);
        }
    }
}
