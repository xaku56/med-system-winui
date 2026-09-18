using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private const int PageSize = 100;

        private List<Group> _groupOptions = new();
        private long _selectedGroupId;
        private long _totalCount;
        private int _page = 1;
        private bool _suppressFilterChange;
        private bool _isPageActive;
        private CancellationTokenSource? _loadCancellation;
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
            _isPageActive = true;
            _suppressFilterChange = true;
            long? requestedGroupId = e.Parameter is long groupId ? groupId : null;
            if (e.Parameter is StudentsPageParameters parameters)
            {
                LifecycleBox.SelectedIndex = parameters.ShowArchived ? 1 : 0;
                requestedGroupId = parameters.GroupId;
            }
            LoadGroupFilter(requestedGroupId);
            _suppressFilterChange = false;
            _page = 1;
            _ = LoadDataAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _isPageActive = false;
            _loadCancellation?.Cancel();
            base.OnNavigatedFrom(e);
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

        private async Task LoadDataAsync(bool debounce = false)
        {
            if (!_isPageActive || SearchBox == null)
                return;

            _loadCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;

            try
            {
                if (debounce)
                    await Task.Delay(300, cancellation.Token);

                SetLoading(true);
                ErrorBar.IsOpen = false;
                var request = new StudentPageRequest
                {
                    SearchText = SearchBox.Text ?? "",
                    GroupId = _selectedGroupId,
                    GroupSearchText = _selectedGroupId == 0 ? GroupFilterBox.Text ?? "" : "",
                    StatusFilter = FilterBox.SelectedIndex,
                    Archived = LifecycleBox.SelectedIndex == 1,
                    Page = _page,
                    PageSize = PageSize,
                };
                var result = await Task.Run(
                    () => StudentRepository.GetPage(request), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                _page = result.Page;
                _totalCount = result.TotalCount;
                var showArchived = request.Archived;
                var dark = ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
                Rows.Clear();
                foreach (var student in result.Items)
                    Rows.Add(CreateRow(student, showArchived, dark));

                AddButton.IsEnabled = !showArchived;
                UpdatePagination();
            }
            catch (OperationCanceledException)
            {
                // Новый запрос заменил устаревший результат.
            }
            catch (Exception ex)
            {
                ErrorBar.Message = $"Не удалось загрузить студентов. {ex.Message}";
                ErrorBar.IsOpen = true;
            }
            finally
            {
                if (ReferenceEquals(_loadCancellation, cancellation))
                {
                    _loadCancellation = null;
                    SetLoading(false);
                }
                cancellation.Dispose();
            }
        }

        private static StudentRow CreateRow(Student s, bool showArchived, bool dark)
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
                SanminimumBg = sanBg,
                SanminimumFg = sanFg,
                MedicalExamBg = medBg,
                MedicalExamFg = medFg,
                FluorographyBg = fluBg,
                FluorographyFg = fluFg,
            };
        }

        private void SetLoading(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            if (isLoading)
            {
                PreviousPageButton.IsEnabled = false;
                NextPageButton.IsEnabled = false;
            }
            else
            {
                UpdatePagination();
            }
        }

        private void UpdatePagination()
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));
            var first = _totalCount == 0 ? 0 : (_page - 1) * PageSize + 1;
            var last = Math.Min((long)_page * PageSize, _totalCount);
            CountText.Text = $"Найдено: {_totalCount}";
            PageText.Text = _totalCount == 0
                ? "Нет записей"
                : $"{first}–{last} из {_totalCount} · страница {_page} из {totalPages}";
            PreviousPageButton.IsEnabled = _page > 1;
            NextPageButton.IsEnabled = _page < totalPages;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressFilterChange || !_isPageActive)
                return;
            _page = 1;
            _ = LoadDataAsync(debounce: true);
        }

        private void GroupFilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            _selectedGroupId = 0;
            UpdateGroupSuggestions(sender.Text);
            _page = 1;
            _ = LoadDataAsync(debounce: true);
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
            {
                _page = 1;
                _ = LoadDataAsync();
            }
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
            if (!_suppressFilterChange && _isPageActive)
            {
                _page = 1;
                _ = LoadDataAsync();
            }
        }

        private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterChange || !_isPageActive)
                return;
            _page = 1;
            _ = LoadDataAsync();
        }

        private void LifecycleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterChange || GroupFilterBox == null || !_isPageActive)
                return;

            _suppressFilterChange = true;
            _selectedGroupId = 0;
            GroupFilterBox.Text = "";
            LoadGroupFilter(null);
            _suppressFilterChange = false;
            _page = 1;
            _ = LoadDataAsync();
        }

        private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_page <= 1)
                return;
            _page--;
            await LoadDataAsync();
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));
            if (_page >= totalPages)
                return;
            _page++;
            await LoadDataAsync();
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
                ItemsSource = new[] { "Не указано", "Выпуск", "Отчисление", "Перевод", "Другое" },
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
                await LoadDataAsync();
            }
        }

        private async void RestoreArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
            {
                StudentRepository.RestoreFromArchive(id);
                await LoadDataAsync();
            }
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var row = Rows.FirstOrDefault(r => r.Id == id);
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
                await LoadDataAsync();
            }
        }
    }
}
