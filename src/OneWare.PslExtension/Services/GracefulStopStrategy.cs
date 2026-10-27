using System.Diagnostics;
using System.Text;
using Asmichi.ProcessManagement;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Services;
using OneWare.Essentials.ToolEngine;
using OneWare.Essentials.ToolEngine.Strategies;

namespace OneWare.PslExtension.Services;

/// <summary>
/// Runs tools like the native strategy, but Stop first asks the tool to terminate and only kills it
/// when it is still running after <see cref="GracePeriod"/>.
/// SBY starts its solvers in their own process groups, killing SBY leaves them running. Asked to terminate,
/// SBY stops them itself, and docker passes the request on to SBY in the container.
/// </summary>
public class GracefulStopStrategy(IChildProcessService childProcessService, IApplicationStateService applicationStateService,
    IOutputService outputService, ILogger logger) : IToolExecutionStrategy
{
    public const string StrategyKey = "PslExtension_GracefulStopStrategy";

    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
    
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly NativeStrategy _nativeStrategy = new();

    public async Task<(bool success, string output)> ExecuteAsync(ToolCommand command)
    {
        var path = command.Executable ?? command.ToolName;
        var toolName = Path.GetFileNameWithoutExtension(path);
        var folderName = Path.GetFileName(command.WorkingDirectory);

        var argumentString = string.Join(' ', command.Arguments.Select(x => x.Contains(' ') ? $"\"{x}\"" : x));
        logger.Log($"[{folderName}]: {toolName} {argumentString}", true, Brushes.CornflowerBlue);

        var success = true;
        var output = new StringBuilder();
        var cancelled = false;
        // Not disposed, Stop can still invoke the callback after the tool finished (the confirmation dialog is open)
        var cancel = new CancellationTokenSource();

        var state = applicationStateService.AddState(command.StatusMessage, command.State, () => cancel.Cancel());
        var start = DateTime.Now;
        var statusTimer = command.ShowTimer
            ? new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Default, (_, _) =>
            {
                var time = DateTime.Now - start;
                state.StatusMessage = $"{command.StatusMessage} {(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
            })
            : null;
        statusTimer?.Start();

        try
        {
            var childProcess = childProcessService.StartChildProcess(new ChildProcessStartInfo
            {
                FileName = path,
                Arguments = command.Arguments,
                WorkingDirectory = command.WorkingDirectory,
                StdOutputRedirection = OutputRedirection.OutputPipe,
                StdInputRedirection = InputRedirection.NullDevice,
                StdErrorRedirection = OutputRedirection.ErrorPipe
            });

            // Each reader tells whether all its lines were accepted. Handlers write to the UI, so lines are handled on the UI thread
            using var stopReading = new CancellationTokenSource();
            var readers = new[]
            {
                ReadLinesAsync(childProcess.StandardOutput, line =>
                {
                    output.AppendLine(line);
                    if (command.OutputHandler != null) return command.OutputHandler(line);
                    outputService.WriteLine(line);
                    return true;
                }, stopReading.Token),
                ReadLinesAsync(childProcess.StandardError, line =>
                {
                    output.AppendLine(line);
                    if (command.ErrorHandler != null) return command.ErrorHandler(line);
                    logger.Error(line);
                    return false;
                }, stopReading.Token)
            };

            try
            {
                await childProcess.WaitForExitAsync(cancel.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                logger.Log($"[{folderName}]: {toolName} cancelled!", true, Brushes.DarkOrange);
                await StopAsync(childProcess);
            }

            stopReading.CancelAfter(OutputDrainTimeout);
            success = (await Task.WhenAll(readers)).All(x => x);

            if (cancelled)
            {
                success = false;
            }
            else if (childProcess.ExitCode != 0)
            {
                success = false;
                logger.Error($"[{folderName}]: {toolName} exited with code {childProcess.ExitCode}");
            }
        }
        catch (Exception e)
        {
            logger.Error(e.Message, e);
            success = false;
        }

        statusTimer?.Stop();
        if (state.Terminated) success = false;
        applicationStateService.RemoveState(state);

        return (success, output.ToString());
    }

    /// <summary>
    /// Asks the process group to terminate (SIGTERM on Linux and macOS, closing the console on Windows)
    /// and kills it when it does not exit within the grace period.
    /// </summary>
    private async Task StopAsync(IChildProcess childProcess)
    {
        try
        {
            if (childProcess.CanSignal) childProcess.SignalTermination();
            if (await childProcess.WaitForExitAsync(GracePeriod, CancellationToken.None)) return;
        }
        catch (Exception e)
        {
            logger.Warning($"Could not ask the process to terminate, killing it: {e.Message}", e);
        }

        childProcess.Kill();
        await childProcess.WaitForExitAsync(CancellationToken.None);
    }

    /// <summary>
    /// Handles the lines of the stream until it ends or <paramref name="stop"/> is cancelled,
    /// returns whether every line was accepted.
    /// </summary>
    private static Task<bool> ReadLinesAsync(Stream stream, Func<string, bool> handleLine, CancellationToken stop)
    {
        return Task.Run(async () =>
        {
            var accepted = true;
            try
            {
                using var reader = new StreamReader(stream);
                // A blocked pipe read does not notice the token, so the wait is given up instead
                while (await reader.ReadLineAsync().WaitAsync(stop) is { } line)
                    accepted &= await Dispatcher.UIThread.InvokeAsync(() => handleLine(line));
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The pipe was closed, or a process the tool left running keeps it open
            }

            return accepted;
        });
    }

    public WeakReference<Process> StartWeakProcess(ToolCommand command) => _nativeStrategy.StartWeakProcess(command);

    public Guid StartProcess(ToolCommand command) => _nativeStrategy.StartProcess(command);

    public bool StopProcess(Guid handle) => _nativeStrategy.StopProcess(handle);

    public bool IsProcessRunning(Guid handle) => _nativeStrategy.IsProcessRunning(handle);

    public string GetStrategyName() => "Native, stops gracefully";

    public string GetStrategyKey() => StrategyKey;
}
