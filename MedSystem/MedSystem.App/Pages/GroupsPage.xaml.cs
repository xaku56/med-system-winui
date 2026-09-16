using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    public class GroupRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string StudentCount { get; set; } = "0";
    }

    /// <summary>Управление учебными группами.</summary>
    public sealed partial class GroupsPage : Page
    {
        public ObservableCollection<GroupRow> Rows { get; } = new();

        public GroupsPage()
        {
            InitializeComponent();
            GroupsList.ItemsSource = Rows;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadData();
        }

        private void LoadData()
        {
            Rows.Clear();
            foreach (var (group, studentCount) in GroupRepository.GetAllWithCounts())
            {
                Rows.Add(new GroupRow
                {
                    Id = group.Id,
                    Name = group.Name,
                    StudentCount = studentCount.ToString(),
                });
            }
        }

        // ── Добавление / переименование ──────────────────────────────

        private async void AddButton_Click(object sender, RoutedEventArgs e) =>
            await ShowNameDialogAsync(null);

        private void GroupsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (GroupsList.SelectedItem is GroupRow row)
                _ = ShowNameDialogAsync(row);
        }

        private void RenameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long id })
            {
                var row = Rows.FirstOrDefault(r => r.Id == id);
                if (row != null)
                    _ = ShowNameDialogAsync(row);
            }
        }

        private void ShowStudentsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: long groupId })
                Frame.Navigate(typeof(StudentsPage), groupId);
        }

        private async Task ShowNameDialogAsync(GroupRow? existing)
        {
            var box = new TextBox
            {
                MinWidth = 400,
                Header = "Название группы",
                PlaceholderText = "Например: 11-ИС-А",
                Text = existing?.Name ?? "",
                MaxLength = 64,
            };
            var dialog = new ContentDialog
            {
                Title = existing == null ? "Новая группа" : "Переименование группы",
                Content = box,
                PrimaryButtonText = "Сохранить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var name = box.Text.Trim();
            if (name.Length == 0)
            {
                await ShowMessageAsync("Ошибка", "Название группы не может быть пустым.");
                return;
            }

            try
            {
                if (existing == null)
                    GroupRepository.Insert(name);
                else
                    GroupRepository.Update(existing.Id, name);
            }
            catch (Exception)
            {
                await ShowMessageAsync("Ошибка", $"Группа «{name}» уже существует.");
                return;
            }

            LoadData();
        }

        // ── Удаление ─────────────────────────────────────────────────

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (row == null)
                return;

            var count = GroupRepository.GetStudentCount(id);
            if (count > 0)
            {
                await ShowMessageAsync(
                    "Группа не пуста",
                    $"В группе «{row.Name}» {count} студент(ов). Сначала переведите студентов или дождитесь функции архивирования групп.");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Перемещение в корзину",
                Content = $"Переместить группу «{row.Name}» в корзину?",
                PrimaryButtonText = "В корзину",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                GroupRepository.MoveToTrash(id);
                LoadData();
            }
        }

        // ── Перевод на следующий курс ────────────────────────────────

        private async void IncrementButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Перевод на следующий курс",
                Content = "Первая цифра в названиях всех групп увеличится (например, 11-ИС-А → 21-ИС-А). Продолжить?",
                PrimaryButtonText = "Перевести",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                var count = GroupRepository.IncrementFirstDigitInAllGroups();
                await ShowMessageAsync("Готово",
                    count > 0 ? $"Обновлено групп: {count}" : "Нет подходящих групп для обновления.");
                LoadData();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Ошибка", ex.Message);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "Понятно",
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };
            await dialog.ShowAsync();
        }
    }
}
