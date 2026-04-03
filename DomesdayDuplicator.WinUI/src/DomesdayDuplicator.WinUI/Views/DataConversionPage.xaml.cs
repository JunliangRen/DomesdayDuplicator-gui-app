// Copyright (C) Simon Inns 2018-2019 / Junliang Ren 2026
// GNU General Public License v3.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DomesdayDuplicator.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DomesdayDuplicator.WinUI.Views;

public sealed partial class DataConversionPage : Page
{
    public DataConversionViewModel ViewModel { get; } = new();

    public DataConversionPage()
    {
        InitializeComponent();
    }

    private async void OpenTenBitFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = CreateFilePicker();
        picker.FileTypeFilter.Add(".dd");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.LoadInputFile(file.Path, isTenBit: true);
        }
    }

    private async void OpenSixteenBitFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = CreateFilePicker();
        picker.FileTypeFilter.Add(".raw");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.LoadInputFile(file.Path, isTenBit: false);
        }
    }

    private async void SaveConvertedFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "converted"
        };

        if (ViewModel.OutputAsTenBit)
        {
            picker.FileTypeChoices.Add("10-bit Packed Data", new[] { ".dd" });
        }
        else
        {
            picker.FileTypeChoices.Add("16-bit Signed Data", new[] { ".raw" });
        }

        // Initialize picker with window handle
        var hWnd = GetWindowHandle();
        InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            ViewModel.OutputFilePath = file.Path;
            ViewModel.ConvertCommand.Execute(null);
        }
    }

    private FileOpenPicker CreateFilePicker()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        // Initialize picker with window handle (required for WinUI 3 desktop)
        var hWnd = GetWindowHandle();
        InitializeWithWindow.Initialize(picker, hWnd);

        return picker;
    }

    private static nint GetWindowHandle()
    {
        // Use the static MainWindow property set during App.OnLaunched
        var window = App.MainWindow
            ?? throw new InvalidOperationException("Main window not available");
        return WinRT.Interop.WindowNative.GetWindowHandle(window);
    }
}
