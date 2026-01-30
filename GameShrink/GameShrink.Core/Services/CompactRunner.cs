using System.Diagnostics;
using System.Text;
using GameShrink.Core.Abstractions;
using GameShrink.Core.Models;
using Serilog;

namespace GameShrink.Core.Services;

public sealed class CompactRunner : ICompactRunner
{
    private readonly ILogger _log;

    public CompactRunner(ILogger log)
    {
        _log = log;
    }

    public Task<CompactRunResult> CompressAsync(
        string directory,
        CompressionAlgorithm algorithm,
        CompactRunOptions options,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var algoArg = algorithm.ToCompactArgument();
        var args = BuildArgs("/C", directory, options, algoArg);
        return RunAsync(directory, args, options, progress, cancellationToken);
    }

    public Task<CompactRunResult> UncompressAsync(
        string directory,
        CompactRunOptions options,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var args = BuildArgs("/U", directory, options, extra: null);
        return RunAsync(directory, args, options, progress, cancellationToken);
    }

    public Task<CompactQueryResult> QueryAsync(string directory, CancellationToken cancellationToken)
    {
        // Running compact without /C or /U displays compression status.
        // We'll keep it quiet and recursive.
        var args = $"/S:\"{directory}\" /I /Q";
        return QueryInternalAsync(args, cancellationToken);
    }

    private static string BuildArgs(string modeArg, string directory, CompactRunOptions options, string? extra)
    {
        // Required by spec: compact /C|/U /S:"{dir}" /I /F /Q (and /EXE:...)
        // We allow Quiet=false to get per-file lines for progress.
        var sb = new StringBuilder();
        sb.Append(modeArg);
        if (options.Recursive) sb.Append($" /S:\"{directory}\"");
        if (options.ContinueOnErrors) sb.Append(" /I");
        if (options.Force) sb.Append(" /F");
        if (options.Quiet) sb.Append(" /Q");
        if (!string.IsNullOrWhiteSpace(extra)) sb.Append(' ').Append(extra);
        return sb.ToString();
    }

    private async Task<CompactRunResult> RunAsync(
        string directory,
        string arguments,
        CompactRunOptions options,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        directory = Path.GetFullPath(directory);

        var psi = new ProcessStartInfo
        {
            FileName = "compact.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = directory
        };

        var result = new CompactRunResult();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        _log.Information("Starting compact.exe {Args}", arguments);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var sw = Stopwatch.StartNew();
        var currentFile = string.Empty;
        long processedFiles = 0;

        // Heartbeat updates so UI doesn't appear frozen when compact runs quietly or emits no parseable output.
        Task? heartbeatTask = null;

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.AppendLine(e.Data);

            // Best-effort parsing of lines that often include file names.
            // compact output varies by locale; we mostly use it to surface "current" info.
            var line = e.Data.Trim();
            if (line.Length == 0) return;

            // Heuristic: treat lines that look like paths as current file.
            if (line.Contains('\\') && (line.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Path.HasExtension(line)))
            {
                currentFile = line;
                processedFiles++;

                progress?.Report(new CompressionProgress
                {
                    CurrentFile = currentFile,
                    ProcessedFiles = (int)Math.Min(int.MaxValue, processedFiles),
                    StatusMessage = "Running compact.exe…",
                    SpeedMBps = 0,
                    EstimatedTimeRemaining = null
                });
            }
        };

        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr) stderr.AppendLine(e.Data);
            result.Errors.Add(e.Data);
        };

        try
        {
            result.Started = p.Start();

            // Ensure compact.exe can't block waiting for stdin.
            try { p.StandardInput.Close(); } catch { /* ignore */ }

            // Initial progress update immediately after start.
            progress?.Report(new CompressionProgress
            {
                CurrentFile = string.Empty,
                ProcessedFiles = 0,
                TotalFiles = 0,
                StatusMessage = "Running compact.exe…"
            });

            // Heartbeat: update status with elapsed time even if compact prints nothing.
            // If caller provides an assumed throughput, we can also show approximate progress + ETA.
            var approxTotalBytes = options.ApproxTotalBytes;
            var assumedBps = options.AssumedThroughputBytesPerSec;

            heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (!p.HasExited && !cancellationToken.IsCancellationRequested)
                    {
                        var elapsed = sw.Elapsed;

                        long processedBytesApprox = 0;
                        TimeSpan? eta = null;
                        double speedMBps = 0;

                        if (approxTotalBytes > 0 && assumedBps > 0)
                        {
                            processedBytesApprox = (long)Math.Min(approxTotalBytes, assumedBps * Math.Max(0, elapsed.TotalSeconds));
                            var remainingBytes = Math.Max(0, approxTotalBytes - processedBytesApprox);
                            eta = TimeSpan.FromSeconds(remainingBytes / assumedBps);
                            speedMBps = assumedBps / (1024d * 1024d);
                        }

                        var status = eta is null
                            ? $"Running compact.exe… Elapsed {elapsed:hh\\:mm\\:ss}"
                            : $"Running compact.exe… Elapsed {elapsed:hh\\:mm\\:ss} • ETA {eta:hh\\:mm\\:ss}";

                        progress?.Report(new CompressionProgress
                        {
                            CurrentFile = currentFile,
                            ProcessedFiles = (int)Math.Min(int.MaxValue, processedFiles),
                            TotalFiles = 0,
                            ProcessedBytes = processedBytesApprox,
                            TotalBytes = approxTotalBytes,
                            SpeedMBps = speedMBps,
                            EstimatedTimeRemaining = eta,
                            StatusMessage = status
                        });

                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignore
                }
            });

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!p.HasExited)
                    {
                        _log.Warning("Cancellation requested; killing compact.exe");
                        p.Kill(entireProcessTree: true);
                        result.WasCancelled = true;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to kill compact.exe");
                }
            });

            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            result.ExitCode = p.ExitCode;
        }
        catch (OperationCanceledException)
        {
            result.WasCancelled = true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "compact.exe failed to start or run");
            result.Errors.Add(ex.Message);
            result.ExitCode = -1;
        }
        finally
        {
            try
            {
                if (heartbeatTask is not null)
                {
                    // Give the heartbeat task a moment to observe exit and finish.
                    await Task.WhenAny(heartbeatTask, Task.Delay(250)).ConfigureAwait(false);
                }
            }
            catch { /* ignore */ }

            sw.Stop();
            lock (stdout) result.StandardOutput = stdout.ToString();
            lock (stderr) result.StandardError = stderr.ToString();
        }

        _log.Information("compact.exe finished ExitCode={ExitCode} Cancelled={Cancelled}", result.ExitCode, result.WasCancelled);
        return result;
    }

    private async Task<CompactQueryResult> QueryInternalAsync(string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "compact.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = Process.Start(psi);
        if (p is null)
        {
            return new CompactQueryResult { ExitCode = -1, Output = "Failed to start compact.exe" };
        }

        var output = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var err = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var qr = new CompactQueryResult { ExitCode = p.ExitCode, Output = output + (string.IsNullOrWhiteSpace(err) ? "" : Environment.NewLine + err) };

        // Locale-agnostic parsing is hard; keep best-effort numbers.
        // In English output you may see: "NN files within ... are compressed." and "MM files ... are not compressed."
        ExtractCounts(qr);

        return qr;
    }

    private static void ExtractCounts(CompactQueryResult qr)
    {
        try
        {
            var lines = qr.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in lines)
            {
                var line = l.Trim();
                // Try to find "files" and first int
                if (!line.Contains("file", StringComparison.OrdinalIgnoreCase)) continue;

                var first = FirstInt(line);
                if (first is null) continue;

                if (line.Contains("not", StringComparison.OrdinalIgnoreCase) && line.Contains("compress", StringComparison.OrdinalIgnoreCase))
                {
                    qr.UncompressedFiles = first;
                }
                else if (line.Contains("compress", StringComparison.OrdinalIgnoreCase))
                {
                    qr.CompressedFiles = first;
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static int? FirstInt(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0) break;
        }
        if (sb.Length == 0) return null;
        if (int.TryParse(sb.ToString(), out var v)) return v;
        return null;
    }
}
