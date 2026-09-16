using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Core;
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
        public bool IsExpired { get; set; }
        public bool IsExpiring { get; set; }
        public bool IsLowQuantity { get; set; }
        public Brush QtyBg { get; set; } = Badges.TransparentBg;
        public Brush QtyFg { get; set; } = Badges.NormalFg;
        public Brush DateBg { get; set; } = Badges.TransparentBg;
        public Brush DateFg { get; set; } = Badges.NormalFg;
    }

    public sealed partial class MedicinesPage : Page
    {
        /// <summary>Порог «мало лекарства» (штук).</summary>
        public const int LowQuantityThreshold = 5;

        private List<MedicineRow> _allRows = new();
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
            LoadData();
        }

        private void LoadData()
        {
            var dark = ActualTheme == ElementTheme.Dark;
            _allRows = MedicineRepository.GetAll().Select(m =>
            {
                var (isExpired, isExpiring) = ExpirationRules.GetMedicineStatus(m.ExpirationDate);
                var isLow = m.Quantity <= LowQuantityThreshold;
                var (dateBg, dateFg) = Badges.For(isExpired, isExpiring, dark);
                var (qtyBg, qtyFg) = Badges.For(isLow, false, dark);
                return new MedicineRow
                {
                    Id = m.Id,
                    Name = m.Name,
                    Quantity = m.Quantity.ToString(),
                    Dosage = m.Dosage,
                    ExpirationDate = m.ExpirationDate,
                    IsExpired = isExpired,
                    IsExpiring = isExpiring,
                    IsLowQuantity = isLow,
                    QtyBg = qtyBg,
                    QtyFg = qtyFg,
                    DateBg = dateBg,
                    DateFg = dateFg,
                };
            }).ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (SearchBox == null || FilterBox == null)
                return;

            var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            IEnumerable<MedicineRow> filtered = _allRows;

            if (!string.IsNullOrEmpty(query))
                filtered = filtered.Where(r => r.Name.ToLowerInvariant().Contains(query));

            filtered = FilterBox.SelectedIndex switch
            {
                1 => filtered.Where(r => r.IsLowQuantity),  // Мало (<= 5)
                2 => filtered.Where(r => r.IsExpiring),     // Истекают (2 недели)
                3 => filtered.Where(r => r.IsExpired),      // Просроченные
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

        // ── Действия ─────────────────────────────────────────────────

        private void AddButton_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(MedicineFormPage), 0L);

        private async void OrderButton_Click(object sender, RoutedEventArgs e)
        {
            var hasCandidates = _allRows.Any(r => r.IsLowQuantity || r.IsExpired || r.IsExpiring);
            if (!hasCandidates)
            {
                var dialog = new ContentDialog
                {
                    Title = "Заказ лекарств",
                    Content = "Все лекарства в норме. Заказывать ничего не нужно.",
                    CloseButtonText = "Понятно",
                    XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                };
                await dialog.ShowAsync();
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
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var row = _allRows.FirstOrDefault(r => r.Id == id);
            var dialog = new ContentDialog
            {
                Title = "Перемещение в корзину",
                Content = $"Переместить лекарство «{row?.Name}» в корзину?",
                PrimaryButtonText = "В корзину",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                MedicineRepository.MoveToTrash(id);
                LoadData();
            }
        }
    }
}
