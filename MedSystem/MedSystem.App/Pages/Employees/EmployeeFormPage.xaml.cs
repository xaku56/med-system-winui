using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.App.UI;
using MedSystem.Core.Models;
using MedSystem.Core.Validation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    /// <summary>Форма добавления/редактирования сотрудника.
    /// Параметр навигации: long id сотрудника; 0 — новый.</summary>
    public sealed partial class EmployeeFormPage : Page
    {
        private long _employeeId;

        public EmployeeFormPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _employeeId = e.Parameter is long id ? id : 0;

            if (_employeeId > 0)
            {
                TitleText.Text = "Редактирование сотрудника";
                var emp = EmployeeRepository.GetById(_employeeId);
                if (emp != null)
                    FillForm(emp);
            }
        }

        private void FillForm(Employee e)
        {
            LastNameBox.Text = e.LastName;
            FirstNameBox.Text = e.FirstName;
            MiddleNameBox.Text = e.MiddleName;
            BirthDateBox.Text = e.BirthDate;
            AffiliationBox.SelectedIndex = e.Affiliation switch
            {
                "основной" => 1,
                "внешний" => 2,
                _ => 0,
            };
            OmsBox.Text = e.Oms;
            AddressBox.Text = e.Address;
            PassportSeriesBox.Text = e.PassportSeries;
            PassportNumberBox.Text = e.PassportNumber;
            PassportIssueDateBox.Text = e.PassportIssueDate;
            PassportDeptCodeBox.Text = e.PassportDepartmentCode;
            PassportIssuedByBox.Text = e.PassportIssuedBy;
            SanminimumBox.Text = e.SanminimumDate;
            MedicalExamBox.Text = e.MedicalExamDate;
            FluorographyBox.Text = e.FluorographyDate;
        }

        private Employee CollectForm() => new()
        {
            Id = _employeeId,
            LastName = LastNameBox.Text.Trim(),
            FirstName = FirstNameBox.Text.Trim(),
            MiddleName = MiddleNameBox.Text.Trim(),
            BirthDate = BirthDateBox.Text.Trim(),
            Affiliation = AffiliationBox.SelectedIndex switch
            {
                1 => "основной",
                2 => "внешний",
                _ => "",
            },
            Oms = OmsBox.Text.Trim(),
            Address = AddressBox.Text.Trim(),
            PassportSeries = PassportSeriesBox.Text.Trim(),
            PassportNumber = PassportNumberBox.Text.Trim(),
            PassportIssueDate = PassportIssueDateBox.Text.Trim(),
            PassportDepartmentCode = PassportDeptCodeBox.Text.Trim(),
            PassportIssuedBy = PassportIssuedByBox.Text.Trim(),
            SanminimumDate = SanminimumBox.Text.Trim(),
            MedicalExamDate = MedicalExamBox.Text.Trim(),
            FluorographyDate = FluorographyBox.Text.Trim(),
        };

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var employee = CollectForm();

            var errors = Validators.ValidateEmployee(employee);
            if (errors.Count > 0)
            {
                await Dialogs.ErrorsAsync(this, errors);
                return;
            }

            SaveButton.IsEnabled = false;
            try
            {
                var duplicates = await Task.Run(() => EmployeeRepository.FindDuplicates(employee));
                if (duplicates.Count > 0 && !await Dialogs.ConfirmDuplicatesAsync(this, duplicates, "Сохранить сотрудника всё равно?"))
                    return;

                await Task.Run(() =>
                {
                    if (_employeeId > 0)
                        EmployeeRepository.Update(employee);
                    else
                        EmployeeRepository.Insert(employee);
                });
            }
            catch (Exception ex)
            {
                await Dialogs.ErrorsAsync(this, new[] { $"Ошибка базы данных: {ex.Message}" });
                return;
            }
            finally
            {
                SaveButton.IsEnabled = true;
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
