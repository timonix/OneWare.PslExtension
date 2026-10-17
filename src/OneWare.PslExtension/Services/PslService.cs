using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Extensions;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ToolEngine;
using OneWare.Essentials.ViewModels;
using OneWare.PslExtension.ViewModels;
using OneWare.UniversalFpgaProjectSystem.Context;
using OneWare.UniversalFpgaProjectSystem.Models;

namespace OneWare.PslExtension.Services;

public class PslService
{
    private const string BuildFolderName = "build";
    private const int DockerCheckTimeoutSeconds = 15;

    private readonly ILogger _logger;
    private readonly IMainDockService _dockService;
    private readonly IPackageService _packageService;
    private readonly IToolExecutionDispatcherService _toolExecutionDispatcherService;
    private readonly IEnvironmentService _environmentService;
    private readonly IOutputService _outputService;
    private readonly ISettingsService _settingsService;
    private readonly IProjectExplorerService _projectExplorerService;
    
    public PslService(ILogger logger, IMainDockService dockService, ISettingsService settingsService, IPackageService packageService, IEnvironmentService environmentService,
        IOutputService outputService, IProjectExplorerService projectExplorerService, IToolExecutionDispatcherService toolExecutionDispatcherService)
    {
        _logger = logger;
        _dockService = dockService;
        _packageService = packageService;
        _toolExecutionDispatcherService = toolExecutionDispatcherService;
        _environmentService = environmentService;
        _outputService = outputService;
        _settingsService = settingsService;
        _projectExplorerService = projectExplorerService;
        
    }
    
    private static string GetSbyPath(string vhdlFullPath)
    {
        return Path.Combine(Path.GetDirectoryName(vhdlFullPath)!, $"{Path.GetFileNameWithoutExtension(vhdlFullPath)}.sby");
    }

    private static bool IsInBuildFolder(string relativePath)
    {
        return relativePath
            .Split('/', '\\')
            .SkipLast(1)
            .Any(x => x.Equals(BuildFolderName, StringComparison.OrdinalIgnoreCase));
    }

    HashSet<string> ExtractComponents(string filePath)
    {
        HashSet<string> components = new HashSet<string>();
        string pattern = @"\s*component\s+(\w+)";

        try
        {
            foreach (string line in File.ReadLines(filePath))
            {
                Match match = Regex.Match(line, pattern);
                if (match.Success)
                {
                    components.Add(match.Groups[1].Value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error reading file {filePath}: {ex.Message}", ex);
        }

        return components;
    }
    HashSet<string> ExtractEntities(string filePath)
    {
        HashSet<string> entities = new HashSet<string>();
        string pattern = @"\s+work\.(\w+)";

        try
        {
            foreach (string line in File.ReadLines(filePath))
            {
                Match match = Regex.Match(line, pattern);
                if (match.Success)
                {
                    entities.Add(match.Groups[1].Value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error reading file {filePath}: {ex.Message}", ex);
        }

        return entities;
    }
    
    public async Task<bool> GenerateSbyAsync(string fullPath)
    {   
        if (_projectExplorerService.GetRootFromFile(fullPath) is not UniversalFpgaProjectRoot root) return false;
        
        var context = await TestBenchContextManager.LoadContextAsync(fullPath);
        var engine = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.BmcEngine)) ?? "smtbmc";
        var mode = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.BmcMode)) ?? "bmc";
        var solver = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.Solver)) ?? "z3";
        var depth = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.SearchDepth)) ?? "25";
        var entity = Path.GetFileNameWithoutExtension(fullPath);
        var sbyDirectory = Path.GetDirectoryName(fullPath)!;
        
        // Project files are relative to the project root folder. Entities are looked up by file name,
        // VHDL names are case insensitive. Build folders only hold tool output (e.g. SBY copies of the sources).
        var vhdlFilesByEntity = root.GetFiles()
            .Where(x => !root.IsCompileExcluded(x))
            .Where(x => Path.GetExtension(x) is ".vhd" or ".vhdl")
            .Where(x => !IsInBuildFolder(x))
            .GroupBy(x => Path.GetFileNameWithoutExtension(x), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(x => x.Count(c => c is '/' or '\\'))
                .ThenBy(x => x, StringComparer.Ordinal)
                .ToList(), StringComparer.OrdinalIgnoreCase);

        // One file per entity, starting from the top entity
        var usedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entity] = Path.GetRelativePath(root.RootFolderPath, fullPath)
        };
        var filesToScan = new Queue<string>(usedFiles.Values);

        while (filesToScan.TryDequeue(out var f))
        {
            var fileFullPath = Path.Combine(root.RootFolderPath, f);
            var dependencies = ExtractEntities(fileFullPath);
            dependencies.UnionWith(ExtractComponents(fileFullPath));

            foreach (var dependency in dependencies)
            {
                if (usedFiles.ContainsKey(dependency) || !vhdlFilesByEntity.TryGetValue(dependency, out var matches)) continue;

                if (matches.Count > 1)
                    _logger.Warning($"Entity '{dependency}' is defined in multiple files, using {matches[0]}. Ignored: {string.Join(", ", matches.Skip(1))}");

                usedFiles[dependency] = matches[0];
                filesToScan.Enqueue(matches[0]);
            }
        }

        var vhdlFiles = usedFiles.Values
            .Where(x => !root.IsCompileExcluded(x))
            .ToList();
        
        var vhdlFilesWithRelativePaths = vhdlFiles
            .Select(x => Path.GetRelativePath(sbyDirectory, Path.Combine(root.RootFolderPath, x)));
        
        var vhdlFilesNamesOnly = vhdlFiles
            .Select(Path.GetFileName);
        
        var sb = new StringBuilder();
    
        sb.AppendLine("[tasks]");
        sb.AppendLine("bmc");
        sb.AppendLine();
    
        sb.AppendLine("[options]");
        sb.AppendLine($"depth {depth}");
        sb.AppendLine($"bmc: mode {mode}");
        sb.AppendLine();
    
        sb.AppendLine("[engines]");
        sb.AppendLine($"bmc: {engine} {solver}");
        sb.AppendLine();
    
        sb.AppendLine("[script]");
        // -fpsl: also check PSL written as "-- psl" comments (without it they are silently ignored)
        sb.Append("bmc: ghdl --std=08 -fpsl ");
        sb.Append(string.Join(" ", vhdlFilesNamesOnly.Select(x => x)));
        sb.AppendLine($" -e {entity}");
        sb.AppendLine($"prep -top {entity}");
        sb.AppendLine();
    
        sb.AppendLine("[files]");
        foreach (var vhdlFile in vhdlFilesWithRelativePaths)
        {
            sb.AppendLine(vhdlFile.Replace("\\","/"));
        }
        
        var outputFilePath = GetSbyPath(fullPath);

        try
        {
            await File.WriteAllTextAsync(outputFilePath, sb.ToString());
            _outputService.WriteLine($"SBY file successfully generated at {outputFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error writing SBY file {outputFilePath}: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> GenerateAndSimulateSbyAsync(string fullPath)
    {
        if (!await GenerateSbyAsync(fullPath)) return false;

        return await RunSbyAsync(GetSbyPath(fullPath));
    }

    /// <summary>
    /// Checks that docker is installed and its engine is reachable, and reports a clear error if not.
    /// Runs silently, so only the result shows up in the output.
    /// </summary>
    private async Task<bool> CheckDockerAsync()
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("info");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.ServerVersion}}");

        try
        {
            using var process = Process.Start(startInfo)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(DockerCheckTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                _logger.Error($"Docker did not respond within {DockerCheckTimeoutSeconds} seconds. " +
                              "Make sure Docker (e.g. Docker Desktop or Rancher Desktop) has finished starting and try again.",
                    null, true, true);
                return false;
            }

            await stdoutTask;
            if (process.ExitCode == 0) return true;

            var details = (await stderrTask).Trim().Split('\n')[0].Trim();
            _logger.Error("Docker is installed, but its engine is not running. " +
                          "Start Docker Desktop or Rancher Desktop (or the docker service) and try again." +
                          (details.Length > 0 ? $"\nDocker: {details}" : ""),
                null, true, true);
            return false;
        }
        catch (Win32Exception)
        {
            _logger.Error("Docker was not found. SymbiYosys runs inside the hdlc/formal Docker container, " +
                          "so install Docker (e.g. Docker Desktop or Rancher Desktop) and make sure 'docker' is on your PATH.",
                null, true, true);
            return false;
        }
    }

    public async Task<bool> RunSbyAsync(string fullPath)
    {
        if (_projectExplorerService.GetRootFromFile(fullPath) is not { } root) return false;

        if (!await CheckDockerAsync()) return false;

        var userPath = root.RootFolderPath;
        const string containerWorkspace = "/workspace";
        const string containerImage = "hdlc/formal";
        const string yosysCommand = "yosys -m ghdl";
        var sbyFileName = Path.GetFileName(fullPath);
        // SBY output goes to <project>/build/psl/<name>_<task>, so its copies of the sources stay out of the project
        var workdirPrefix = $"{containerWorkspace}/{BuildFolderName}/psl/{Path.GetFileNameWithoutExtension(fullPath)}";
        
        var relativePath = Path.GetRelativePath(userPath, Path.GetDirectoryName(fullPath)!).Replace('\\', '/');

        List<string> arguments = new List<string>
        {
            "run", "--rm",
            "-v", $"{userPath}:{containerWorkspace}",
            "-w", $"{containerWorkspace}/{relativePath}",
            containerImage,
            "sby", "--yosys", yosysCommand, "--prefix", workdirPrefix, "-f", sbyFileName
        };

        var command = ToolCommand.FromShellParams("docker", arguments, userPath,
            "running bounded model checker...", AppState.Loading, true, x =>
            {
                _outputService.WriteLine(x);
                return true;
            }, x =>
            {
                _logger.Warning(x);
                return true;
            });

        var (success, _) = await _toolExecutionDispatcherService.ExecuteAsync(command);
        return success;
    }
}