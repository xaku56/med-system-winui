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
    /// <summary>Справочник кодов МКБ: просмотр, добавление, изменение, удаление.</summary>
    public sealed partial class IcdReferencePage : Page
    {
        private const int PageSize = Pagination.DefaultPageSize;

        private long _totalCount;
        private int _page = 1;
        private bool _isPageActive;
        private readonly LatestLoad _loader = new();
        public ObservableCollection<IcdCode> Rows { get; } = new();

        public IcdReferencePage()
        {
            InitializeComponent();
            // Страница кэшируется: поиск и фильтр сохраняются между переходами,
            // данные всё равно перезагружаются в OnNavigatedTo
            NavigationCacheMode = NavigationCacheMode.Required;
            IcdList.ItemsSource = Rows;
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
                var searchText = SearchBox.Text ?? "";
                var requestedPage = _page;
                var result = await Task.Run(
                    () => IcdRepository.GetPage(searchText, requestedPage, PageSize),
                    token);
                token.ThrowIfCancellationRequested();

                _page = result.Page;
                _totalCount = result.TotalCount;
                Rows.Clear();
                foreach (var code in result.Items)
                    Rows.Add(code);
                UpdatePagination();
            }, SetLoading, ex =>
            {
                ErrorBar.Message = $"Не удалось загрузить справочник МКБ. {ex.Message}";
                ErrorBar.IsOpen = true;
            }, debounce ? 300 : 0);
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

        // ── Добавление / изменение ───────────────────────────────────

        private async void AddButton_Click(object sender, RoutedEventArgs e) =>
            await ShowEditDialogAsync(null);

        private void IcdList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (IcdList.SelectedItem is IcdCode row)
                _ = ShowEditDialogAsync(row);
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string code })
            {
                var row = Rows.FirstOrDefault(r => r.Code == code);
                if (row != null)
                    _ = ShowEditDialogAsync(row);
            }
        }

        private async Task ShowEditDialogAsync(IcdCode? existing)
        {
            var codeBox = new TextBox
            {
                Header = "Код",
                PlaceholderText = "Например: J06.9",
                Text = existing?.Code ?? "",
                MaxLength = 10,
            };
            var nameBox = new TextBox
            {
                Header = "Наименование диагноза",
                Text = existing?.Name ?? "",
                MaxLength = 255,
            };
            var panel = new StackPanel { MinWidth = 400, Spacing = 12 };
            panel.Children.Add(codeBox);
            panel.Children.Add(nameBox);

            var dialog = Dialogs.Create(this, existing == null ? "Новый код МКБ" : "Изменение кода МКБ", panel);
            dialog.PrimaryButtonText = "Сохранить";
            dialog.CloseButtonText = "Отмена";
            dialog.DefaultButton = ContentDialogButton.Primary;

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var code = codeBox.Text.Trim();
            var name = nameBox.Text.Trim();
            if (code.Length == 0 || name.Length == 0)
            {
                await Dialogs.MessageAsync(this, "Ошибка", "Код и наименование не могут быть пустыми.");
                return;
            }

            bool ok;
            try
            {
                ok = existing == null
                    ? IcdRepository.Insert(code, name)
                    : IcdRepository.Update(existing.Code, code, name);
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, "Не удалось сохранить код МКБ", ex.Message);
                return;
            }

            if (!ok)
            {
                await Dialogs.MessageAsync(this, "Ошибка", $"Код «{code}» уже существует в справочнике.");
                return;
            }

            await LoadDataAsync();
        }

        // ── Удаление ─────────────────────────────────────────────────

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string code })
                return;

            if (await Dialogs.ConfirmAsync(this, "Удаление",
                $"Удалить код «{code}» из справочника?", "Удалить"))
            {
                try
                {
                    IcdRepository.Delete(code);
                    await LoadDataAsync();
                }
                catch (Exception ex)
                {
                    await Dialogs.MessageAsync(this, "Не удалось удалить код МКБ", ex.Message);
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }
    }
}
