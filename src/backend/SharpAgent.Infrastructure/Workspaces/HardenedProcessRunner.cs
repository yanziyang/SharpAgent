using System.Text;
using System.Diagnostics;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Infrastructure.Workspaces;

/// <summary>
/// Hardened process execution: UseShellExecute=false, fixed working directory,
/// timeout with process-tree kill, bounded combined output, environment allowlist
/// (FR-033, FR-034, FR-035). Never invoked directly from the browser.
/// </summary>
public sealed class HardenedProcessRunner : IProcessRunner
{
    /// <summary>
    /// The only environment variables child processes receive. Everything else
    /// (including any secret-shaped variable) is stripped before spawn.
    /// </summary>
    private static readonly IReadOnlyList<string> EnvironmentAllowlist =
    [
        "PATH",
        "SYSTEMROOT",   // Windows loaders require it
        "COMSPEC",
        "TEMP",
        "TMP",
        "HOME",
        "DOTNET_CLI_TELEMETRY_OPTOUT",
        "DOTNET_NOLOGO",
        "npm_config_loglevel",
    ];

    public ProcessExecutionResult Run(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Environment allowlist: strip everything, then copy only approved keys that
        // exist in THIS process. Child values are never logged or returned.
        startInfo.EnvironmentVariables.Clear();
        foreach (var key in EnvironmentAllowlist)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(value))
            {
                startInfo.Environment[key] = value;
            }
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var truncated = false;
        var lockObject = new object();

        process.OutputDataReceived += (_, args) => Append(args.Data);
        process.ErrorDataReceived += (_, args) => Append(args.Data);

        void Append(string? data)
        {
            if (data is null)
            {
                return;
            }

            lock (lockObject)
            {
                if (output.Length >= request.OutputLimitCharacters)
                {
                    truncated = true;
                    return;
                }

                var remaining = request.OutputLimitCharacters - output.Length;
                output.Append(data.Length <= remaining ? data : data[..remaining]);
                if (data.Length > remaining)
                {
                    truncated = true;
                }
            }
        }

        if (!process.Start())
        {
            return new ProcessExecutionResult(null, "The process could not be started.", false, false, false);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        if (!process.WaitForExit((int)request.Timeout.TotalMilliseconds))
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        // Drain async readers so buffered output is captured after kill.
        process.WaitForExit();

        if (cancellationToken.IsCancellationRequested && !timedOut)
        {
            return new ProcessExecutionResult(
                ExitCode: null,
                Combine(output.ToString(), truncated),
                truncated,
                TimedOut: false,
                Cancelled: true);
        }

        return new ProcessExecutionResult(
            ExitCode: process.HasExited ? process.ExitCode : null,
            Combine(output.ToString(), truncated),
            truncated,
            timedOut,
            Cancelled: false);
    }

    private static string Combine(string output, bool truncated) =>
        truncated ? output + "\n…[output truncated]" : output;
}


