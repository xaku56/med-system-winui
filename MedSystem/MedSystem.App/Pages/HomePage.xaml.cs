using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    public sealed partial class HomePage : Page
    {
        private CancellationTokenSource? _loadCancellation;

        public HomePage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = LoadDashboardAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _loadCancellation?.Cancel();
            base.OnNavigatedFrom(e);
        }

        private async Task LoadDashboardAsync()
        {
            _loadCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;
            DashboardProgress.IsActive = true;
            DashboardProgress.Visibility = Visibility.Visible;
            ErrorBar.IsOpen = false;

            try
            {
                var snapshot = await Task.Run(
                    () => DashboardRepository.GetSnapshot(MedicinesPage.LowQuantityThreshold),
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                EmployeesCount.Text = snapshot.Employees.ToString();
                StudentsCount.Text = snapshot.Students.ToString();
                MedicinesCount.Text = snapshot.Medicines.ToString();
                AppealsCount.Text = snapshot.Appeals.ToString();
                EmployeeAttentionText.Text =
                    $"Просрочено: {snapshot.ExpiredEmployees} · истекает: {snapshot.ExpiringEmployees}";
                StudentAttentionText.Text =
                    $"Просрочено: {snapshot.ExpiredStudents} · истекает: {snapshot.ExpiringStudents}";
                MedicineAttentionText.Text =
                    $"Мало: {snapshot.LowMedicines} · просрочено: {snapshot.ExpiredMedicines} · истекает: {snapshot.ExpiringMedicines}";
                TrashAttentionText.Text = $"Записей: {snapshot.Trash}";
                BackupStatusText.Text = snapshot.LatestBackupAt.HasValue
                    ? $"Последняя резервная копия: {snapshot.LatestBackupAt:dd.MM.yyyy HH:mm}"
                    : "Резервных копий пока нет";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ErrorBar.Message = ex.Message;
                ErrorBar.IsOpen = true;
            }
            finally
            {
                if (ReferenceEquals(_loadCancellation, cancellation))
                {
                    _loadCancellation = null;
                    DashboardProgress.IsActive = false;
                    DashboardProgress.Visibility = Visibility.Collapsed;
                }
                cancellation.Dispose();
            }
        }

        // ── Быстрые действия ─────────────────────────────────────────

        private void AddEmployee_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(EmployeeFormPage), 0L);

        private async void AddStudent_Click(object sender, RoutedEventArgs e)
        {
            if (GroupRepository.GetAll().Count == 0)
            {
                var dialog = new ContentDialog
                {
                    Title = "Нет групп",
                    Content = "Сначала добавьте хотя бы одну учебную группу (раздел «Студенты» → «Группы»).",
                    CloseButtonText = "Понятно",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ActualTheme,
                };
                await dialog.ShowAsync();
                return;
            }
            Frame.Navigate(typeof(StudentFormPage), 0L);
        }

        private void AddMedicine_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(MedicineFormPage), 0L);

        private void AddAppeal_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(AppealFormPage), 0L);

        private void OpenEmployees_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(EmployeesPage));

        private void OpenStudents_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(StudentsPage));

        private void OpenMedicines_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(MedicinesPage));

        private void OpenAppeals_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(AppealsPage));

        private void OpenTrash_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(TrashPage));
    }
}
