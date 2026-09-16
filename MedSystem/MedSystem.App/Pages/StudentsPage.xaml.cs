using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    public sealed class StudentsPageParameters
    {
        public long GroupId { get; init; }
        public bool ShowArchived { get; init; }
    }

    public class StudentRow
    {
        public long Id { get; set; }
        public long GroupId { get; set; }
        public string FullName { get; set; } = "";
        public string GroupName { get; set; } = "";
        public string ArchiveReason { get; set; } = "";
        public string Sanminimum { get; set; } = "";
        public string MedicalExam { get; set; } = "";
        public string Fluorography { get; set; } = "";
        public bool IsExpired { get; set; }
        public bool IsExpiring { get; set; }
        public Visibility ArchiveVisibility { get; set; }
        public Visibility RestoreVisibility { get; set; }
        public Microsoft.UI.Xaml.Media.Brush SanminimumBg { get; set; } = Badges.TransparentBg;
        public Microsoft.UI.Xaml.Media.Brush SanminimumFg { get; set; } = Badges.NormalFg;
        public Microsoft.UI.Xaml.Media.Brush MedicalExamBg { get; set; } = Badges.TransparentBg;
        public Microsoft.UI.Xaml.Media.Brush MedicalExamFg { get; set; } = Badges.NormalFg;
        public Microsoft.UI.Xaml.Media.Brush FluorographyBg { get; set; } = Badges.TransparentBg;
        public Microsoft.UI.Xaml.Media.Brush FluorographyFg { get; set; } = Badges.NormalFg;
    }

    public sealed partial class StudentsPage : Page
    {
        private List<StudentRow> _allRows = new();
        private List<Group> _groupOptions = new();
        private long _selectedGroupId;
        private bool _suppressLifecycleChange;
        public ObservableCollection<StudentRow> Rows { get; } = new();

        public StudentsPage()
        {
            InitializeComponent();
            // Страница кэшируется: поиск и фильтр сохраняются между переходами,
            // данные всё равно перезагружаются в OnNavigatedTo
            NavigationCacheMode = NavigationCacheMode.Required;
            StudentsList.ItemsSource = Rows;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            long? requestedGroupId = e.Parameter is long groupId ? groupId : null;
            if (e.Parameter is StudentsPageParameters parameters)
            {
                _suppressLifecycleChange = true;
                LifecycleBox.SelectedIndex = parameters.ShowArchived ? 1 : 0;
                _suppressLifecycleChange = false;
                requestedGroupId = parameters.GroupId;
            }
            LoadGroupFilter(requestedGroupId);
            LoadData();
        }

        private void LoadGroupFilter(long? requestedGroupId)
        {
            _groupOptions = GroupRepository.GetAll(archived: LifecycleBox.SelectedIndex == 1);
            _groupOptions.Insert(0, new Group { Id = -1, Name = "Без группы" });

            if (requestedGroupId.HasValue)
                SelectGroup(requestedGroupId.Value);
            else if (_selectedGroupId != 0)
                SelectGroup(_selectedGroupId);
            else
                UpdateGroupSuggestions(GroupFilterBox.Text);
        }

        private void LoadData()
        {
            var dark = ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
            var showArchived = LifecycleBox.SelectedIndex == 1;
            _allRows = StudentRepository.GetAll(archived: showArchived).Select(s =>
            {
                var sanStatus = ExpirationRules.GetSingleCheckupStatus(s.SanminimumDate);
                var medStatus = ExpirationRules.GetSingleCheckupStatus(s.MedicalExamDate);
                var fluStatus = ExpirationRules.GetSingleCheckupStatus(s.FluorographyDate);
                var (sanBg, sanFg) = Badges.For(sanStatus.IsExpired, sanStatus.IsExpiring, dark);
                var (medBg, medFg) = Badges.For(medStatus.IsExpired, medStatus.IsExpiring, dark);
                var (fluBg, fluFg) = Badges.For(fluStatus.IsExpired, fluStatus.IsExpiring, dark);
                var (isExpired, isExpiring) = ExpirationRules.GetPersonStatus(
                    new[] { s.SanminimumDate, s.MedicalExamDate, s.FluorographyDate });
                return new StudentRow
                {
                    Id = s.Id,
                    GroupId = s.GroupId,
                    FullName = s.FullName,
                    GroupName = s.GroupName,
                    ArchiveReason = string.IsNullOrWhiteSpace(s.ArchiveReason)
                        ? "Причина не указана"
                        : $"Причина: {s.ArchiveReason}",
                    Sanminimum = s.SanminimumDate,
                    MedicalExam = s.MedicalExamDate,
                    Fluorography = s.FluorographyDate,
                    IsExpired = isExpired,
                    IsExpiring = isExpiring,
                    ArchiveVisibility = showArchived ? Visibility.Collapsed : Visibility.Visible,
                    RestoreVisibility = showArchived ? Visibility.Visible : Visibility.Collapsed,
                    SanminimumBg = sanBg, SanminimumFg = sanFg,
                    MedicalExamBg = medBg, MedicalExamFg = medFg,
                    FluorographyBg = fluBg, FluorographyFg = fluFg,
                };
            }).ToList();
            AddButton.IsEnabled = !showArchived;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (SearchBox == null || GroupFilterBox == null || FilterBox == null)
                return;

            var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            IEnumerable<StudentRow> filtered = _allRows;

            if (!string.IsNullOrEmpty(query))
                filtered = filtered.Where(r => r.FullName.ToLowerInvariant().Contains(query));

            if (_selectedGroupId > 0)
            {
                filtered = filtered.Where(r => r.GroupId == _selectedGroupId);
            }
            else if (_selectedGroupId == -1)
            {
                filtered = filtered.Where(r => r.GroupId == 0);
            }
            else
            {
                var groupQuery = GroupFilterBox.Text?.Trim();
                if (!string.IsNullOrEmpty(groupQuery))
                    filtered = filtered.Where(r =>
                        r.GroupName.Contains(groupQuery, StringComparison.OrdinalIgnoreCase));
            }

            filtered = FilterBox.SelectedIndex switch
            {
                1 => filtered.Where(r => r.IsExpired),
                2 => filtered.Where(r => r.IsExpiring),
                _ => filtered,
            };

            var list = filtered.ToList();
            Rows.Clear();
            foreach (var row in list)
                Rows.Add(row);

            CountText.Text = $"Всего: {list.Count}";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void GroupFilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            _selectedGroupId = 0;
            UpdateGroupSuggestions(sender.Text);
            ApplyFilter();
        }

        private void GroupFilterBox_SuggestionChosen(
            AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is Group group)
                SelectGroup(group.Id);
        }

        private void GroupFilterBox_QuerySubmitted(
            AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is Group group)
            {
                SelectGroup(group.Id);
                return;
            }

            var exactMatch = _groupOptions.FirstOrDefault(g =>
                string.Equals(g.Name, sender.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
                SelectGroup(exactMatch.Id);
            else
                ApplyFilter();
        }

        private void UpdateGroupSuggestions(string? query)
        {
            var text = query?.Trim() ?? "";
            GroupFilterBox.ItemsSource = string.IsNullOrEmpty(text)
                ? null
                : _groupOptions.Where(g =>
                    g.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
        }

        private void SelectGroup(long groupId)
        {
            var group = _groupOptions.FirstOrDefault(g => g.Id == groupId);
            _selectedGroupId = group?.Id ?? 0;
            GroupFilterBox.Text = group?.Name ?? "";
            GroupFilterBox.ItemsSource = null;
            ApplyFilter();
        }

        private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

        private void LifecycleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLifecycleChange || GroupFilterBox == null)
                return;

            _selectedGroupId = 0;
            GroupFilterBox.Text = "";
            LoadGroupFilter(null);
            LoadData();
        }

        // ── Действия ─────────────────────────────────────────────────

        private void AddButton_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(StudentFormPage), 0L);

        private void GroupsButton_Click(object sender, RoutedEventArgs e) =>
            Frame.Navigate(typeof(GroupsPage));

        private void StudentsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (StudentsList.SelectedItem is StudentRow row)
                Frame.Navigate(typeof(StudentFormPage), row.Id);
        }

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
                Frame.Navigate(typeof(StudentFormPage), id);
        }

        private async void ArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var reasonBox = new ComboBox
            {
                Header = "Причина",
                MinWidth = 320,
                ItemsSource = new[] { "Выпуск", "Отчисление", "Перевод", "Другое" },
                SelectedIndex = 0,
            };
            var dialog = new ContentDialog
            {
                Title = "Архивировать студента?",
                Content = reasonBox,
                PrimaryButtonText = "Архивировать",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                StudentRepository.Archive(id, reasonBox.SelectedItem?.ToString() ?? "Другое");
                LoadData();
            }
        }

        private void RestoreArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
            {
                StudentRepository.RestoreFromArchive(id);
                LoadData();
            }
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var row = _allRows.FirstOrDefault(r => r.Id == id);
            var dialog = new ContentDialog
            {
                Title = "Перемещение в корзину",
                Content = $"Переместить студента «{row?.FullName}» в корзину?",
                PrimaryButtonText = "В корзину",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                StudentRepository.MoveToTrash(id);
                LoadData();
            }
        }
    }
}
