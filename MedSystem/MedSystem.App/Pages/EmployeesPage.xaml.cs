using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Core;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    /// <summary>Строка таблицы сотрудников (модель для отображения).</summary>
    public class EmployeeRow
    {
        public long Id { get; set; }
        public string FullName { get; set; } = "";
        public string Affiliation { get; set; } = "";
        public string Sanminimum { get; set; } = "";
        public string MedicalExam { get; set; } = "";
        public string Fluorography { get; set; } = "";
        public bool IsExpired { get; set; }
        public bool IsExpiring { get; set; }
        public Visibility ArchiveVisibility { get; set; }
        public Visibility RestoreVisibility { get; set; }
        public Microsoft.UI.Xaml.Media.Brush SanminimumBg { get; set; } = Badges.TransparentBg;
        public Microsoft.UI.Xaml.Media.Brush SanminimumFg { get; set; } = Badges.NormalFg;
        public Microsoft.UI.Xaml.Media.Brush MedicalExamBg { get; set; } = Badges.TransparentBg;
        public Microsoft.UI.Xaml.Media.Brush MedicalExamFg { get; set; } = Badges.NormalFg;
        public Microsoft.UI.Xaml.Media.Brush FluorographyBg { get; set; } = Badges.TransparentBg;
        public Microsoft.UI.Xaml.Media.Brush FluorographyFg { get; set; } = Badges.NormalFg;
    }

    public sealed partial class EmployeesPage : Page
    {
        private List<EmployeeRow> _allRows = new();
        public ObservableCollection<EmployeeRow> Rows { get; } = new();

        public EmployeesPage()
        {
            InitializeComponent();
            // Страница кэшируется: поиск и фильтр сохраняются между переходами,
            // данные всё равно перезагружаются в OnNavigatedTo
            NavigationCacheMode = NavigationCacheMode.Required;
            EmployeesList.ItemsSource = Rows;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadData();
        }

        private void LoadData()
        {
            var dark = ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
            var showArchived = LifecycleBox.SelectedIndex == 1;
            _allRows = EmployeeRepository.GetAll(archived: showArchived).Select(emp =>
            {
                var sanStatus = ExpirationRules.GetSingleCheckupStatus(emp.SanminimumDate);
                var medStatus = ExpirationRules.GetSingleCheckupStatus(emp.MedicalExamDate);
                var fluStatus = ExpirationRules.GetSingleCheckupStatus(emp.FluorographyDate);
                var (sanBg, sanFg) = Badges.For(sanStatus.IsExpired, sanStatus.IsExpiring, dark);
                var (medBg, medFg) = Badges.For(medStatus.IsExpired, medStatus.IsExpiring, dark);
                var (fluBg, fluFg) = Badges.For(fluStatus.IsExpired, fluStatus.IsExpiring, dark);
                var (isExpired, isExpiring) = ExpirationRules.GetPersonStatus(
                    new[] { emp.SanminimumDate, emp.MedicalExamDate, emp.FluorographyDate });
                return new EmployeeRow
                {
                    Id = emp.Id,
                    FullName = emp.FullName,
                    Affiliation = emp.Affiliation == "внешний" ? "внешний совместитель" : emp.Affiliation,
                    Sanminimum = emp.SanminimumDate,
                    MedicalExam = emp.MedicalExamDate,
                    Fluorography = emp.FluorographyDate,
                    IsExpired = isExpired,
                    IsExpiring = isExpiring,
                    ArchiveVisibility = showArchived ? Visibility.Collapsed : Visibility.Visible,
                    RestoreVisibility = showArchived ? Visibility.Visible : Visibility.Collapsed,
                    SanminimumBg = sanBg, SanminimumFg = sanFg,
                    MedicalExamBg = medBg, MedicalExamFg = medFg,
                    FluorographyBg = fluBg, FluorographyFg = fluFg,
                };
            }).ToList();
            AddButton.IsEnabled = !showArchived;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (SearchBox == null || FilterBox == null)
                return;

            var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            IEnumerable<EmployeeRow> filtered = _allRows;

            if (!string.IsNullOrEmpty(query))
                filtered = filtered.Where(r => r.FullName.ToLowerInvariant().Contains(query));

            filtered = FilterBox.SelectedIndex switch
            {
                1 => filtered.Where(r => r.IsExpired),   // Просроченные
                2 => filtered.Where(r => r.IsExpiring),  // Истекают (2 недели)
                _ => filtered,
            };

            var list = filtered.ToList();
            Rows.Clear();
            foreach (var row in list)
                Rows.Add(row);

            CountText.Text = $"Всего: {list.Count}";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

        private void LifecycleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LifecycleBox == null)
                return;
            LoadData();
        }

        // ── Действия ─────────────────────────────────────────────────

        private void AddButton_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(EmployeeFormPage), 0L);

        private void EmployeesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (EmployeesList.SelectedItem is EmployeeRow row)
                Frame.Navigate(typeof(EmployeeFormPage), row.Id);
        }

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
                Frame.Navigate(typeof(EmployeeFormPage), id);
        }

        private async void ArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var reasonBox = new ComboBox
            {
                Header = "Причина",
                MinWidth = 320,
                ItemsSource = new[] { "Увольнение", "Перевод", "Другое" },
                SelectedIndex = 0,
            };
            var dialog = new ContentDialog
            {
                Title = "Архивировать сотрудника?",
                Content = reasonBox,
                PrimaryButtonText = "Архивировать",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                EmployeeRepository.Archive(id, reasonBox.SelectedItem?.ToString() ?? "Другое");
                LoadData();
            }
        }

        private void RestoreArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
            {
                EmployeeRepository.RestoreFromArchive(id);
                LoadData();
            }
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var row = _allRows.FirstOrDefault(r => r.Id == id);
            var dialog = new ContentDialog
            {
                Title = "Перемещение в корзину",
                Content = $"Переместить сотрудника «{row?.FullName}» в корзину?",
                PrimaryButtonText = "В корзину",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                EmployeeRepository.MoveToTrash(id);
                LoadData();
            }
        }
    }
}
