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

    private async void ExportReportPdf(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.CanExportReport)
        {
            return;
        }

        IStorageFile? file;
        try
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Esporta report PDF",
                SuggestedFileName = $"CoverageReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                DefaultExtension = "pdf",
                FileTypeChoices =
                [
                    new FilePickerFileType("Documento PDF")
                    {
                        Patterns = ["*.pdf"],
                        MimeTypes = ["application/pdf"],
                    },
                ],
            });
        }
        catch (IOException exception)
        {
            viewModel.ReportExportStatus = $"Impossibile aprire la finestra di salvataggio: {exception.Message}";
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            viewModel.ReportExportStatus = $"Accesso negato durante la selezione del file: {exception.Message}";
            return;
        }
        catch (InvalidOperationException exception)
        {
            viewModel.ReportExportStatus = $"Impossibile aprire la finestra di salvataggio: {exception.Message}";
            return;
        }
        catch (NotSupportedException exception)
        {
            viewModel.ReportExportStatus = $"Il salvataggio dei file non è supportato: {exception.Message}";
            return;
        }

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await viewModel.ExportReportPdfAsync(stream);
        }
        catch (IOException exception)
        {
            viewModel.ReportExportStatus = $"Impossibile salvare il PDF: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            viewModel.ReportExportStatus = $"Accesso negato durante il salvataggio del PDF: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            viewModel.ReportExportStatus = $"Impossibile salvare il PDF: {exception.Message}";
        }
        catch (NotSupportedException exception)
        {
            viewModel.ReportExportStatus = $"Il percorso selezionato non è supportato: {exception.Message}";
        }
    }
}