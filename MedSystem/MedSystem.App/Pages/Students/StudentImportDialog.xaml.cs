using System;
using System.Collections.Generic;
using System.Linq;
using MedSystem.Data.Repositories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedSystem.App.Pages;

public sealed partial class StudentImportDialog : ContentDialog
{
    private readonly StudentImport _preview;
    private readonly List<string> _groups;
    private bool _updating = true;
    public int Imported { get; private set; }

    public StudentImportDialog(Page parent, StudentImport preview, List<string> groups)
    {
        _preview = preview;
        _groups = groups;
        InitializeComponent();
        XamlRoot = parent.XamlRoot;
        RequestedTheme = parent.ActualTheme;
        Layout.Width = Math.Clamp(XamlRoot.Size.Width - 100, 240, 860);
        Layout.Height = Math.Clamp(XamlRoot.Size.Height - 200, 220, 560);
        GroupBox.Text = preview.GroupName;
        IgnoredText.Text = preview.IgnoredColumns.Count > 0
            ? "Пропущенные колонки: " + string.Join(", ", preview.IgnoredColumns)
            : "";
        IgnoredText.Visibility = IgnoredText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Students.ItemsSource = preview.Rows;
        _updating = false;
        Students.SelectedIndex = 0;
        UpdateSummary();
    }

    private void Group_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_updating) return;
        _preview.GroupName = sender.Text;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = _groups.Where(g => g.Contains(sender.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
        _preview.Refresh();
        UpdateSummary();
    }

    private void Group_Chosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string group)
            sender.Text = group;
    }

    private void Row_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || Students.SelectedItem is not StudentImportRow row) return;
        _updating = true;
        var s = row.Student;
        LastNameBox.Text = s.LastName;
        FirstNameBox.Text = s.FirstName;
        MiddleNameBox.Text = s.MiddleName;
        AddressBox.Text = s.Address;
        BirthBox.Text = s.BirthDate;
        OmsBox.Text = s.Oms;
        ExamBox.Text = s.MedicalExamDate;
        FluoroBox.Text = s.FluorographyDate;
        HealthBox.Text = s.HealthGroup;
        _updating = false;
    }

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if (_updating || Students.SelectedItem is not StudentImportRow row) return;
        var s = row.Student;
        s.LastName = LastNameBox.Text.Trim();
        s.FirstName = FirstNameBox.Text.Trim();
        s.MiddleName = MiddleNameBox.Text.Trim();
        s.Address = AddressBox.Text.Trim();
        s.BirthDate = BirthBox.Text.Trim();
        s.Oms = OmsBox.Text.Trim();
        s.MedicalExamDate = ExamBox.Text.Trim();
        s.FluorographyDate = FluoroBox.Text.Trim();
        s.HealthGroup = HealthBox.Text.Trim();
        _preview.Refresh();
        UpdateSummary();
    }

    private void Include_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: StudentImportRow row } box) return;
        _preview.Include(row, box.IsChecked == true);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var count = _preview.Rows.Count(r => r.Included);
        PrimaryButtonText = $"Импортировать ({count})";
        Summary.Text = $"Выбрано {count} из {_preview.Rows.Count}. Строки с совпадениями можно отметить вручную, если это другой студент.";
        ErrorText.Text = "";
        IsPrimaryButtonEnabled = _preview.CanSave;
    }

    private void Import_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            Imported = _preview.Save();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            UpdateSummary();
            ErrorText.Text = ex.Message;
        }
    }
}
