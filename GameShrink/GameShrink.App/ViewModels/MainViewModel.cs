using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows;
using GameShrink.Core.Abstractions;
using GameShrink.Core.Models;
using GameShrink.Core.Services;
using Serilog;

namespace GameShrink.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ILogger _log;
    private readonly GameShrinkEngine _engine;
    private readonly IOperationJournal _journal;

    private CancellationTokenSource? _runCts;
    private int _runCtsSeq;
    private int _activeRunId;
    private bool _isPaused;

    private CancellationTokenSource NewRunCts(string reason)
    {
        var id = Interlocked.Increment(ref _runCtsSeq);
        _activeRunId = id;

        _log.Information("Run CTS created Id={RunId} Reason={Reason}", id, reason);

        var cts = new CancellationTokenSource();
        cts.Token.Register(() =>
        {
            _log.Warning("Run CTS cancelled (token observed) Id={RunId} Reason={Reason}", id, reason);
        });

        return cts;
    }

    private void CancelRunCts(string reason)
    {
        if (_runCts is null)
        {
            _log.Information("Cancel requested but CTS is null. Reason={Reason}", reason);
            return;
        }

        _log.Warning(
            "Cancel requested Id={RunId} Reason={Reason} Stack={Stack}",
            _activeRunId,
            reason,
            Environment.StackTrace);

        try
        {
            _runCts.Cancel();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "CTS.Cancel() threw");
        }
    }

    private string _selectedFolder = string.Empty;
    public string SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                StatusText = string.IsNullOrWhiteSpace(_selectedFolder) ? "Select a folder" : "Ready";
                RaisePropertyChanged(nameof(HasValidSelectedFolder));
            }
        }
    }

    public ObservableCollection<string> DetectedLibraryRoots { get; } = new();

    private FolderAnalysisResult? _analysis;

    private string _statusText = "Select a folder";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private string? _selectedLibraryRoot;
    public string? SelectedLibraryRoot
    {
        get => _selectedLibraryRoot;
        set => SetProperty(ref _selectedLibraryRoot, value);
    }

    public bool HasValidSelectedFolder => !string.IsNullOrWhiteSpace(SelectedFolder) && Directory.Exists(SelectedFolder);

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public string AppDataText => $"Data: {App.AppDataDir}";

    public string AnalysisTotalSizeText => _analysis is null ? "-" : Formatters.Bytes(_analysis.TotalSize);
    public string AnalysisTotalSizeOnDiskText => _analysis is null ? "-" : Formatters.Bytes(_analysis.TotalSizeOnDisk);
    public string AnalysisFileCountText => _analysis is null ? "-" : _analysis.FileCount.ToString("N0");
    public string AnalysisEstimatedSavingsText => _analysis is null ? "-" : Formatters.Bytes(_analysis.EstimatedSavings);

    public string AnalysisVolumeText
    {
        get
        {
            if (_analysis is null) return "-";
            if (!_analysis.IsNtfs) return "Not NTFS";
            if (!_analysis.SupportsCompression) return "NTFS (no per-file compression?)";
            return "NTFS (OK)";
        }
    }

    public ObservableCollection<FileRowViewModel> TopCompressibleFiles { get; } = new();
    public ObservableCollection<FolderRowViewModel> TopCompressibleFolders { get; } = new();

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (SetProperty(ref _progressPercent, value))
            {
                RaisePropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public bool IsProgressIndeterminate => IsBusy && ProgressPercent <= 0.01;

    private string _progressStatusText = "Idle";
    public string ProgressStatusText { get => _progressStatusText; set => SetProperty(ref _progressStatusText, value); }

    private string _progressCurrentFileText = string.Empty;
    public string ProgressCurrentFileText { get => _progressCurrentFileText; set => SetProperty(ref _progressCurrentFileText, value); }

    private string _lastOperationText = "-";
    public string LastOperationText { get => _lastOperationText; set => SetProperty(ref _lastOperationText, value); }

    private bool _isSafeProfileSelected = true;
    public bool IsSafeProfileSelected
    {
        get => _isSafeProfileSelected;
        set
        {
            if (SetProperty(ref _isSafeProfileSelected, value))
            {
                if (value) IsStrongerProfileSelected = false;
            }
        }
    }

    private bool _isStrongerProfileSelected;
    public bool IsStrongerProfileSelected
    {
        get => _isStrongerProfileSelected;
        set
        {
            if (SetProperty(ref _isStrongerProfileSelected, value))
            {
                if (value) IsSafeProfileSelected = false;
            }
        }
    }

    private bool _skipLowSavingsFiles = true;
    public bool SkipLowSavingsFiles { get => _skipLowSavingsFiles; set => SetProperty(ref _skipLowSavingsFiles, value); }

    private string _excludedExtensionsText = ".tmp\n.log\n.dmp";
    public string ExcludedExtensionsText { get => _excludedExtensionsText; set => SetProperty(ref _excludedExtensionsText, value); }

    private string _excludedFolderFragmentsText = "shadercache\nshader cache\ncache\ntemp\ncrash\ncrashes";
    public string ExcludedFolderFragmentsText { get => _excludedFolderFragmentsText; set => SetProperty(ref _excludedFolderFragmentsText, value); }

    public AsyncRelayCommand AnalyzeCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand DetectLibrariesCommand { get; }

    public AsyncRelayCommand StartCompressionCommand { get; }
    public RelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public RelayCommand StopCommand { get; }
    public AsyncRelayCommand RollbackCommand { get; }

    public RelayCommand ShowLogCommand { get; }

    public MainViewModel()
    {
        _log = App.Log;

        var estimator = new BrotliCompressibilityEstimator();
        var volume = new VolumeInfoService();
        var scanner = new FileScanner(estimator, volume, _log);
        var compact = new CompactRunner(_log);

        var dbPath = Path.Combine(App.AppDataDir, "gameshrink.db");
        _journal = new SqliteOperationJournal(dbPath);
        _journal.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        _engine = new GameShrinkEngine(scanner, compact, _journal, _log);

        BrowseCommand = new RelayCommand(Browse);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        DetectLibrariesCommand = new AsyncRelayCommand(DetectLibrariesAsync);

        StartCompressionCommand = new AsyncRelayCommand(StartCompressionAsync);
        PauseCommand = new RelayCommand(Pause);
        ResumeCommand = new AsyncRelayCommand(ResumeAsync);
        StopCommand = new RelayCommand(Stop);
        RollbackCommand = new AsyncRelayCommand(RollbackAsync);

        ShowLogCommand = new RelayCommand(ShowLog);

        // Try auto-detect quickly
        _ = DetectLibrariesAsync();
    }

    private void Browse()
    {
        try
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select game folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            var r = dlg.ShowDialog();
            if (r == System.Windows.Forms.DialogResult.OK)
            {
                SelectedFolder = dlg.SelectedPath;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Browse failed");
            System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task DetectLibrariesAsync()
    {
        try
        {
            StatusText = "Detecting libraries…";
            DetectedLibraryRoots.Clear();

            var roots = await Task.Run(() => GameLibraryDetector.DetectLibraryRoots(_log)).ConfigureAwait(true);
            foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                DetectedLibraryRoots.Add(r);
            }

            StatusText = $"Detected {DetectedLibraryRoots.Count} library root(s)";
            SelectedLibraryRoot = DetectedLibraryRoots.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Library detection failed");
            StatusText = "Library detection failed";
        }
    }

    private ScanOptions BuildScanOptions()
    {
        var opt = new ScanOptions
        {
            DryRunOnly = true,
            DoNotFollowReparsePoints = true,
            MinSavingsRatioToConsider = SkipLowSavingsFiles ? 0.03 : 0.0
        };

        opt.ExcludedExtensions = new HashSet<string>(
            SplitLines(ExcludedExtensionsText)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Select(s => s.StartsWith('.') ? s : "." + s),
            StringComparer.OrdinalIgnoreCase);

        opt.ExcludedFolderNameFragments = SplitLines(ExcludedFolderFragmentsText)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        return opt;
    }

    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder) || !Directory.Exists(SelectedFolder))
        {
            System.Windows.MessageBox.Show("Select an existing folder first.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        CancelRunCts("AnalyzeAsync(start)");
        _runCts = NewRunCts("AnalyzeAsync(start)");
        _isPaused = false;

        IsBusy = true;
        ProgressPercent = 0;
        ProgressStatusText = "Analyzing…";
        ProgressCurrentFileText = string.Empty;
        StatusText = "Analyzing…";

        try
        {
            var prog = new Progress<CompressionProgress>(p =>
            {
                ProgressPercent = p.Percentage;
                ProgressStatusText = p.StatusMessage ?? "Analyzing…";
                ProgressCurrentFileText = string.IsNullOrWhiteSpace(p.CurrentFile) ? "" : $"Current: {p.CurrentFile}";
            });

            var scanOpt = BuildScanOptions();
            var res = await _engine.AnalyzeAsync(SelectedFolder, scanOpt, prog, _runCts.Token).ConfigureAwait(true);
            _analysis = res;

            RaisePropertyChanged(nameof(AnalysisTotalSizeText));
            RaisePropertyChanged(nameof(AnalysisTotalSizeOnDiskText));
            RaisePropertyChanged(nameof(AnalysisFileCountText));
            RaisePropertyChanged(nameof(AnalysisEstimatedSavingsText));
            RaisePropertyChanged(nameof(AnalysisVolumeText));

            TopCompressibleFiles.Clear();
            foreach (var f in res.Files
                .Where(f => f.IsCompressible)
                .OrderByDescending(f => f.EstimatedSavings)
                .Take(10))
            {
                TopCompressibleFiles.Add(new FileRowViewModel { Model = f });
            }

            TopCompressibleFolders.Clear();
            foreach (var row in res.Files
                .Where(f => f.IsCompressible)
                .GroupBy(f => Path.GetDirectoryName(f.RelativePath) ?? "(root)")
                .Select(g => new { Folder = g.Key, Savings = g.Sum(x => x.EstimatedSavings) })
                .OrderByDescending(x => x.Savings)
                .Take(10))
            {
                TopCompressibleFolders.Add(new FolderRowViewModel { Folder = row.Folder, Savings = row.Savings });
            }

            if (!string.IsNullOrWhiteSpace(res.WarningMessage))
            {
                System.Windows.MessageBox.Show(res.WarningMessage, "Volume warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }

            StatusText = "Analysis complete";
            ProgressStatusText = "Analysis complete";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analysis cancelled";
            ProgressStatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Analyze failed");
            StatusText = "Analysis failed";
            ProgressStatusText = "Failed";
            System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartCompressionAsync()
    {
        if (_analysis is null)
        {
            System.Windows.MessageBox.Show("Run analysis first.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        if (!_analysis.IsNtfs || !_analysis.SupportsCompression)
        {
            System.Windows.MessageBox.Show("Selected folder is not on an NTFS volume that supports per-file compression.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (_isPaused)
        {
            System.Windows.MessageBox.Show("Currently paused. Use Resume.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        CancelRunCts("StartCompressionAsync(start)");
        _runCts = NewRunCts("StartCompressionAsync(start)");

        var mode = IsStrongerProfileSelected ? CompressionMode.StrongerLzx : CompressionMode.Safe;
        var algo = IsStrongerProfileSelected ? CompressionAlgorithm.Lzx : CompressionAlgorithm.NTFS;

        // For large game folders, verbose compact output can be extremely large and make the app appear “stuck”.
        // We run compact in quiet mode and rely on a heartbeat progress update from the runner.
        // To still provide an ETA, we pass a best-effort total + assumed throughput.
        var assumedBps = await EstimateThroughputBytesPerSecAsync(algo, _runCts.Token).ConfigureAwait(true);
        var runOpt = new CompactRunOptions
        {
            Quiet = true,
            ApproxTotalBytes = _analysis.TotalSizeOnDisk,
            AssumedThroughputBytesPerSec = assumedBps
        };

        IsBusy = true;
        ProgressPercent = 0;
        ProgressStatusText = "Starting compact.exe…";
        ProgressCurrentFileText = "";
        StatusText = "Compressing…";

        try
        {
            var prog = new Progress<CompressionProgress>(p =>
            {
                ProgressStatusText = p.StatusMessage ?? "Running…";
                ProgressCurrentFileText = string.IsNullOrWhiteSpace(p.CurrentFile) ? "" : $"Current: {p.CurrentFile}";

                // compact.exe doesn't provide reliable byte progress. If we ever get bytes, use them.
                if (p.Percentage > 0)
                {
                    ProgressPercent = p.Percentage;
                }
                else
                {
                    // Keep at 0 so ProgressBar stays indeterminate.
                    if (ProgressPercent != 0) ProgressPercent = 0;
                }
            });

            WarnIfGameMightBeRunning();

            var op = await _engine.CompressAsync(
                SelectedFolder,
                mode,
                algo,
                runOpt,
                _analysis.TotalSizeOnDisk,
                prog,
                _runCts.Token).ConfigureAwait(true);

            LastOperationText = BuildOperationSummary(op);
            StatusText = "Done";
            ProgressStatusText = "Finished";

            try { SystemSounds.Asterisk.Play(); } catch { /* ignore */ }

            // Post-scan to refresh estimates/flags
            await AnalyzeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Compression cancelled";
            ProgressStatusText = "Cancelled";
            try { SystemSounds.Exclamation.Play(); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Compression failed");
            StatusText = "Compression failed";
            ProgressStatusText = "Failed";
            try { SystemSounds.Hand.Play(); } catch { /* ignore */ }
            System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Pause()
    {
        if (_runCts is null) return;
        if (_isPaused) return;

        _isPaused = true;
        CancelRunCts("Pause()");
        ProgressStatusText = "Paused (compact.exe cancelled). Resume will re-run compact.exe.";
        StatusText = "Paused";
    }

    private async Task ResumeAsync()
    {
        if (!_isPaused)
        {
            System.Windows.MessageBox.Show("Not paused.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        _isPaused = false;
        await StartCompressionAsync().ConfigureAwait(true);
    }

    private void Stop()
    {
        _isPaused = false;
        CancelRunCts("Stop()");
        StatusText = "Stopping…";
    }

    private async Task RollbackAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder) || !Directory.Exists(SelectedFolder))
        {
            System.Windows.MessageBox.Show("Select an existing folder first.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        CancelRunCts("RollbackAsync(start)");
        _runCts = NewRunCts("RollbackAsync(start)");
        _isPaused = false;

        IsBusy = true;
        ProgressPercent = 0;
        ProgressStatusText = "Starting rollback (compact /U)…";
        ProgressCurrentFileText = "";
        StatusText = "Rolling back…";

        try
        {
            var before = _analysis?.TotalSizeOnDisk ?? 0;
            var assumedBps = await EstimateThroughputBytesPerSecAsync(CompressionAlgorithm.None, _runCts.Token).ConfigureAwait(true);

            var prog = new Progress<CompressionProgress>(p =>
            {
                var current = string.IsNullOrWhiteSpace(p.CurrentFile) ? "" : $"Current: {p.CurrentFile}";

                ProgressStatusText = p.StatusMessage ?? "Running rollback…";
                ProgressCurrentFileText = current;

                // Live status in the small console during rollback.
                LastOperationText = string.IsNullOrWhiteSpace(current)
                    ? "Rollback in progress…"
                    : $"Rollback in progress…{Environment.NewLine}{current}";
            });

            var op = await _engine.RollbackAsync(
                SelectedFolder,
                originalOperationId: null,
                options: new CompactRunOptions
                {
                    Quiet = true,
                    ApproxTotalBytes = before,
                    AssumedThroughputBytesPerSec = assumedBps
                },
                beforeBytes: before,
                progress: prog,
                ct: _runCts.Token).ConfigureAwait(true);

            // Integrity check: compact query
            var qr = await new CompactRunner(_log).QueryAsync(SelectedFolder, _runCts.Token).ConfigureAwait(true);
            var integrity = qr.CompressedFiles is > 0
                ? $"Integrity check: compact reports compressed files still present: {qr.CompressedFiles}."
                : "Integrity check: compact reports no compressed files (best-effort).";

            LastOperationText = BuildOperationSummary(op) + Environment.NewLine + integrity;
            StatusText = "Rollback finished";
            ProgressStatusText = "Finished";

            try { SystemSounds.Asterisk.Play(); } catch { /* ignore */ }

            await AnalyzeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Rollback cancelled";
            ProgressStatusText = "Cancelled";
            try { SystemSounds.Exclamation.Play(); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Rollback failed");
            StatusText = "Rollback failed";
            ProgressStatusText = "Failed";
            try { SystemSounds.Hand.Play(); } catch { /* ignore */ }
            System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowLog()
    {
        try
        {
            var dir = App.AppDataDir;
            if (!Directory.Exists(dir))
            {
                System.Windows.MessageBox.Show("Log folder not found yet.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            // Serilog rolling creates: gameshrinkYYYYMMDD.log when using gameshrink-.log pattern.
            var latest = Directory.EnumerateFiles(dir, "gameshrink*.log", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is null)
            {
                System.Windows.MessageBox.Show("Log file not found yet.", "GameShrink", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{latest.FullName}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Show log failed");
        }
    }

    private void WarnIfGameMightBeRunning()
    {
        // Minimal, non-invasive: show a generic reminder.
        System.Windows.MessageBox.Show(
            "Recommendation: close the game and launcher before compressing/uncompressing.\n\n" +
            "GameShrink will not patch files, but changing compression attributes while the game is running can cause IO errors or anti-cheat flags.",
            "Reminder",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string BuildOperationSummary(OperationRecord op)
    {
        var duration = op.FinishedAt is null ? "-" : (op.FinishedAt.Value - op.StartedAt).ToString("g");
        var delta = op.BeforeBytes > 0 && op.AfterBytes > 0 ? (op.BeforeBytes - op.AfterBytes) : 0;

        return $"Operation: {(op.IsRollback ? "Rollback" : "Compress")}" + Environment.NewLine +
               $"Status: {op.Status}" + Environment.NewLine +
               $"Folder: {op.Path}" + Environment.NewLine +
               $"Mode/Algorithm: {op.Mode} / {op.Algorithm}" + Environment.NewLine +
               $"Before (size on disk): {Formatters.Bytes(op.BeforeBytes)}" + Environment.NewLine +
               $"After (size on disk): {Formatters.Bytes(op.AfterBytes)}" + Environment.NewLine +
               $"Recovered/Saved (approx): {Formatters.Bytes(delta)}" + Environment.NewLine +
               $"Duration: {duration}" + (string.IsNullOrWhiteSpace(op.ErrorMessage) ? "" : Environment.NewLine + "Error: " + op.ErrorMessage);
    }

    private static IEnumerable<string> SplitLines(string s)
        => s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

    private async Task<double> EstimateThroughputBytesPerSecAsync(CompressionAlgorithm algorithm, CancellationToken ct)
    {
        try
        {
            // Best-effort: compute a throughput estimate from recent completed operations.
            // We store BeforeBytes as size-on-disk now, which is what we want for ETA.
            var recent = await _journal.GetRecentAsync(40, ct).ConfigureAwait(false);

            var candidates = recent
                .Where(r => !r.IsRollback)
                .Where(r => r.Status == OperationStatus.Completed)
                .Where(r => r.BeforeBytes > 0)
                .Where(r => r.FinishedAt is not null)
                .Select(r => new
                {
                    Algo = r.Algorithm,
                    Bps = r.BeforeBytes / Math.Max(1.0, (r.FinishedAt!.Value - r.StartedAt).TotalSeconds)
                })
                .Where(x => x.Bps > 1024 * 1024) // ignore absurdly low/invalid
                .ToList();

            // Prefer same algorithm samples first.
            var picked = candidates.Where(x => x.Algo == algorithm).Select(x => x.Bps).ToList();
            if (picked.Count < 3)
            {
                picked = candidates.Select(x => x.Bps).ToList();
            }

            if (picked.Count > 0)
            {
                picked.Sort();
                var median = picked[picked.Count / 2];
                return median;
            }
        }
        catch
        {
            // ignore
        }

        // Fallback heuristics (very approximate, but better than nothing).
        return algorithm switch
        {
            CompressionAlgorithm.Lzx => 15d * 1024 * 1024,
            CompressionAlgorithm.NTFS => 30d * 1024 * 1024,
            _ => 25d * 1024 * 1024
        };
    }
}
