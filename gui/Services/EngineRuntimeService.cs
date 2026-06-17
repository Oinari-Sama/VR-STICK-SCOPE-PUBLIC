using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace InariKontroller.Services;

public enum SteamVrAutoStartState
{
    Enabled,
    Disabled,
    Unknown
}

public class EngineRuntimeService
{
    private const string EngineExeName = "InariKontrollerEngine.exe";

    public string? FindEnginePath()
    {
        foreach (var candidate in EnumerateEngineCandidates())
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public bool IsEngineRunning()
    {
        var enginePath = FindEnginePath();
        return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(EngineExeName))
            .Any(p =>
            {
                try
                {
                    return enginePath == null ||
                        string.Equals(p.MainModule?.FileName, enginePath, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return true;
                }
            });
    }

    public bool StartDiagnostics()
        => StartEngine(enableOsc: false, restartExisting: true);

    public bool StartVrChatOsc(int port = 9000)
        => StartEngine(enableOsc: true, port, restartExisting: true);

    public bool StopEngine()
    {
        var stoppedAny = false;
        foreach (var process in FindEngineProcesses())
        {
            try
            {
                process.Kill(entireProcessTree: true);
                stoppedAny = true;
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return stoppedAny;
    }

    public async Task<SteamVrAutoStartState> GetAutoStartStateAsync()
    {
        int exitCode = await RunEngineCommandAsync("--autostart-status");
        return exitCode switch
        {
            0 => SteamVrAutoStartState.Enabled,
            2 => SteamVrAutoStartState.Disabled,
            _ => SteamVrAutoStartState.Unknown
        };
    }

    public Task<bool> UninstallAutoStartAsync()
        => RunEngineCommandAsync("--uninstall-autostart").ContinueWith(t => t.Result == 0);

    public Task<bool> InstallAutoStartAsync()
        => RunEngineCommandAsync("--install-autostart").ContinueWith(t => t.Result == 0);

    private bool StartEngine(bool enableOsc, int port = 9000, bool restartExisting = false)
    {
        if (IsEngineRunning())
        {
            if (!restartExisting) return true;
            StopEngine();
        }

        var enginePath = FindEnginePath();
        if (enginePath == null) return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = enginePath,
            WorkingDirectory = Path.GetDirectoryName(enginePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (enableOsc)
        {
            startInfo.Environment["InariKontroller_OSC_ENABLE"] = "1";
            startInfo.Environment["InariKontroller_CORRECTION_ENABLE"] = "1";
            startInfo.Environment["InariKontroller_OSC_HOST"] = "127.0.0.1";
            startInfo.Environment["InariKontroller_OSC_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            startInfo.Environment["InariKontroller_OSC_ENABLE"] = "0";
            startInfo.Environment["InariKontroller_CORRECTION_ENABLE"] = "0";
        }

        Process.Start(startInfo);
        return true;
    }

    private async Task<int> RunEngineCommandAsync(string arguments)
    {
        var enginePath = FindEnginePath();
        if (enginePath == null) return -1;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = enginePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(enginePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var completed = await Task.Run(() => process.WaitForExit(15000));
        return completed ? process.ExitCode : -2;
    }

    private static IEnumerable<string> EnumerateEngineCandidates()
    {
        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, EngineExeName);

        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "engine", "build", "Release", EngineExeName);
            yield return Path.Combine(dir.FullName, "engine", "build", "Debug", EngineExeName);
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "InariKontroller",
            "engine",
            "build",
            "Release",
            EngineExeName);
    }

    private IEnumerable<Process> FindEngineProcesses()
    {
        var enginePath = FindEnginePath();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(EngineExeName)))
        {
            bool matches;
            try
            {
                matches = enginePath == null ||
                    string.Equals(process.MainModule?.FileName, enginePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                matches = true;
            }

            if (matches) yield return process;
            else process.Dispose();
        }
    }
}
