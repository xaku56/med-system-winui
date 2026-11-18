using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.App.UI;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    /// <summary>Параметр навигации: новое обращение из карточки студента.</summary>
    public sealed class NewAppealForStudent
    {
        public long StudentId { get; init; }
    }

    /// <summary>Форма добавления/редактирования обращения.
    /// Параметр навигации: long id (0 — новое) или <see cref="NewAppealForStudent"/>.</summary>
    public sealed partial class AppealFormPage : Page
    {
        private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        private long _appealId;
        /// <summary>Студент, к карточке которого относится обращение.</summary>
        private long? _studentId;
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
                CreatedTimeBox.PlaceholderText = "при сохранении";

                if (e.Parameter is NewAppealForStudent { StudentId: > 0 } request
                    && StudentRepository.GetById(request.StudentId) is { } student)
                {
                    SenderBox.Text = student.FullName;
                    BirthDateBox.Text = student.BirthDate;
                    GroupNameBox.Text = student.GroupName;
                    _studentId = student.Id;
                }
            }
            UpdateStudentLinkText();
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
            CreatedTimeBox.Text = a.CreatedTime;
            SenderBox.Text = a.Sender;
            _studentId = a.StudentId;
            BirthDateBox.Text = a.BirthDate;
            ParentPhoneBox.Text = a.ParentPhone;
            GroupNameBox.Text = a.GroupName;
            ComplaintsBox.Text = a.Complaints;
            HeightBox.Text = a.HeightCm?.ToString(CultureInfo.InvariantCulture) ?? "";
            WeightBox.Text = a.WeightKg?.ToString("0.#", Russian) ?? "";
            TemperatureBox.Text = a.Temperature?.ToString("0.0", Russian) ?? "";
            BloodPressureSystolicBox.Text = a.BloodPressureSystolic?.ToString(CultureInfo.InvariantCulture) ?? "";
            BloodPressureDiastolicBox.Text = a.BloodPressureDiastolic?.ToString(CultureInfo.InvariantCulture) ?? "";
            PulseBox.Text = a.Pulse?.ToString(CultureInfo.InvariantCulture) ?? "";
            SaturationBox.Text = a.Saturation?.ToString(CultureInfo.InvariantCulture) ?? "";
            DiagnosisBox.Text = a.Diagnosis;
            ActionsBox.Text = a.ActionsRecommendations;
            AmbulanceCheckBox.IsChecked = a.AmbulanceCalled;
        }

        /// <summary>Собирает форму; ошибки разбора чисел добавляются в <paramref name="errors"/>.</summary>
        private Appeal CollectForm(List<string> errors)
        {
            var appeal = new Appeal
            {
                Id = _appealId,
                Number = double.IsNaN(NumberBox.Value) ? 0 : (long)NumberBox.Value,
                CreatedAt = CreatedAtBox.Text.Trim(),
                CreatedTime = CreatedTimeBox.Text.Trim(),
                StudentId = _studentId,
                Sender = StripPersonSuffix(SenderBox.Text.Trim()),
                BirthDate = BirthDateBox.Text.Trim(),
                ParentPhone = ParentPhoneBox.Text.Trim(),
                GroupName = GroupNameBox.Text.Trim(),
                Complaints = ComplaintsBox.Text.Trim(),
                HeightCm = ReadInteger(HeightBox, "Рост, см", errors),
                WeightKg = ReadNumber(WeightBox, "Вес, кг", errors),
                Temperature = ReadNumber(TemperatureBox, "Температура, °C", errors),
                BloodPressureSystolic = ReadInteger(BloodPressureSystolicBox, "Давление: верхнее", errors),
                BloodPressureDiastolic = ReadInteger(BloodPressureDiastolicBox, "Давление: нижнее", errors),
                Pulse = ReadInteger(PulseBox, "Пульс, уд/мин", errors),
                Saturation = ReadInteger(SaturationBox, "Сатурация, %", errors),
                Diagnosis = DiagnosisBox.Text.Trim(),
                ActionsRecommendations = ActionsBox.Text.Trim(),
                AmbulanceCalled = AmbulanceCheckBox.IsChecked == true,
            };

            // Новое обращение без указанного вручную времени получает время сохранения.
            // У старых записей пустое время не подменяется временем редактирования.
            if (_appealId == 0 && appeal.CreatedTime.Length == 0)
                appeal.CreatedTime = DateTime.Now.ToString(Validators.TimeFormat, CultureInfo.InvariantCulture);

            return appeal;
        }

        private static int? ReadInteger(TextBox box, string label, List<string> errors)
        {
            if (Validators.TryParseOptionalInteger(box.Text, out var value))
                return value;
            errors.Add($"Поле '{label}' должно быть целым числом.");
            return null;
        }

        private static double? ReadNumber(TextBox box, string label, List<string> errors)
        {
            if (Validators.TryParseOptionalNumber(box.Text, out var value))
                return value;
            errors.Add($"Поле '{label}' должно быть числом.");
            return null;
        }

        /// <summary>«Иванов Пётр (Группа 11-ИС-А)» → «Иванов Пётр».</summary>
        private static string StripPersonSuffix(string value)
        {
            var idx = value.IndexOf(" (", StringComparison.Ordinal);
            return idx > 0 ? value[..idx] : value;
        }

        private void UpdateStudentLinkText()
        {
            StudentLinkText.Text = _studentId is > 0
                ? "✓ Обращение будет сохранено в карточке студента."
                : string.IsNullOrWhiteSpace(SenderBox.Text)
                    ? "Выберите студента из подсказок, чтобы обращение попало в его карточку."
                    : "Не привязано к карточке студента. Чтобы привязать, выберите студента из подсказок.";
        }

        // ── Автодополнение отправителя ───────────────────────────────

        private async void SenderBox_TextChanged(
            AutoSuggestBox sender,
            AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            // ФИО изменили вручную — прежняя привязка к студенту больше не достоверна.
            _studentId = null;
            UpdateStudentLinkText();

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
                _studentId = person.StudentId > 0 ? person.StudentId : null;
                UpdateStudentLinkText();
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
            if (double.IsNaN(NumberBox.Value) || NumberBox.Value != Math.Truncate(NumberBox.Value))
            {
                await Dialogs.ErrorsAsync(this, new[] { "Номер обращения должен быть целым числом." });
                return;
            }

            var errors = new List<string>();
            var appeal = CollectForm(errors);
            errors.AddRange(Validators.ValidateAppeal(appeal));
            if (errors.Count > 0)
            {
                await Dialogs.ErrorsAsync(this, errors.Distinct());
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
                await Dialogs.ErrorsAsync(this, new[] { $"Ошибка базы данных: {ex.Message}" });
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
    }
}
