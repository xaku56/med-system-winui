using System;
using System.Threading.Tasks;
using MedSystem.Data.Repositories;
using Microsoft.UI.Xaml.Controls;

namespace MedSystem.App.UI;

internal static class SpecialtyCatalog
{
    public static async Task ShowAsync(Page owner)
    {
        while (true)
        {
            var list = new ListView { ItemsSource = SpecialtyRepository.GetAll(), MaxHeight = 330, MinWidth = 290 };
            var catalog = Dialogs.Create(owner, "Каталог специальностей", list);
            catalog.PrimaryButtonText = "Изменить";
            catalog.SecondaryButtonText = "Добавить";
            catalog.CloseButtonText = "Закрыть";
            catalog.IsPrimaryButtonEnabled = false;
            list.SelectionChanged += (_, _) => catalog.IsPrimaryButtonEnabled = list.SelectedItem != null;
            var action = await catalog.ShowAsync();
            if (action == ContentDialogResult.None) return;

            var original = action == ContentDialogResult.Primary ? (string)list.SelectedItem : null;
            var field = new TextBox { Header = "Название специальности", Text = original ?? "", MaxLength = 255 };
            var editor = Dialogs.Create(owner, original == null ? "Новая специальность" : "Редактирование специальности", field);
            editor.PrimaryButtonText = "Сохранить";
            if (original != null) editor.SecondaryButtonText = "Удалить";
            editor.CloseButtonText = "Отмена";
            var edit = await editor.ShowAsync();
            try
            {
                if (edit == ContentDialogResult.Primary)
                    SpecialtyRepository.Save(original, field.Text);
                else if (edit == ContentDialogResult.Secondary && original != null &&
                    await Dialogs.ConfirmAsync(owner, "Удалить специальность из каталога?",
                        $"«{original}» останется в ранее заполненных карточках.", "Удалить"))
                    SpecialtyRepository.Delete(original);
            }
            catch (Exception ex)
            {
                await Dialogs.MessageAsync(owner, "Не удалось изменить каталог", ex.Message);
            }
        }
    }
}
