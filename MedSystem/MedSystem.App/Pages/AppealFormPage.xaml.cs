using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    /// <summary>Форма добавления/редактирования обращения.
    /// Параметр навигации: long id; 0 — новое.</summary>
    public sealed partial class AppealFormPage : Page
    {
        private long _appealId;
        private CancellationTokenSource? _personSearchCancellation;
        private CancellationTokenSource? _diagnosisSearchCancellation;

        public AppealFormPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _appealId = e.Parameter is long id ? id : 0;

            if (_appealId > 0)
            {
                TitleText.Text = "Редактирование обращения";
                var a = AppealRepository.GetById(_appealId);
                if (a != null)
                    FillForm(a);
            }
            else
            {
                NumberBox.Value = AppealRepository.GetNextNumber();
                CreatedAtBox.Text = DateTime.Now.ToString(ExpirationRules.DateFormat);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _personSearchCancellation?.Cancel();
            _diagnosisSearchCancellation?.Cancel();
            base.OnNavigatedFrom(e);
        }

        private void FillForm(Appeal a)
        {
            NumberBox.Value = a.Number;
            CreatedAtBox.Text = a.CreatedAt;
            SenderBox.Text = a.Sender;
            BirthDateBox.Text = a.BirthDate;
            ParentPhoneBox.Text = a.ParentPhone;
            GroupNameBox.Text = a.GroupName;
            ComplaintsBox.Text = a.Complaints;
            DiagnosisBox.Text = a.Diagnosis;
            ActionsBox.Text = a.ActionsRecommendations;
        }

        private Appeal CollectForm() => new()
        {
            Id = _appealId,
            Number = double.IsNaN(NumberBox.Value) ? 0 : (long)NumberBox.Value,
            CreatedAt = CreatedAtBox.Text.Trim(),
            Sender = StripPersonSuffix(SenderBox.Text.Trim()),
            BirthDate = BirthDateBox.Text.Trim(),
            ParentPhone = ParentPhoneBox.Text.Trim(),
            GroupName = GroupNameBox.Text.Trim(),
            Complaints = ComplaintsBox.Text.Trim(),
            Diagnosis = DiagnosisBox.Text.Trim(),
            ActionsRecommendations = ActionsBox.Text.Trim(),
        };

        /// <summary>«Иванов Пётр (Группа 11-ИС-А)» → «Иванов Пётр».</summary>
        private static string StripPersonSuffix(string value)
        {
            var idx = value.IndexOf(" (", StringComparison.Ordinal);
            return idx > 0 ? value[..idx] : value;
        }

        // ── Автодополнение отправителя ───────────────────────────────

        private async void SenderBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            _personSearchCancellation?.Cancel();
            var query = sender.Text.Trim();
            if (query.Length == 0)
            {
                sender.ItemsSource = null;
                SenderSearchRing.IsActive = false;
                SenderSearchRing.Visibility = Visibility.Collapsed;
                return;
            }

            var cancellation = new CancellationTokenSource();
            _personSearchCancellation = cancellation;
            try
            {
                await Task.Delay(250, cancellation.Token);
                SenderSearchRing.IsActive = true;
                SenderSearchRing.Visibility = Visibility.Visible;
                var persons = await Task.Run(
                    () => AppealRepository.SearchPersons(query), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                sender.ItemsSource = persons;
            }
            catch (OperationCanceledException)
            {
                // Продолжаем только с последней введённой строкой.
            }
            catch
            {
                sender.ItemsSource = null;
            }
            finally
            {
                if (ReferenceEquals(_personSearchCancellation, cancellation))
                {
                    _personSearchCancellation = null;
                    SenderSearchRing.IsActive = false;
                    SenderSearchRing.Visibility = Visibility.Collapsed;
                }
                cancellation.Dispose();
            }
        }

        private void SenderBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is PersonOption person)
            {
                BirthDateBox.Text = person.BirthDate;
                GroupNameBox.Text = person.GroupName;
            }
        }

        // ── Автодополнение диагноза по МКБ ───────────────────────────

        private async void DiagnosisBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            _diagnosisSearchCancellation?.Cancel();
            var query = sender.Text.Trim();
            if (query.Length == 0)
            {
                sender.ItemsSource = null;
                DiagnosisSearchRing.IsActive = false;
                DiagnosisSearchRing.Visibility = Visibility.Collapsed;
                return;
            }

            var cancellation = new CancellationTokenSource();
            _diagnosisSearchCancellation = cancellation;
            try
            {
                await Task.Delay(250, cancellation.Token);
                DiagnosisSearchRing.IsActive = true;
                DiagnosisSearchRing.Visibility = Visibility.Visible;
                var codes = await Task.Run(
                    () => IcdRepository.Search(query), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                sender.ItemsSource = codes.Select(c => $"{c.Code} - {c.Name}").ToList();
            }
            catch (OperationCanceledException)
            {
                // Продолжаем только с последней введённой строкой.
            }
            catch
            {
                sender.ItemsSource = null;
            }
            finally
            {
                if (ReferenceEquals(_diagnosisSearchCancellation, cancellation))
                {
                    _diagnosisSearchCancellation = null;
                    DiagnosisSearchRing.IsActive = false;
                    DiagnosisSearchRing.Visibility = Visibility.Collapsed;
                }
                cancellation.Dispose();
            }
        }

        // ── Сохранение ───────────────────────────────────────────────

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var appeal = CollectForm();

            var errors = Validators.ValidateAppeal(appeal);
            if (errors.Count > 0)
            {
                await ShowErrorsAsync(errors);
                return;
            }

            try
            {
                if (_appealId > 0)
                    AppealRepository.Update(appeal);
                else
                    AppealRepository.Insert(appeal);
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
