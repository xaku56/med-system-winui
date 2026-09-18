using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    public sealed class TrashRow
    {
        public string Key { get; init; } = "";
        public string TypeName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string DeletedAt { get; init; } = "";
        public string Retention { get; init; } = "";
    }

    public sealed partial class TrashPage : Page
    {
        private const int PageSize = 100;
        private readonly ObservableCollection<TrashRow> _rows = new();
        private CancellationTokenSource? _loadCancellation;
        private int _page = 1;
        private long _totalCount;

        public TrashPage()
        {
            InitializeComponent();
            TrashList.ItemsSource = _rows;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = LoadAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _loadCancellation?.Cancel();
            base.OnNavigatedFrom(e);
        }

        private async Task LoadAsync()
        {
            _loadCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;
            var token = cancellation.Token;
            SetLoading(true);
            ErrorBar.IsOpen = false;

            try
            {
                var result = await Task.Run(() => TrashRepository.GetPage(_page, PageSize), token);
                token.ThrowIfCancellationRequested();

                _totalCount = result.TotalCount;
                var totalPages = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));
                if (_page > totalPages)
                {
                    _page = totalPages;
                    await LoadAsync();
                    return;
                }

                var retentionDays = TrashRepository.GetRetentionDays();
                _rows.Clear();
                foreach (var item in result.Items)
                {
                    var remainingDays = retentionDays == 0
                        ? "Бессрочно"
                        : $"{Math.Max(0, (int)Math.Ceiling((item.DeletedAtUtc.AddDays(retentionDays) - DateTime.UtcNow).TotalDays))} дн.";
                    _rows.Add(new TrashRow
                    {
                        Key = item.Key,
                        TypeName = item.TypeName,
                        DisplayName = item.DisplayName,
                        DeletedAt = item.DeletedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                        Retention = remainingDays,
                    });
                }

                CountText.Text = $"Всего: {_totalCount}";
                PageText.Text = $"Страница {_page} из {totalPages}";
                PreviousButton.IsEnabled = _page > 1;
                NextButton.IsEnabled = _page < totalPages;
                EmptyTrashButton.IsEnabled = _totalCount > 0;
                EmptyText.Visibility = _totalCount == 0 ? Visibility.Visible : Visibility.Collapsed;
                RetentionText.Text = retentionDays == 0
                    ? "Автоматическая очистка отключена"
                    : $"Записи автоматически удаляются через {retentionDays} дней";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ErrorBar.Title = "Не удалось загрузить корзину";
                ErrorBar.Message = ex.Message;
                ErrorBar.IsOpen = true;
            }
            finally
            {
                if (ReferenceEquals(_loadCancellation, cancellation))
                {
                    _loadCancellation = null;
                    SetLoading(false);
                }
                cancellation.Dispose();
            }
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string key })
                return;

            try
            {
                var (entityType, id) = TrashRepository.ParseKey(key);
                await Task.Run(() => TrashRepository.Restore(entityType, id));
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowError("Не удалось восстановить запись", ex.Message);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string key })
                return;

            var dialog = new ContentDialog
            {
                Title = "Удалить навсегда?",
                Content = "Эту запись нельзя будет восстановить из корзины.",
                PrimaryButtonText = "Удалить навсегда",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                var (entityType, id) = TrashRepository.ParseKey(key);
                await Task.Run(() => TrashRepository.DeletePermanently(entityType, id));
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowError("Не удалось удалить запись", ex.Message);
            }
        }

        private async void EmptyTrashButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Очистить корзину?",
                Content = $"Будут окончательно удалены все записи: {_totalCount}. Это действие нельзя отменить.",
                PrimaryButtonText = "Очистить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                await Task.Run(TrashRepository.EmptyTrash);
                _page = 1;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowError("Не удалось очистить корзину", ex.Message);
            }
        }

        private async void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_page <= 1)
                return;
            _page--;
            await LoadAsync();
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_page * PageSize >= _totalCount)
                return;
            _page++;
            await LoadAsync();
        }

        private void SetLoading(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            PreviousButton.IsEnabled = !isLoading && _page > 1;
            NextButton.IsEnabled = !isLoading && _page * PageSize < _totalCount;
        }

        private void ShowError(string title, string message)
        {
            ErrorBar.Title = title;
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }
    }
}
