using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedSystem.App.UI;

internal static class Dialogs
{
    public static ContentDialog Create(FrameworkElement owner, string title, object content) => new()
    {
        Title = title,
        Content = content,
        XamlRoot = owner.XamlRoot,
        RequestedTheme = owner.ActualTheme,
    };

    public static async Task MessageAsync(FrameworkElement owner, string title, string message)
    {
        var dialog = Create(owner, title, message);
        dialog.CloseButtonText = "Понятно";
        await dialog.ShowAsync();
    }

    public static Task ErrorsAsync(FrameworkElement owner, IEnumerable<string> errors) =>
        MessageAsync(owner, "Проверьте данные", string.Join("\n", errors));

    public static async Task<bool> ConfirmAsync(
        FrameworkElement owner, string title, object content, string confirmText, string cancelText = "Отмена")
    {
        var dialog = Create(owner, title, content);
        dialog.PrimaryButtonText = confirmText;
        dialog.CloseButtonText = cancelText;
        dialog.DefaultButton = ContentDialogButton.Close;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static Task<bool> ConfirmDuplicatesAsync(
        FrameworkElement owner, IEnumerable<string> duplicates, string question) =>
        ConfirmAsync(owner, "Возможный дубликат",
            "Найдены похожие записи:\n\n"
            + string.Join("\n", duplicates.Select(item => $"• {item}"))
            + $"\n\n{question}", "Сохранить всё равно", "Вернуться");

    public static async Task<string?> ArchiveReasonAsync(
        FrameworkElement owner, string title, string[] reasons)
    {
        var reasonBox = new ComboBox
        {
            Header = "Причина",
            MinWidth = 320,
            ItemsSource = reasons,
            SelectedIndex = 0,
        };
        return await ConfirmAsync(owner, title, reasonBox, "Архивировать")
            ? reasonBox.SelectedItem?.ToString() ?? "Другое"
            : null;
    }
}
