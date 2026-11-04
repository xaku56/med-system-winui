using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MedSystem.App.UI;
using MedSystem.Data.Repositories;

namespace MedSystem.App.Pages
{
    public class GroupRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string StudentCount { get; set; } = "0";
        public string ArchiveReason { get; set; } = "";
        public bool IsArchived { get; set; }
        public Visibility ActiveVisibility { get; set; }
        public Visibility ArchiveVisibility { get; set; }
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
            var showArchived = ArchiveFilterBox.SelectedIndex == 1;
            Rows.Clear();
            foreach (var (group, studentCount) in GroupRepository.GetAllWithCounts(archived: showArchived))
            {
                Rows.Add(new GroupRow
                {
                    Id = group.Id,
                    Name = group.Name,
                    StudentCount = studentCount.ToString(),
                    ArchiveReason = string.IsNullOrWhiteSpace(group.ArchiveReason)
                        ? "Причина не указана"
                        : $"Причина: {group.ArchiveReason}",
                    IsArchived = showArchived,
                    ActiveVisibility = showArchived ? Visibility.Collapsed : Visibility.Visible,
                    ArchiveVisibility = showArchived ? Visibility.Visible : Visibility.Collapsed,
                });
            }

            AddButton.IsEnabled = !showArchived;
            IncrementButton.IsEnabled = !showArchived;
        }

        private void ArchiveFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupsList != null)
                LoadData();
        }

        // ── Добавление / переименование ──────────────────────────────

        private async void AddButton_Click(object sender, RoutedEventArgs e) =>
            await ShowNameDialogAsync(null);

        private void GroupsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (GroupsList.SelectedItem is GroupRow { IsArchived: false } row)
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
                Frame.Navigate(typeof(StudentsPage), new StudentsPageParameters
                {
                    GroupId = groupId,
                    ShowArchived = ArchiveFilterBox.SelectedIndex == 1,
                });
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
            var dialog = Dialogs.Create(this, existing == null ? "Новая группа" : "Переименование группы", box);
            dialog.PrimaryButtonText = "Сохранить";
            dialog.CloseButtonText = "Отмена";
            dialog.DefaultButton = ContentDialogButton.Primary;

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            var name = box.Text.Trim();
            if (name.Length == 0)
            {
                await Dialogs.MessageAsync(this, "Ошибка", "Название группы не может быть пустым.");
                return;
            }

            try
            {
                var saved = existing == null
                    ? GroupRepository.Insert(name)
                    : GroupRepository.Update(existing.Id, name);
                if (!saved)
                {
                    await Dialogs.MessageAsync(this, "Ошибка", $"Группа «{name}» уже существует.");
                    return;
                }
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, "Не удалось сохранить группу", ex.Message);
                return;
            }

            LoadData();
        }

        // ── Удаление ─────────────────────────────────────────────────

        private async void ArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (row == null)
                return;

            if (!await Dialogs.ConfirmAsync(this, "Завершить обучение группы?",
                $"Группа «{row.Name}» и её активные студенты ({row.StudentCount}) будут перемещены в архив.", "В архив"))
                return;

            try
            {
                var archivedStudents = GroupRepository.ArchiveWithStudents(id, "Выпуск");
                await Dialogs.MessageAsync(this, "Группа архивирована", $"Студентов перемещено в архив: {archivedStudents}.");
                LoadData();
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, "Ошибка", ex.Message);
            }
        }

        private async void RestoreArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: long id })
                return;

            try
            {
                var restoredStudents = GroupRepository.RestoreFromArchive(id);
                await Dialogs.MessageAsync(this, "Группа восстановлена", $"Студентов восстановлено: {restoredStudents}.");
                LoadData();
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, "Ошибка", ex.Message);
            }
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await RecordAction.RunAsync(async () =>
            {
                if (sender is not FrameworkElement { Tag: long id })
                    return;

                var row = Rows.FirstOrDefault(r => r.Id == id);
                if (row == null)
                    return;

                var count = GroupRepository.GetStudentCount(id);
                if (count > 0)
                {
                    await Dialogs.MessageAsync(this,
                        "Группа не пуста",
                        $"В группе «{row.Name}» {count} студент(ов). Сначала восстановите или переместите их отдельно.");
                    return;
                }

                if (await Dialogs.ConfirmAsync(this, "Перемещение в корзину",
                    $"Переместить группу «{row.Name}» в корзину?", "В корзину"))
                {
                    GroupRepository.MoveToTrash(id);
                    LoadData();
                }
            }, ex => Dialogs.MessageAsync(this, "Не удалось переместить группу в корзину", ex.Message));
        }

        // ── Перевод на следующий курс ────────────────────────────────

        private async void IncrementButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await Dialogs.ConfirmAsync(this, "Перевод на следующий курс",
                "Первая цифра в названиях всех групп увеличится (например, 11-ИС-А → 21-ИС-А). Продолжить?", "Перевести"))
                return;

            try
            {
                var count = GroupRepository.IncrementFirstDigitInAllGroups();
                await Dialogs.MessageAsync(this, "Готово",
                    count > 0 ? $"Обновлено групп: {count}" : "Нет подходящих групп для обновления.");
                LoadData();
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(this, "Ошибка", ex.Message);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }
    }
}
