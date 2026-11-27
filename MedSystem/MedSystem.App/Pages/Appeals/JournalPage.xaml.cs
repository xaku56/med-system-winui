using System;
using System.Linq;
using System.Threading.Tasks;
using MedSystem.App.UI;
using MedSystem.Core.Models;
using MedSystem.Data.Repositories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MedSystem.App.Pages;

public sealed class JournalRow
{
    public JournalRow(Appeal appeal) => Appeal = appeal;

    public Appeal Appeal { get; }
    public long StudentId => Appeal.StudentId ?? 0;
    public Visibility LinkVisibility => StudentId > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NameVisibility => StudentId > 0 ? Visibility.Collapsed : Visibility.Visible;
    public string Heading => $"{(Appeal.CreatedTime.Length == 0 ? "Время не указано" : Appeal.CreatedTime)} · Обращение № {Appeal.Number}";
    public string Complaints => $"Жалобы: {OrDash(Appeal.Complaints)}";
    public string Diagnosis => $"Диагноз: {OrDash(Appeal.Diagnosis)}";
    public string Vitals => $"Показатели: {OrDash(Appeal.VitalsSummary)}";
    public string Actions => $"Действия и рекомендации: {OrDash(Appeal.ActionsRecommendations)}";
    public string Ambulance => Appeal.AmbulanceCalled ? "Скорая помощь вызвана" : "Вызов скорой помощи: нет";

    private static string OrDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}

public sealed partial class JournalPage : Page
{
    private readonly LatestLoad _loader = new();
    private bool _isActive;

    public JournalPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        DayPicker.Date = DateTimeOffset.Now;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isActive = true;
        LoadDay();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _isActive = false;
        _loader.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void DayPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_isActive)
            LoadDay();
    }

    private void LoadDay()
    {
        _loader.Cancel();
        ErrorBar.IsOpen = false;
        JournalList.ItemsSource = null;
        EmptyText.Visibility = Visibility.Collapsed;
        if (DayPicker.Date is not { } selected)
        {
            EmptyText.Text = "Выберите дату.";
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        var day = DateOnly.FromDateTime(selected.Date);
        _ = _loader.RunAsync(async token =>
        {
            var appeals = await Task.Run(() => AppealRepository.GetByDay(day), token);
            token.ThrowIfCancellationRequested();
            JournalList.ItemsSource = appeals.Select(appeal => new JournalRow(appeal)).ToList();
            EmptyText.Text = "За выбранный день обращений нет.";
            EmptyText.Visibility = appeals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }, loading =>
        {
            LoadingRing.IsActive = loading;
            LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }, ex =>
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        });
    }

    private void JournalList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is JournalRow row)
            Frame.Navigate(typeof(AppealFormPage), row.Appeal.Id);
    }

    private void Student_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long id } && id > 0)
            Frame.Navigate(typeof(StudentCardPage), id);
    }
}
