using System;
using System.Collections.ObjectModel;
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
    public class StudentAppealRow
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public string DateTime { get; set; } = "";
        public string Complaints { get; set; } = "";
        public string Diagnosis { get; set; } = "";
        public string Vitals { get; set; } = "";
        public string Ambulance { get; set; } = "";
    }

    /// <summary>Карточка студента: сведения и история обращений.
    /// Параметр навигации: long id студента.</summary>
    public sealed partial class StudentCardPage : Page
    {
        private long _studentId;
        private readonly LatestLoad _loader = new();
        public ObservableCollection<StudentAppealRow> Rows { get; } = new();

        public StudentCardPage()
        {
            InitializeComponent();
            AppealsList.ItemsSource = Rows;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _studentId = e.Parameter is long id ? id : 0;
            _ = LoadDataAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _loader.Cancel();
            base.OnNavigatedFrom(e);
        }

        private Task LoadDataAsync()
        {
            var studentId = _studentId;
            return _loader.RunAsync(async token =>
            {
                ErrorBar.IsOpen = false;
                var (student, appeals) = await Task.Run(
                    () => (StudentRepository.GetById(studentId), AppealRepository.GetByStudent(studentId)),
                    token);
                token.ThrowIfCancellationRequested();

                if (student == null)
                {
                    TitleText.Text = "Студент не найден";
                    SubtitleText.Text = "Запись могла быть перемещена в корзину.";
                    EditButton.IsEnabled = false;
                    NewAppealButton.IsEnabled = false;
                    Rows.Clear();
                    EmptyText.Visibility = Visibility.Collapsed;
                    AppealCountText.Text = "";
                    return;
                }

                FillStudent(student);
                Rows.Clear();
                foreach (var appeal in appeals)
                    Rows.Add(CreateRow(appeal));
                AppealCountText.Text = $"Всего: {appeals.Count}";
                EmptyText.Visibility = appeals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }, SetLoading, ex =>
            {
                ErrorBar.Message = $"Не удалось загрузить карточку студента. {ex.Message}";
                ErrorBar.IsOpen = true;
            });
        }

        private void FillStudent(Student s)
        {
            TitleText.Text = s.FullName;
            var group = string.IsNullOrWhiteSpace(s.GroupName) ? "Группа не указана" : $"Группа {s.GroupName}";
            SubtitleText.Text = s.IsArchived
                ? $"{group} · В архиве{(string.IsNullOrWhiteSpace(s.ArchiveReason) ? "" : $": {s.ArchiveReason}")}"
                : group;

            BirthDateText.Text = OrDash(s.BirthDate);
            OmsText.Text = OrDash(s.Oms);
            HealthGroupText.Text = OrDash(s.HealthGroup);
            DispensaryText.Text = s.DispensaryDisplay;
            AddressText.Text = OrDash(s.Address);

            var dark = ActualTheme == ElementTheme.Dark;
            SetBadge(MedicalExamBadge, MedicalExamText, s.MedicalExamDate, dark);
            SetBadge(FluorographyBadge, FluorographyText, s.FluorographyDate, dark);

            EditButton.IsEnabled = true;
            // Архивные студенты недоступны в выборе отправителя — новые обращения им не добавляются.
            NewAppealButton.IsEnabled = !s.IsArchived;
        }

        private static void SetBadge(Border badge, TextBlock text, string date, bool dark)
        {
            var status = ExpirationRules.GetSingleCheckupStatus(date);
            var (bg, fg) = Badges.For(status.IsExpired, status.IsExpiring, dark);
            badge.Background = bg;
            text.Foreground = fg;
            text.Text = OrDash(date);
        }

        private static string OrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value;

        private static StudentAppealRow CreateRow(Appeal appeal) => new()
        {
            Id = appeal.Id,
            Number = appeal.Number.ToString(),
            DateTime = appeal.DateTimeDisplay,
            Complaints = appeal.Complaints,
            Diagnosis = appeal.Diagnosis,
            Vitals = appeal.VitalsSummary,
            Ambulance = appeal.AmbulanceCalled ? "Да" : "",
        };

        private void SetLoading(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            if (isLoading)
                EmptyText.Visibility = Visibility.Collapsed;
        }

        // ── Действия ─────────────────────────────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
            else
                Frame.Navigate(typeof(StudentsPage));
        }

        private void EditButton_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(StudentFormPage), _studentId);

        private void NewAppealButton_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(AppealFormPage), new NewAppealForStudent { StudentId = _studentId });

        private void AppealsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (AppealsList.SelectedItem is StudentAppealRow row)
                Frame.Navigate(typeof(AppealFormPage), row.Id);
        }

        private void OpenAppealMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
                Frame.Navigate(typeof(AppealFormPage), id);
        }
    }
}
