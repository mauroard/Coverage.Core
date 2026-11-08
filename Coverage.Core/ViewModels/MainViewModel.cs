using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coverage.Core.Models;
using Coverage.Core.Services;

namespace Coverage.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly CoverageAnalysisService _analysisService;
    private readonly TestProjectDiscoveryService _testProjectDiscoveryService;

    public IReadOnlyList<CoverageProjectKindOption> ProjectTypes { get; }

    public ObservableCollection<CoverageFileResult> CoverageFiles { get; } = new();
    public ObservableCollection<CoverageFileResult> FilteredCoverageFiles { get; } = new();

    public ObservableCollection<string> LogEntries { get; } = new();

    [ObservableProperty]
    private string applicationPath = string.Empty;

    [ObservableProperty]
    private string testProjectPath = string.Empty;

    [ObservableProperty]
    private bool isDiscoveringTestProject;

    [ObservableProperty]
    private string testProjectDiscoveryMessage = "Seleziona la cartella applicativa per avviare la ricerca automatica.";

    [ObservableProperty]
    private CoverageProjectKindOption? selectedProjectType;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Seleziona la cartella applicativa.";

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private string clipboardStatus = string.Empty;

    [ObservableProperty]
    private CoverageAnalysisResult? analysisResult;

    [ObservableProperty]
    private string coverageFileFilter = string.Empty;

    [ObservableProperty]
    private bool isOverallCoverageThresholdEnabled = true;

    [ObservableProperty]
    private decimal minimumOverallCoverage = 80m;

    [ObservableProperty]
    private bool isFileCoverageThresholdEnabled = true;

    [ObservableProperty]
    private decimal minimumFileCoverage = 80m;

    [ObservableProperty]
    private bool failOnThresholdViolation = true;

    [ObservableProperty]
    private CoverageQualityReport? qualityReport;

    public bool HasResult => AnalysisResult is not null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasClipboardStatus => !string.IsNullOrWhiteSpace(ClipboardStatus);

    public bool HasWarnings => AnalysisResult?.Warnings.Count > 0;

    public bool IsAnyQualityThresholdEnabled => IsOverallCoverageThresholdEnabled || IsFileCoverageThresholdEnabled;

    public bool HasQualityFindings => QualityReport?.HasFindings == true;

    public bool IsQualityGateFailed => QualityReport?.IsFailure == true;

    public bool HasQualityWarning => HasQualityFindings && !IsQualityGateFailed;

    public bool HasQualityPassed => QualityReport?.HasPassed == true;

    public bool CanSelectApplication => !IsBusy && !IsDiscoveringTestProject;

    public string ApplicationPathDisplay => string.IsNullOrWhiteSpace(ApplicationPath)
        ? "Nessuna cartella selezionata"
        : ApplicationPath;

    public string ApplicationProjectName => string.IsNullOrWhiteSpace(ApplicationPath)
        ? "Nessun progetto selezionato"
        : Path.GetExtension(ApplicationPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(ApplicationPath)
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(ApplicationPath));

    public string TestProjectName => IsDiscoveringTestProject
        ? "Ricerca del progetto unit test in corso..."
        : string.IsNullOrWhiteSpace(TestProjectPath)
            ? "Progetto unit test non trovato"
            : Path.GetExtension(TestProjectPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(TestProjectPath)
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(TestProjectPath));

    public string TestProjectPathDisplay => TestProjectPath;

    public bool HasTestProject => !string.IsNullOrWhiteSpace(TestProjectPath);

    public bool HasNoCoverageFiles => AnalysisResult is not null && CoverageFiles.Count == 0;

    public bool HasNoMatchingCoverageFiles => CoverageFiles.Count > 0 && FilteredCoverageFiles.Count == 0;

    public string CoverageFileCountText => $"Visualizzati {FilteredCoverageFiles.Count} di {CoverageFiles.Count} file";

    public MainViewModel()
        : this(new CoverageAnalysisService(), new TestProjectDiscoveryService())
    {
    }

    public MainViewModel(CoverageAnalysisService analysisService, TestProjectDiscoveryService testProjectDiscoveryService)
    {
        _analysisService = analysisService;
        _testProjectDiscoveryService = testProjectDiscoveryService;
        ProjectTypes =
        [
            new CoverageProjectKindOption(CoverageProjectKind.Microservice, "Microservizio .NET"),
            new CoverageProjectKindOption(CoverageProjectKind.FrontendMvc, "Frontend MVC"),
        ];
        SelectedProjectType = ProjectTypes[0];
    }

    public async Task SelectApplicationPathAsync(string path)
    {
        ErrorMessage = string.Empty;
        AnalysisResult = null;
        ResetCoverageFiles();
        LogEntries.Clear();
        TestProjectPath = string.Empty;
        TestProjectDiscoveryMessage = "Ricerca automatica del progetto unit test...";
        IsDiscoveringTestProject = true;
        ApplicationPath = path;

        try
        {
            var discovery = await _testProjectDiscoveryService.FindTestProjectAsync(path);
            ApplicationPath = discovery.ApplicationProjectPath ?? path;
            TestProjectPath = discovery.ProjectPath ?? string.Empty;
            TestProjectDiscoveryMessage = discovery.Message;
            StatusMessage = discovery.IsFound
                ? "Progetto unit test individuato automaticamente."
                : "Progetto unit test non trovato; verifica la cartella selezionata.";
        }
        catch (CoverageAnalysisException exception)
        {
            TestProjectDiscoveryMessage = exception.Message;
            ErrorMessage = exception.Message;
            StatusMessage = "Ricerca del progetto unit test non riuscita.";
        }
        finally
        {
            IsDiscoveringTestProject = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        ErrorMessage = string.Empty;
        AnalysisResult = null;
        ResetCoverageFiles();
        LogEntries.Clear();
        IsBusy = true;
        StatusMessage = "Preparazione dell'analisi...";

        try
        {
            var progress = new Progress<string>(AppendLog);
            var result = await _analysisService.AnalyzeAsync(
                ApplicationPath,
                TestProjectPath,
                SelectedProjectType!.Value,
                progress);

            AnalysisResult = result;
            foreach (var file in result.Files)
            {
                CoverageFiles.Add(file);
            }

            ApplyCoverageFileFilter();

            UpdateAnalysisStatusMessage();
        }
        catch (CoverageAnalysisException exception)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "Analisi non completata.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAnalyze() => !IsBusy
        && !IsDiscoveringTestProject
        && !string.IsNullOrWhiteSpace(ApplicationPath)
        && !string.IsNullOrWhiteSpace(TestProjectPath)
        && SelectedProjectType is not null;

    private void AppendLog(string message)
    {
        if (LogEntries.Count >= 100)
        {
            LogEntries.RemoveAt(0);
        }

        LogEntries.Add(message);
    }

    private void ResetCoverageFiles()
    {
        CoverageFiles.Clear();
        CoverageFileFilter = string.Empty;
        ApplyCoverageFileFilter();
    }

    private void ApplyCoverageFileFilter()
    {
        var filter = CoverageFileFilter?.Trim();
        FilteredCoverageFiles.Clear();

        if (string.IsNullOrWhiteSpace(filter))
        {
            foreach (var file in CoverageFiles)
            {
                FilteredCoverageFiles.Add(file);
            }
        }
        else
        {
            foreach (var file in CoverageFiles)
            {
                if (file.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredCoverageFiles.Add(file);
                }
            }
        }

        OnPropertyChanged(nameof(CoverageFileCountText));
        OnPropertyChanged(nameof(HasNoCoverageFiles));
        OnPropertyChanged(nameof(HasNoMatchingCoverageFiles));
    }

    partial void OnApplicationPathChanged(string value)
    {
        OnPropertyChanged(nameof(ApplicationPathDisplay));
        OnPropertyChanged(nameof(ApplicationProjectName));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    partial void OnTestProjectPathChanged(string value)
    {
        OnPropertyChanged(nameof(TestProjectName));
        OnPropertyChanged(nameof(TestProjectPathDisplay));
        OnPropertyChanged(nameof(HasTestProject));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDiscoveringTestProjectChanged(bool value)
    {
        OnPropertyChanged(nameof(TestProjectName));
        OnPropertyChanged(nameof(TestProjectPathDisplay));
        OnPropertyChanged(nameof(CanSelectApplication));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProjectTypeChanged(CoverageProjectKindOption? value) => AnalyzeCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectApplication));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        ClipboardStatus = string.Empty;
    }

    partial void OnClipboardStatusChanged(string value) => OnPropertyChanged(nameof(HasClipboardStatus));

    partial void OnAnalysisResultChanged(CoverageAnalysisResult? value)
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasNoCoverageFiles));
        RefreshQualityReport();
    }

    partial void OnCoverageFileFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasCoverageFileFilter));
        ApplyCoverageFileFilter();
    }

    partial void OnIsOverallCoverageThresholdEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyQualityThresholdEnabled));
        RefreshQualityReport();
    }

    partial void OnMinimumOverallCoverageChanged(decimal value) => RefreshQualityReport();

    partial void OnIsFileCoverageThresholdEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnyQualityThresholdEnabled));
        RefreshQualityReport();
    }

    partial void OnMinimumFileCoverageChanged(decimal value) => RefreshQualityReport();

    partial void OnFailOnThresholdViolationChanged(bool value) => RefreshQualityReport();

    partial void OnQualityReportChanged(CoverageQualityReport? value)
    {
        OnPropertyChanged(nameof(HasQualityFindings));
        OnPropertyChanged(nameof(IsQualityGateFailed));
        OnPropertyChanged(nameof(HasQualityWarning));
        OnPropertyChanged(nameof(HasQualityPassed));
    }

    private void RefreshQualityReport()
    {
        if (AnalysisResult is null || !IsAnyQualityThresholdEnabled)
        {
            QualityReport = null;
        }
        else
        {
            QualityReport = CoverageQualityEvaluator.Evaluate(
                AnalysisResult,
                IsOverallCoverageThresholdEnabled ? MinimumOverallCoverage : null,
                IsFileCoverageThresholdEnabled ? MinimumFileCoverage : null,
                FailOnThresholdViolation);
        }

        if (AnalysisResult is not null)
        {
            UpdateAnalysisStatusMessage();
        }
    }

    private void UpdateAnalysisStatusMessage()
    {
        if (QualityReport is { IsFailure: true })
        {
            StatusMessage = "Esito negativo: una o più soglie di coverage non sono rispettate.";
        }
        else if (QualityReport is { HasFindings: true })
        {
            StatusMessage = "Analisi completata con avvisi: una o più soglie di coverage non sono rispettate.";
        }
        else if (QualityReport is { HasPassed: true })
        {
            StatusMessage = "Analisi completata; soglie di qualità rispettate.";
        }
        else if (AnalysisResult?.TotalLines == 0)
        {
            StatusMessage = "Test completati, ma non sono state misurate linee di codice.";
        }
        else
        {
            StatusMessage = "Analisi completata.";
        }
    }

    public bool HasCoverageFileFilter => !string.IsNullOrWhiteSpace(CoverageFileFilter);

    [RelayCommand]
    private void ClearCoverageFileFilter() => CoverageFileFilter = string.Empty;
}
