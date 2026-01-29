using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Serilog;

namespace GameShrink.App;

public static class GameLibraryDetector
{
    public static IReadOnlyList<string> DetectLibraryRoots(ILogger log)
    {
        var roots = new List<string>();

        // Steam: registry InstallPath + libraryfolders.vdf
        try
        {
            var steamPath = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null)
                            ?? (string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null)
                            ?? (string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null);

            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                steamPath = steamPath.Replace('/', '\\');
                log.Information("Steam path detected: {SteamPath}", steamPath);

                var steamApps = Path.Combine(steamPath, "steamapps");
                if (Directory.Exists(steamApps))
                {
                    roots.Add(Path.Combine(steamApps, "common"));
                }

                var vdf = Path.Combine(steamApps, "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (var lib in ParseSteamLibraryFoldersVdf(vdf, log))
                    {
                        var common = Path.Combine(lib, "steamapps", "common");
                        if (Directory.Exists(common)) roots.Add(common);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Steam detection failed");
        }

        // Epic Games: read manifests from ProgramData (no API usage).
        try
        {
            var manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic",
                "EpicGamesLauncher",
                "Data",
                "Manifests");

            if (Directory.Exists(manifests))
            {
                foreach (var itemFile in Directory.EnumerateFiles(manifests, "*.item", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using var fs = File.OpenRead(itemFile);
                        using var doc = JsonDocument.Parse(fs);
                        if (doc.RootElement.TryGetProperty("InstallLocation", out var locEl) && locEl.ValueKind == JsonValueKind.String)
                        {
                            var loc = locEl.GetString();
                            if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc))
                            {
                                roots.Add(loc);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Debug(ex, "Failed to parse Epic manifest {File}", itemFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Epic detection failed");
        }

        // Common default paths (best effort)
        AddIfExists(roots, @"C:\Program Files (x86)\Steam\steamapps\common");
        AddIfExists(roots, @"D:\SteamLibrary\steamapps\common");

        return roots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddIfExists(List<string> roots, string path)
    {
        try
        {
            if (Directory.Exists(path)) roots.Add(path);
        }
        catch { /* ignore */ }
    }

    private static IEnumerable<string> ParseSteamLibraryFoldersVdf(string vdfPath, ILogger log)
    {
        // Minimal parsing: look for lines like: "path"  "D:\\SteamLibrary"
        // VDF format varies; this is best-effort and safe.
        var libs = new List<string>();
        foreach (var line in File.ReadAllLines(vdfPath))
        {
            var l = line.Trim();
            if (!l.Contains("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = l.Split('"', StringSplitOptions.RemoveEmptyEntries);
            // Expected: path, D:\SteamLibrary
            if (parts.Length >= 2)
            {
                var path = parts[^1].Replace("\\\\", "\\");
                if (Directory.Exists(path))
                {
                    libs.Add(path);
                }
            }
        }

        if (libs.Count > 0)
        {
            log.Information("Steam libraries from VDF: {Count}", libs.Count);
        }

        return libs;
    }
}
