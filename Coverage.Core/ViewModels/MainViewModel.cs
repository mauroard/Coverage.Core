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
    private readonly StaticAnalysisService _staticAnalysisService;
    private readonly PdfReportExportService _pdfReportExportService;

    public IReadOnlyList<CoverageProjectKindOption> ProjectTypes { get; }

    public IReadOnlyList<StaticAnalysisSeverityOption> StaticAnalysisSeverityOptions { get; } =
    [
        new(null, "Tutte"),
        new(StaticAnalysisSeverity.Error, "Errore"),
        new(StaticAnalysisSeverity.Warning, "Avviso"),
        new(StaticAnalysisSeverity.Info, "Informazione"),
    ];

    public ObservableCollection<CoverageFileResult> CoverageFiles { get; } = new();
    public ObservableCollection<CoverageFileResult> FilteredCoverageFiles { get; } = new();
    public ObservableCollection<StaticAnalysisFinding> FilteredStaticAnalysisFindings { get; } = new();

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
    private StaticAnalysisResult? staticAnalysisResult;

    [ObservableProperty]
    private StaticAnalysisSeverityOption? selectedStaticAnalysisSeverity;

    [ObservableProperty]
    private string staticAnalysisRuleFilter = string.Empty;

    [ObservableProperty]
    private string staticAnalysisFileFilter = string.Empty;

    [ObservableProperty]
    private bool isStaticAnalysisBusy;

    [ObservableProperty]
    private string staticAnalysisStatusMessage = "Seleziona un progetto per avviare l'analisi statica.";

    [ObservableProperty]
    private string staticAnalysisErrorMessage = string.Empty;

    [ObservableProperty]
    private bool isReportExportBusy;

    [ObservableProperty]
    private string reportExportStatus = string.Empty;

    [ObservableProperty]
    private bool isOverallCoverageThresholdEnabled = true;

    [ObservableProperty]
    private decimal minimumOverallCoverage = 80m;

    [ObservableProperty]
    private bool isFileCoverageThresholdEnabled = false;

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

    public bool HasStaticAnalysisResult => StaticAnalysisResult is not null;

    public bool HasStaticAnalysisPanel => HasStaticAnalysisResult || IsStaticAnalysisBusy || HasStaticAnalysisError;

    public bool HasStaticAnalysisError => !string.IsNullOrWhiteSpace(StaticAnalysisErrorMessage);

    public bool HasStaticAnalysisWarnings => StaticAnalysisResult?.Warnings.Count > 0;

    public bool HasNoStaticAnalysisFindings => StaticAnalysisResult is not null && StaticAnalysisResult.Findings.Count == 0;

    public bool HasNoMatchingStaticAnalysisFindings => StaticAnalysisResult is not null
        && StaticAnalysisResult.Findings.Count > 0
        && FilteredStaticAnalysisFindings.Count == 0;

    public string StaticAnalysisFindingsCountText => StaticAnalysisResult is null
        ? string.Empty
        : $"Visualizzati {FilteredStaticAnalysisFindings.Count} di {StaticAnalysisResult.Findings.Count} rilievi";

    public bool HasExportableReport => AnalysisResult is not null || StaticAnalysisResult is not null;

    public bool CanExportReport => HasExportableReport && !IsBusy && !IsStaticAnalysisBusy && !IsReportExportBusy;

    public bool HasReportExportStatus => !string.IsNullOrWhiteSpace(ReportExportStatus);

    public bool IsAnyQualityThresholdEnabled => IsOverallCoverageThresholdEnabled || IsFileCoverageThresholdEnabled;

    public bool HasQualityFindings => QualityReport?.HasFindings == true;

    public bool IsQualityGateFailed => QualityReport?.IsFailure == true;

    public bool HasQualityWarning => HasQualityFindings && !IsQualityGateFailed;

    public bool HasQualityPassed => QualityReport?.HasPassed == true;

    public bool CanSelectApplication => !IsBusy && !IsStaticAnalysisBusy && !IsReportExportBusy && !IsDiscoveringTestProject;

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
        : this(new CoverageAnalysisService(), new TestProjectDiscoveryService(), new StaticAnalysisService())
    {
    }

    public MainViewModel(CoverageAnalysisService analysisService, TestProjectDiscoveryService testProjectDiscoveryService)
        : this(analysisService, testProjectDiscoveryService, new StaticAnalysisService())
    {
    }

    public MainViewModel(
        CoverageAnalysisService analysisService,
        TestProjectDiscoveryService testProjectDiscoveryService,
        StaticAnalysisService staticAnalysisService)
        : this(analysisService, testProjectDiscoveryService, staticAnalysisService, new PdfReportExportService())
    {
    }

    public MainViewModel(
        CoverageAnalysisService analysisService,
        TestProjectDiscoveryService testProjectDiscoveryService,
        StaticAnalysisService staticAnalysisService,
        PdfReportExportService pdfReportExportService)
    {
        _analysisService = analysisService;
        _testProjectDiscoveryService = testProjectDiscoveryService;
        _staticAnalysisService = staticAnalysisService;
        _pdfReportExportService = pdfReportExportService;
        ProjectTypes =
        [
            new CoverageProjectKindOption(CoverageProjectKind.Microservice, "Microservizio .NET"),
            new CoverageProjectKindOption(CoverageProjectKind.FrontendMvc, "Frontend MVC"),
        ];
        SelectedProjectType = ProjectTypes[0];
        SelectedStaticAnalysisSeverity = StaticAnalysisSeverityOptions[0];
    }

    public async Task SelectApplicationPathAsync(string path)
    {
        ErrorMessage = string.Empty;
        ReportExportStatus = string.Empty;
        AnalysisResult = null;
        ResetCoverageFiles();
        ResetStaticAnalysis();
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
        ReportExportStatus = string.Empty;
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

    [RelayCommand(CanExecute = nameof(CanAnalyzeStatically))]
    private async Task AnalyzeStaticAsync()
    {
        StaticAnalysisErrorMessage = string.Empty;
        ReportExportStatus = string.Empty;
        StaticAnalysisResult = null;
        IsStaticAnalysisBusy = true;
        StaticAnalysisStatusMessage = "Preparazione dell'analisi statica...";

        try
        {
            var progress = new Progress<string>(message => StaticAnalysisStatusMessage = message);
            var result = await _staticAnalysisService.AnalyzeAsync(
                ApplicationPath,
                SelectedProjectType!.Value,
                progress);
            StaticAnalysisResult = result;

            var findingSummary = result.Findings.Count == 0
                ? "nessun rilievo"
                : $"{result.Findings.Count} rilievi";
            StaticAnalysisStatusMessage = result.Warnings.Count > 0
                ? $"Analisi statica completata con avvisi: {findingSummary}."
                : $"Analisi statica completata: {findingSummary}.";
        }
        catch (StaticAnalysisException exception)
        {
            StaticAnalysisErrorMessage = exception.Message;
            StaticAnalysisStatusMessage = "Analisi statica non completata.";
        }
        finally
        {
            IsStaticAnalysisBusy = false;
        }
    }

    public async Task ExportReportPdfAsync(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            ReportExportStatus = "Non è possibile scrivere nel file selezionato.";
            return;
        }

        if (!HasExportableReport)
        {
            ReportExportStatus = "Non ci sono risultati da esportare.";
            return;
        }

        IsReportExportBusy = true;
        ReportExportStatus = "Generazione del report PDF...";
        try
        {
            var request = new PdfReportExportRequest
            {
                ProjectName = ApplicationProjectName,
                ApplicationPath = ApplicationPathDisplay,
                ProjectType = SelectedProjectType?.Label ?? "Non specificato",
                Coverage = AnalysisResult,
                QualityReport = QualityReport,
                StaticAnalysis = StaticAnalysisResult,
                MinimumOverallCoverage = IsOverallCoverageThresholdEnabled ? MinimumOverallCoverage : null,
                MinimumFileCoverage = IsFileCoverageThresholdEnabled ? MinimumFileCoverage : null,
                FailOnThresholdViolation = FailOnThresholdViolation,
            };
            var pdf = await Task.Run(() => _pdfReportExportService.Generate(request));
            await destination.WriteAsync(pdf);
            ReportExportStatus = "Report PDF esportato correttamente.";
        }
        catch (PdfReportExportException exception)
        {
            ReportExportStatus = $"Esportazione PDF non riuscita: {exception.Message}";
        }
        catch (ArgumentException exception)
        {
            ReportExportStatus = $"I dati non possono essere rappresentati nel PDF: {exception.Message}";
        }
        catch (IOException exception)
        {
            ReportExportStatus = $"Impossibile scrivere il file PDF: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportExportStatus = $"Accesso negato durante il salvataggio del PDF: {exception.Message}";
        }
        catch (NotSupportedException exception)
        {
            ReportExportStatus = $"Il percorso selezionato non è supportato: {exception.Message}";
        }
        finally
        {
            IsReportExportBusy = false;
        }
    }

    private bool CanAnalyzeStatically() => !IsBusy
        && !IsStaticAnalysisBusy
        && !IsReportExportBusy
        && !IsDiscoveringTestProject
        && !string.IsNullOrWhiteSpace(ApplicationPath)
        && SelectedProjectType is not null;

    private bool CanAnalyze() => !IsBusy
        && !IsStaticAnalysisBusy
        && !IsReportExportBusy
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

    private void ResetStaticAnalysis()
    {
        StaticAnalysisResult = null;
        StaticAnalysisErrorMessage = string.Empty;
        StaticAnalysisStatusMessage = "Seleziona un progetto per avviare l'analisi statica.";
        SelectedStaticAnalysisSeverity = StaticAnalysisSeverityOptions[0];
        StaticAnalysisRuleFilter = string.Empty;
        StaticAnalysisFileFilter = string.Empty;
        FilteredStaticAnalysisFindings.Clear();
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

    private void ApplyStaticAnalysisFilters()
    {
        FilteredStaticAnalysisFindings.Clear();
        var result = StaticAnalysisResult;
        var selectedSeverity = SelectedStaticAnalysisSeverity?.Value;
        var ruleFilter = StaticAnalysisRuleFilter?.Trim();
        var fileFilter = StaticAnalysisFileFilter?.Trim();

        if (result is not null)
        {
            foreach (var finding in result.Findings)
            {
                if (selectedSeverity.HasValue && finding.Severity != selectedSeverity.Value)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(ruleFilter)
                    && !finding.RuleId.Contains(ruleFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(fileFilter)
                    && !finding.RelativePath.Contains(fileFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FilteredStaticAnalysisFindings.Add(finding);
            }
        }

        OnPropertyChanged(nameof(StaticAnalysisFindingsCountText));
        OnPropertyChanged(nameof(HasNoStaticAnalysisFindings));
        OnPropertyChanged(nameof(HasNoMatchingStaticAnalysisFindings));
    }

    partial void OnApplicationPathChanged(string value)
    {
        OnPropertyChanged(nameof(ApplicationPathDisplay));
        OnPropertyChanged(nameof(ApplicationProjectName));
        AnalyzeCommand.NotifyCanExecuteChanged();
        AnalyzeStaticCommand.NotifyCanExecuteChanged();
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
        AnalyzeStaticCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProjectTypeChanged(CoverageProjectKindOption? value)
    {
        AnalyzeCommand.NotifyCanExecuteChanged();
        AnalyzeStaticCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectApplication));
        OnPropertyChanged(nameof(CanExportReport));
        AnalyzeCommand.NotifyCanExecuteChanged();
        AnalyzeStaticCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsStaticAnalysisBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectApplication));
        OnPropertyChanged(nameof(HasStaticAnalysisPanel));
        OnPropertyChanged(nameof(CanExportReport));
        AnalyzeCommand.NotifyCanExecuteChanged();
        AnalyzeStaticCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsReportExportBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectApplication));
        OnPropertyChanged(nameof(CanExportReport));
        AnalyzeCommand.NotifyCanExecuteChanged();
        AnalyzeStaticCommand.NotifyCanExecuteChanged();
    }

    partial void OnReportExportStatusChanged(string value) => OnPropertyChanged(nameof(HasReportExportStatus));

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
        OnPropertyChanged(nameof(HasExportableReport));
        OnPropertyChanged(nameof(CanExportReport));
        RefreshQualityReport();
    }

    partial void OnStaticAnalysisResultChanged(StaticAnalysisResult? value)
    {
        OnPropertyChanged(nameof(HasStaticAnalysisResult));
        OnPropertyChanged(nameof(HasStaticAnalysisPanel));
        OnPropertyChanged(nameof(HasStaticAnalysisWarnings));
        OnPropertyChanged(nameof(HasExportableReport));
        OnPropertyChanged(nameof(CanExportReport));
        ApplyStaticAnalysisFilters();
    }

    partial void OnStaticAnalysisErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasStaticAnalysisError));
        OnPropertyChanged(nameof(HasStaticAnalysisPanel));
    }

    partial void OnSelectedStaticAnalysisSeverityChanged(StaticAnalysisSeverityOption? value) => ApplyStaticAnalysisFilters();

    partial void OnStaticAnalysisRuleFilterChanged(string value) => ApplyStaticAnalysisFilters();

    partial void OnStaticAnalysisFileFilterChanged(string value) => ApplyStaticAnalysisFilters();

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
