using System;
using System.Threading.Tasks;
using MedSystem.Core;
using MedSystem.Core.Models;
using MedSystem.Data.Repositories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedSystem.App.UI;

public sealed partial class DiagnosisPicker : ContentDialog
{
    private readonly LatestLoad _loader = new();
    private const int PageSize = 50;
    private int _page = 1;
    private bool _isOpen;

    public DiagnosisPicker() => InitializeComponent();

    public static async Task<IcdCode?> PickAsync(Page owner)
    {
        var dialog = new DiagnosisPicker { XamlRoot = owner.XamlRoot, RequestedTheme = owner.ActualTheme };
        dialog.Opened += (_, _) =>
        {
            dialog._isOpen = true;
            dialog.LoadPage();
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? dialog.CodesList.SelectedItem as IcdCode
                : null;
        }
        finally
        {
            dialog._isOpen = false;
            dialog._loader.Cancel();
        }
    }

    private void LoadPage(bool debounce = false)
    {
        var search = SearchBox.Text;
        var page = _page;
        Previous.IsEnabled = false;
        Next.IsEnabled = false;
        CodesList.ItemsSource = null;
        IsPrimaryButtonEnabled = false;
        StatusText.Text = "Поиск…";

        _ = _loader.RunAsync(async token =>
        {
            var result = await Task.Run(() => IcdRepository.GetPage(search, page, PageSize), token);
            token.ThrowIfCancellationRequested();
            CodesList.ItemsSource = result.Items;
            _page = result.Page;
            var pages = Pagination.GetTotalPages(result.TotalCount, PageSize);
            StatusText.Text = result.TotalCount == 0 ? "Диагнозы не найдены" : $"Страница {_page} из {pages}";
            Previous.IsEnabled = _page > 1;
            Next.IsEnabled = _page < pages;
        }, _ => { }, ex => StatusText.Text = $"Не удалось загрузить МКБ. {ex.Message}", debounce ? 200 : 0);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isOpen)
            return;
        _page = 1;
        LoadPage(debounce: true);
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        _page--;
        LoadPage();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        LoadPage();
    }

    private void CodesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = CodesList.SelectedItem is IcdCode;
}
