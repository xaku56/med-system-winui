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
    public class AppealRow
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string Sender { get; set; } = "";
        public string Complaints { get; set; } = "";
    }

    public sealed partial class AppealsPage : Page
    {
        private const int PageSize = Pagination.DefaultPageSize;

        private long _totalCount;
        private int _page = 1;
        private bool _isPageActive;
        private readonly LatestLoad _loader = new();
        public ObservableCollection<AppealRow> Rows { get; } = new();

        public AppealsPage()
        {
            InitializeComponent();
            // Страница кэшируется: поиск и фильтр сохраняются между переходами,
            // данные всё равно перезагружаются в OnNavigatedTo
            NavigationCacheMode = NavigationCacheMode.Required;
            AppealsList.ItemsSource = Rows;
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
                var request = new AppealPageRequest
                {
                    SearchText = SearchBox.Text ?? "",
                    Page = _page,
                    PageSize = PageSize,
                };
                var result = await Task.Run(
                    () => AppealRepository.GetPage(request), token);
                token.ThrowIfCancellationRequested();

                _page = result.Page;
                _totalCount = result.TotalCount;
                Rows.Clear();
                foreach (var appeal in result.Items)
                    Rows.Add(CreateRow(appeal));
                UpdatePagination();
            }, SetLoading, ex =>
            {
                ErrorBar.Message = $"Не удалось загрузить обращения. {ex.Message}";
                ErrorBar.IsOpen = true;
            }, debounce ? 300 : 0);
        }

        private static AppealRow CreateRow(Appeal appeal) => new()
        {
            Id = appeal.Id,
            Number = appeal.Number.ToString(),
            CreatedAt = appeal.CreatedAt,
            Sender = FormatInitials(appeal.Sender),
            Complaints = appeal.Complaints,
        };

        /// <summary>"Иванов Пётр Сергеевич" → "Иванов П. С."</summary>
        private static string FormatInitials(string fullName)
        {
            var parts = fullName.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "",
                1 => parts[0],
                2 => $"{parts[0]} {parts[1][0]}.",
                _ => $"{parts[0]} {parts[1][0]}. {parts[2][0]}.",
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
            Frame.Navigate(typeof(AppealFormPage), 0L);

        private void IcdButton_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(IcdReferencePage));

        private void AppealsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (AppealsList.SelectedItem is AppealRow row)
                Frame.Navigate(typeof(AppealFormPage), row.Id);
        }

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
                Frame.Navigate(typeof(AppealFormPage), id);
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await RecordAction.RunAsync(async () =>
            {
                if (sender is not FrameworkElement { Tag: long id })
                    return;

                var row = Rows.FirstOrDefault(r => r.Id == id);
                if (await Dialogs.ConfirmAsync(this, "Перемещение в корзину",
                    $"Переместить обращение №{row?.Number} в корзину?", "В корзину"))
                {
                    AppealRepository.MoveToTrash(id);
                    await LoadDataAsync();
                }
            }, ex => Dialogs.MessageAsync(this, "Не удалось переместить обращение в корзину", ex.Message));
        }
    }
}
