using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Coverage.Core.ViewModels;

namespace Coverage.Core.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseApplicationPath(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleziona la cartella del progetto applicativo",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && DataContext is MainViewModel viewModel)
        {
            await viewModel.SelectApplicationPathAsync(folders[0].Path.LocalPath);
        }
    }

    private async void CopyErrorMessage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || string.IsNullOrWhiteSpace(viewModel.ErrorMessage))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            viewModel.ClipboardStatus = "Gli appunti non sono disponibili.";
            return;
        }

        try
        {
            await clipboard.SetTextAsync(viewModel.ErrorMessage);
            viewModel.ClipboardStatus = "Messaggio copiato negli appunti.";
        }
        catch (ExternalException exception)
        {
            viewModel.ClipboardStatus = $"Impossibile copiare il messaggio: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            viewModel.ClipboardStatus = $"Impossibile copiare il messaggio: {exception.Message}";
        }
        catch (NotSupportedException exception)
        {
            viewModel.ClipboardStatus = $"Impossibile copiare il messaggio: {exception.Message}";
        }
        catch (IOException exception)
        {
            viewModel.ClipboardStatus = $"Impossibile copiare il messaggio: {exception.Message}";
        }
    }
}