using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.App.UI;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    /// <summary>Строка таблицы сотрудников (модель для отображения).</summary>
    public class EmployeeRow
    {
        public long Id { get; set; }
        public string FullName { get; set; } = "";
        public string Affiliation { get; set; } = "";
        public string ArchiveReason { get; set; } = "";
        public string Sanminimum { get; set; } = "";
        public string MedicalExam { get; set; } = "";
        public string Fluorography { get; set; } = "";
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
        private const int PageSize = Pagination.DefaultPageSize;

        private long _totalCount;
        private int _page = 1;
        private bool _isPageActive;
        private readonly LatestLoad _loader = new();
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
            _isPageActive = true;
            _page = 1;
            _ = LoadDataAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _isPageActive = false;
            _loader.Cancel();
            base.OnNavigatedFrom(e);
        }

        private Task LoadDataAsync(bool debounce = false)
        {
            if (!_isPageActive || SearchBox == null)
                return Task.CompletedTask;

            return _loader.RunAsync(async token =>
            {
                ErrorBar.IsOpen = false;
                var request = new EmployeePageRequest
                {
                    SearchText = SearchBox.Text ?? "",
                    StatusFilter = FilterBox.SelectedIndex,
                    Archived = LifecycleBox.SelectedIndex == 1,
                    Page = _page,
                    PageSize = PageSize,
                };
                var result = await Task.Run(
                    () => EmployeeRepository.GetPage(request), token);
                token.ThrowIfCancellationRequested();

                _page = result.Page;
                _totalCount = result.TotalCount;
                var dark = ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
                Rows.Clear();
                foreach (var employee in result.Items)
                    Rows.Add(CreateRow(employee, request.Archived, dark));

                AddButton.IsEnabled = !request.Archived;
                UpdatePagination();
            }, SetLoading, ex =>
            {
                ErrorBar.Message = $"Не удалось загрузить сотрудников. {ex.Message}";
                ErrorBar.IsOpen = true;
            }, debounce ? 300 : 0);
        }

        private static EmployeeRow CreateRow(Employee employee, bool showArchived, bool dark)
        {
            var sanStatus = ExpirationRules.GetSingleCheckupStatus(employee.SanminimumDate);
            var medStatus = ExpirationRules.GetSingleCheckupStatus(employee.MedicalExamDate);
            var fluStatus = ExpirationRules.GetSingleCheckupStatus(employee.FluorographyDate);
            var (sanBg, sanFg) = Badges.For(sanStatus.IsExpired, sanStatus.IsExpiring, dark);
            var (medBg, medFg) = Badges.For(medStatus.IsExpired, medStatus.IsExpiring, dark);
            var (fluBg, fluFg) = Badges.For(fluStatus.IsExpired, fluStatus.IsExpiring, dark);
            return new EmployeeRow
            {
                Id = employee.Id,
                FullName = employee.FullName,
                Affiliation = employee.Affiliation == "внешний"
                    ? "внешний совместитель"
                    : employee.Affiliation,
                ArchiveReason = string.IsNullOrWhiteSpace(employee.ArchiveReason)
                    ? "Причина не указана"
                    : $"Причина: {employee.ArchiveReason}",
                Sanminimum = employee.SanminimumDate,
                MedicalExam = employee.MedicalExamDate,
                Fluorography = employee.FluorographyDate,
                ArchiveVisibility = showArchived ? Visibility.Collapsed : Visibility.Visible,
                RestoreVisibility = showArchived ? Visibility.Visible : Visibility.Collapsed,
                SanminimumBg = sanBg,
                SanminimumFg = sanFg,
                MedicalExamBg = medBg,
                MedicalExamFg = medFg,
                FluorographyBg = fluBg,
                FluorographyFg = fluFg,
            };
        }

        private void SetLoading(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            if (isLoading)
            {
                PreviousPageButton.IsEnabled = false;
                NextPageButton.IsEnabled = false;
            }
            else
            {
                UpdatePagination();
            }
        }

        private void UpdatePagination()
        {
            var totalPages = Pagination.GetTotalPages(_totalCount, PageSize);
            var first = _totalCount == 0 ? 0 : (_page - 1) * PageSize + 1;
            var last = Math.Min((long)_page * PageSize, _totalCount);
            CountText.Text = $"Найдено: {_totalCount}";
            PageText.Text = _totalCount == 0
                ? "Нет записей"
                : $"{first}–{last} из {_totalCount} · страница {_page} из {totalPages}";
            PreviousPageButton.IsEnabled = _page > 1;
            NextPageButton.IsEnabled = _page < totalPages;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPageActive)
                return;
            _page = 1;
            _ = LoadDataAsync(debounce: true);
        }

        private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPageActive)
                return;
            _page = 1;
            _ = LoadDataAsync();
        }

        private void LifecycleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LifecycleBox == null || !_isPageActive)
                return;
            _page = 1;
            _ = LoadDataAsync();
        }

        private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_page <= 1)
                return;
            _page--;
            await LoadDataAsync();
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = Pagination.GetTotalPages(_totalCount, PageSize);
            if (_page >= totalPages)
                return;
            _page++;
            await LoadDataAsync();
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

            var reason = await Dialogs.ArchiveReasonAsync(this, "Архивировать сотрудника?",
                new[] { "Не указано", "Увольнение", "Перевод", "Другое" });
            if (reason != null)
            {
                EmployeeRepository.Archive(id, reason);
                await LoadDataAsync();
            }
        }

        private async void RestoreArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
            {
                EmployeeRepository.RestoreFromArchive(id);
                await LoadDataAsync();
            }
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (await Dialogs.ConfirmAsync(this, "Перемещение в корзину",
                $"Переместить сотрудника «{row?.FullName}» в корзину?", "В корзину"))
            {
                EmployeeRepository.MoveToTrash(id);
                await LoadDataAsync();
            }
        }
    }
}
