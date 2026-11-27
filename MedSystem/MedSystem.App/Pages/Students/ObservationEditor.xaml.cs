using System;
using System.Collections.Generic;
using System.Linq;
using MedSystem.App.UI;
using MedSystem.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedSystem.App.Pages;

public sealed partial class ObservationEditor : UserControl
{
    private IcdCode? _diagnosis;
    private Page _owner = null!;
    private IReadOnlyList<string> _specialties = Array.Empty<string>();

    public ObservationEditor() => InitializeComponent();

    public event EventHandler? RemoveRequested;

    internal void Initialize(Page owner, IReadOnlyList<string> specialties, DispensaryObservation item)
    {
        _owner = owner;
        _specialties = specialties;
        SpecialtyBox.Text = item.Specialty;
        if (item.IcdCode.Length > 0)
            _diagnosis = new IcdCode { Code = item.IcdCode, Name = item.Diagnosis };
        ShowDiagnosis();
    }

    internal DispensaryObservation Collect() => new()
    {
        Specialty = SpecialtyBox.Text.Trim(),
        IcdCode = _diagnosis?.Code ?? "",
        Diagnosis = _diagnosis?.Name ?? "",
    };

    private void OpenList_Click(object sender, RoutedEventArgs e)
    {
        SpecialtyBox.Focus(FocusState.Programmatic);
        SpecialtyBox.ItemsSource = _specialties.ToList();
        SpecialtyBox.IsSuggestionListOpen = true;
    }

    private void SpecialtyBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = _specialties.Where(name =>
                name.Contains(sender.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void SpecialtyBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string name) sender.Text = name;
    }

    private async void PickDiagnosis_Click(object sender, RoutedEventArgs e)
    {
        var diagnosis = await DiagnosisPicker.PickAsync(_owner);
        if (diagnosis == null) return;
        _diagnosis = diagnosis;
        ShowDiagnosis();
    }

    private void ShowDiagnosis() =>
        DiagnosisText.Text = _diagnosis == null ? "Не выбран" : $"{_diagnosis.Code} {_diagnosis.Name}";

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
}
