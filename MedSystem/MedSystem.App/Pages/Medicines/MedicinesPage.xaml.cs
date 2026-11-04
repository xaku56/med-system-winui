using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.App.UI;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    public class MedicineRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string Dosage { get; set; } = "";
        public string ExpirationDate { get; set; } = "";
        public Brush QtyBg { get; set; } = Badges.TransparentBg;
        public Brush QtyFg { get; set; } = Badges.NormalFg;
        public Brush DateBg { get; set; } = Badges.TransparentBg;
        public Brush DateFg { get; set; } = Badges.NormalFg;
    }

    public sealed partial class MedicinesPage : Page
    {
        private const int PageSize = Pagination.DefaultPageSize;

        private long _totalCount;
        private int _page = 1;
        private bool _isPageActive;
        private readonly LatestLoad _loader = new();
        public ObservableCollection<MedicineRow> Rows { get; } = new();

        public MedicinesPage()
        {
            InitializeComponent();
            // Страница кэшируется: поиск и фильтр сохраняются между переходами,
            // данные всё равно перезагружаются в OnNavigatedTo
            NavigationCacheMode = NavigationCacheMode.Required;
            MedicinesList.ItemsSource = Rows;
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
                var request = new MedicinePageRequest
                {
                    SearchText = SearchBox.Text ?? "",
                    StatusFilter = FilterBox.SelectedIndex,
                    LowQuantityThreshold = MedicineRules.LowQuantityThreshold,
                    Page = _page,
                    PageSize = PageSize,
                };
                var result = await Task.Run(
                    () => MedicineRepository.GetPage(request), token);
                token.ThrowIfCancellationRequested();

                _page = result.Page;
                _totalCount = result.TotalCount;
                var dark = ActualTheme == ElementTheme.Dark;
                Rows.Clear();
                foreach (var medicine in result.Items)
                    Rows.Add(CreateRow(medicine, dark));
                UpdatePagination();
            }, SetLoading, ex =>
            {
                ErrorBar.Message = $"Не удалось загрузить лекарства. {ex.Message}";
                ErrorBar.IsOpen = true;
            }, debounce ? 300 : 0);
        }

        private static MedicineRow CreateRow(Medicine medicine, bool dark)
        {
            var (isExpired, isExpiring) = ExpirationRules.GetMedicineStatus(medicine.ExpirationDate);
            var isLow = medicine.Quantity <= MedicineRules.LowQuantityThreshold;
            var (dateBg, dateFg) = Badges.For(isExpired, isExpiring, dark);
            var (qtyBg, qtyFg) = Badges.For(isLow, false, dark);
            return new MedicineRow
            {
                Id = medicine.Id,
                Name = medicine.Name,
                Quantity = medicine.Quantity.ToString(),
                Dosage = medicine.Dosage,
                ExpirationDate = medicine.ExpirationDate,
                QtyBg = qtyBg,
                QtyFg = qtyFg,
                DateBg = dateBg,
                DateFg = dateFg,
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
            Frame.Navigate(typeof(MedicineFormPage), 0L);

        private async void OrderButton_Click(object sender, RoutedEventArgs e)
        {
            bool hasCandidates;
            try
            {
                SetLoading(true);
                hasCandidates = await Task.Run(
                    () => MedicineRepository.HasOrderCandidates(MedicineRules.LowQuantityThreshold));
            }
            catch (Exception ex)
            {
                ErrorBar.Message = $"Не удалось проверить список заказа. {ex.Message}";
                ErrorBar.IsOpen = true;
                return;
            }
            finally
            {
                SetLoading(false);
            }
            if (!hasCandidates)
            {
                await Dialogs.MessageAsync(this, "Заказ лекарств", "Все лекарства в норме. Заказывать ничего не нужно.");
                return;
            }
            Frame.Navigate(typeof(OrderMedicinesPage));
        }

        private void MedicinesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (MedicinesList.SelectedItem is MedicineRow row)
                Frame.Navigate(typeof(MedicineFormPage), row.Id);
        }

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
                Frame.Navigate(typeof(MedicineFormPage), id);
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await RecordAction.RunAsync(async () =>
            {
                if (sender is not FrameworkElement { Tag: long id })
                    return;

                var row = Rows.FirstOrDefault(r => r.Id == id);
                if (await Dialogs.ConfirmAsync(this, "Перемещение в корзину",
                    $"Переместить лекарство «{row?.Name}» в корзину?", "В корзину"))
                {
                    MedicineRepository.MoveToTrash(id);
                    await LoadDataAsync();
                }
            }, ex => Dialogs.MessageAsync(this, "Не удалось переместить лекарство в корзину", ex.Message));
        }
    }
}
