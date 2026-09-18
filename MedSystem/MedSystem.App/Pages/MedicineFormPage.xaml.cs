using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    /// <summary>Форма добавления/редактирования лекарства.
    /// Параметр навигации: long id; 0 — новое.</summary>
    public sealed partial class MedicineFormPage : Page
    {
        private long _medicineId;

        public MedicineFormPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _medicineId = e.Parameter is long id ? id : 0;

            if (_medicineId > 0)
            {
                TitleText.Text = "Редактирование лекарства";
                var m = MedicineRepository.GetById(_medicineId);
                if (m != null)
                {
                    NameBox.Text = m.Name;
                    DosageBox.Text = m.Dosage;
                    QuantityBox.Value = m.Quantity;
                    ExpirationBox.Text = m.ExpirationDate;
                }
            }
        }

        private Medicine CollectForm() => new()
        {
            Id = _medicineId,
            Name = NameBox.Text.Trim(),
            Dosage = DosageBox.Text.Trim(),
            Quantity = double.IsNaN(QuantityBox.Value) ? 0 : (long)QuantityBox.Value,
            ExpirationDate = ExpirationBox.Text.Trim(),
        };

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.IsNaN(QuantityBox.Value) || QuantityBox.Value != Math.Truncate(QuantityBox.Value))
            {
                await ShowErrorsAsync(new() { "Количество должно быть целым числом." });
                return;
            }

            var medicine = CollectForm();

            var errors = Validators.ValidateMedicine(medicine);
            if (errors.Count > 0)
            {
                await ShowErrorsAsync(errors);
                return;
            }

            try
            {
                if (_medicineId > 0)
                    MedicineRepository.Update(medicine);
                else
                    MedicineRepository.Insert(medicine);
            }
            catch (Exception ex)
            {
                await ShowErrorsAsync(new() { $"Ошибка базы данных: {ex.Message}" });
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

        private async Task ShowErrorsAsync(List<string> errors)
        {
            var dialog = new ContentDialog
            {
                Title = "Проверьте данные",
                Content = string.Join("\n", errors),
                CloseButtonText = "Понятно",
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            await dialog.ShowAsync();
        }
    }
}
