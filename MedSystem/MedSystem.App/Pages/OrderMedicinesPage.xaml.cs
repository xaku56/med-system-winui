using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    /// <summary>Позиция заказа: старая партия + данные новой.</summary>
    public class OrderRow
    {
        public long Id { get; set; }
        public string RealName { get; set; } = "";
        public string Dosage { get; set; } = "";
        public string Title { get; set; } = "";
        public string CurrentInfo { get; set; } = "";
        public bool Selected { get; set; } = true;
        public double NewQuantity { get; set; } = 50;
        public string NewExpiration { get; set; } = "";
    }

    /// <summary>Заказ лекарств: списание старых партий и добавление новых
    /// одной транзакцией (перенос order_medicines_dialog из python).</summary>
    public sealed partial class OrderMedicinesPage : Page
    {
        private List<OrderRow> _rows = new();

        public OrderMedicinesPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _rows = MedicineRepository.GetOrderCandidates(MedicinesPage.LowQuantityThreshold)
                .Select(m => new OrderRow
                {
                    Id = m.Id,
                    RealName = m.Name,
                    Dosage = m.Dosage,
                    Title = $"{m.Name}, {m.Dosage}",
                    CurrentInfo = $"Осталось: {m.Quantity} · Годен до: {m.ExpirationDate}",
                })
                .ToList();

            OrderList.ItemsSource = _rows;
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _rows.Where(r => r.Selected).ToList();
            if (selected.Count == 0)
            {
                await ShowMessageAsync("Заказ пуст", "Отметьте хотя бы одну позицию.");
                return;
            }

            var errors = new List<string>();
            foreach (var row in selected)
            {
                if (double.IsNaN(row.NewQuantity) || row.NewQuantity < 1)
                    errors.Add($"«{row.RealName}»: укажите новое количество.");
                else if (row.NewQuantity != Math.Truncate(row.NewQuantity))
                    errors.Add($"«{row.RealName}»: количество должно быть целым числом.");
                if (!Validators.IsValidDate(row.NewExpiration.Trim()))
                    errors.Add($"«{row.RealName}»: новый срок годности должен быть в формате ДД.ММ.ГГГГ.");
            }
            if (errors.Count > 0)
            {
                await ShowMessageAsync("Проверьте данные", string.Join("\n", errors));
                return;
            }

            try
            {
                // Одна транзакция: при ошибке база останется нетронутой
                MedicineRepository.Reorder(selected.Select(r => (r.Id, new Medicine
                {
                    Name = r.RealName,
                    Dosage = r.Dosage,
                    Quantity = (long)r.NewQuantity,
                    ExpirationDate = r.NewExpiration.Trim(),
                })));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Ошибка базы данных", ex.Message);
                return;
            }

            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
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
