using System;
using System.Collections.Generic;
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
    /// <summary>Форма добавления/редактирования студента.
    /// Параметр навигации: long id студента; 0 — новый.</summary>
    public sealed partial class StudentFormPage : Page
    {
        private long _studentId;
        private readonly List<string> _specialties = new();
        private bool _allowEmptyRegistration;
        private List<Group> _groups = new();

        public StudentFormPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _studentId = e.Parameter is long id ? id : 0;

            _specialties.Clear();
            _specialties.AddRange(SpecialtyRepository.GetAll());
            _groups = GroupRepository.GetAll();
            _groups.Insert(0, new Group { Name = "Не указана" });
            GroupBox.ItemsSource = _groups;
            GroupBox.SelectedIndex = 0;

            if (_studentId > 0)
            {
                TitleText.Text = "Редактирование студента";
                var s = StudentRepository.GetById(_studentId);
                if (s != null)
                    FillForm(s);
            }
        }

        private void FillForm(Student s)
        {
            LastNameBox.Text = s.LastName;
            FirstNameBox.Text = s.FirstName;
            MiddleNameBox.Text = s.MiddleName;
            BirthDateBox.Text = s.BirthDate;
            GroupBox.SelectedValue = s.GroupId;
            OmsBox.Text = s.Oms;
            AddressBox.Text = s.Address;
            MedicalExamBox.Text = s.MedicalExamDate;
            FluorographyBox.Text = s.FluorographyDate;
            var group = HealthGroupBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                string.Equals(item.Tag as string, s.HealthGroup, StringComparison.Ordinal));
            if (group == null)
            {
                group = new ComboBoxItem { Content = s.HealthGroup, Tag = s.HealthGroup };
                HealthGroupBox.Items.Add(group);
            }
            HealthGroupBox.SelectedItem = group;

            _allowEmptyRegistration = s.IsOnDispensaryRegister && s.Observations.Count == 0 && s.DispensaryDetails.Length == 0;
            LegacyDetailsBox.Text = s.DispensaryDetails;
            LegacyPanel.Visibility = s.DispensaryDetails.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var observation in s.Observations)
                AddObservation(observation);
            DispensaryToggle.IsOn = s.IsOnDispensaryRegister;
        }

        private void DispensaryToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ObservationsPanel.Visibility = DispensaryToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (DispensaryToggle.IsOn && !_allowEmptyRegistration && ObservationRows.Children.Count == 0
                && LegacyDetailsBox.Text.Length == 0)
                AddObservation(new DispensaryObservation());
        }

        private async void CompleteLegacy_Click(object sender, RoutedEventArgs e)
        {
            var diagnosis = await DiagnosisPicker.PickAsync(this);
            if (diagnosis == null) return;
            AddObservation(new DispensaryObservation
            {
                Specialty = LegacyDetailsBox.Text,
                IcdCode = diagnosis.Code,
                Diagnosis = diagnosis.Name,
            });
            LegacyDetailsBox.Text = "";
            LegacyPanel.Visibility = Visibility.Collapsed;
        }

        private void AddObservation_Click(object sender, RoutedEventArgs e) =>
            AddObservation(new DispensaryObservation());

        private void AddObservation(DispensaryObservation observation)
        {
            var editor = new ObservationEditor();
            editor.Initialize(this, _specialties, observation);
            editor.RemoveRequested += (_, _) => ObservationRows.Children.Remove(editor);
            ObservationRows.Children.Add(editor);
        }

        private async void EditCatalog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SpecialtyCatalog.ShowAsync(this);
                var updated = SpecialtyRepository.GetAll();
                _specialties.Clear();
                _specialties.AddRange(updated);
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, "Не удалось открыть каталог", ex.Message);
            }
        }

        private Student CollectForm() => new()
        {
            Id = _studentId,
            GroupId = GroupBox.SelectedValue is long gid ? gid : 0,
            LastName = LastNameBox.Text.Trim(),
            FirstName = FirstNameBox.Text.Trim(),
            MiddleName = MiddleNameBox.Text.Trim(),
            BirthDate = BirthDateBox.Text.Trim(),
            Oms = OmsBox.Text.Trim(),
            Address = AddressBox.Text.Trim(),
            MedicalExamDate = MedicalExamBox.Text.Trim(),
            FluorographyDate = FluorographyBox.Text.Trim(),
            HealthGroup = (HealthGroupBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
            IsOnDispensaryRegister = DispensaryToggle.IsOn,
            DispensaryDetails = DispensaryToggle.IsOn ? LegacyDetailsBox.Text : "",
            Observations = DispensaryToggle.IsOn
                ? ObservationRows.Children.OfType<ObservationEditor>().Select(editor => editor.Collect()).ToList()
                : new(),
        };

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var student = CollectForm();

            var errors = Validators.ValidateStudent(student);
            if (student.IsOnDispensaryRegister && student.Observations.Count == 0 && !_allowEmptyRegistration
                && student.DispensaryDetails.Length == 0)
                errors.Add("Добавьте наблюдение со специальностью врача и диагнозом по МКБ.");
            if (errors.Count > 0)
            {
                await Dialogs.ErrorsAsync(this, errors);
                return;
            }

            SaveButton.IsEnabled = false;
            try
            {
                var duplicates = await Task.Run(() => StudentRepository.FindDuplicates(student));
                if (duplicates.Count > 0 && !await Dialogs.ConfirmDuplicatesAsync(this, duplicates, "Сохранить студента всё равно?"))
                    return;

                await Task.Run(() =>
                {
                    if (_studentId > 0)
                        StudentRepository.Update(student);
                    else
                        StudentRepository.Insert(student);
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
