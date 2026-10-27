using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Extensions;
using OneWare.Essentials.Helpers;
using OneWare.Essentials.Models;
using OneWare.Essentials.PackageManager.Compatibility;
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

    public const string SbyRunnerSetting = "PslExtension_SbyRunner";
    public const string SbyRunnerDocker = "Docker";
    public const string SbyRunnerNative = "Native (OSS CAD Suite)";

    // Registered by Studio's OSS CAD Suite integration, set when OSS CAD Suite is installed from the package manager
    private const string OssCadSuitePathSetting = "OssCadSuite_Path";

    private readonly ILogger _logger;
    private readonly IMainDockService _dockService;
    private readonly IPackageService _packageService;
    private readonly IToolExecutionDispatcherService _toolExecutionDispatcherService;
    private readonly IEnvironmentService _environmentService;
    private readonly IOutputService _outputService;
    private readonly ISettingsService _settingsService;
    private readonly IProjectExplorerService _projectExplorerService;
    private readonly IPaths _paths;

    public PslService(ILogger logger, IMainDockService dockService, ISettingsService settingsService, IPackageService packageService, IEnvironmentService environmentService,
        IOutputService outputService, IProjectExplorerService projectExplorerService, IToolExecutionDispatcherService toolExecutionDispatcherService, IPaths paths)
    {
        _logger = logger;
        _dockService = dockService;
        _packageService = packageService;
        _toolExecutionDispatcherService = toolExecutionDispatcherService;
        _environmentService = environmentService;
        _outputService = outputService;
        _settingsService = settingsService;
        _projectExplorerService = projectExplorerService;
        _paths = paths;

    }
    
    internal static string GetSbyPath(string vhdlFullPath)
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

            var relativeSbyPath = Path.GetRelativePath(root.RootFolderPath, outputFilePath);
            if (!root.IsPathIncluded(relativeSbyPath))
            {
                root.IncludePath(relativeSbyPath);
                await _projectExplorerService.SaveProjectAsync(root);
            }
            if (root.GetLoadedEntry(relativeSbyPath) == null) root.AddFile(relativeSbyPath);

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
    private async Task<bool> CheckDockerAsync(Action<string>? output)
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
                ReportRunError($"Docker did not respond within {DockerCheckTimeoutSeconds} seconds. " +
                               "Make sure Docker (e.g. Docker Desktop or Rancher Desktop) has finished starting and try again.", output);
                return false;
            }

            await stdoutTask;
            if (process.ExitCode == 0) return true;

            var details = (await stderrTask).Trim().Split('\n')[0].Trim();
            ReportRunError("Docker is installed, but its engine is not running. " +
                           "Start Docker Desktop or Rancher Desktop (or the docker service) and try again." +
                           (details.Length > 0 ? $"\nDocker: {details}" : ""), output);
            return false;
        }
        catch (Win32Exception)
        {
            ReportRunError("Docker was not found. SymbiYosys runs inside the hdlc/formal Docker container, " +
                           "so install Docker (e.g. Docker Desktop or Rancher Desktop) and make sure 'docker' is on your PATH.", output);
            return false;
        }
    }

    // SBY starts its lines with the full path of the task folder, the folder name alone tells which task it is
    private static readonly Regex SbyLineTaskFolder = new(@"^(SBY \d+:\d+:\d+ \[)[^\]]*[\\/]([^\\/\]]+\])");

    internal static string ShortenSbyLine(string line) => SbyLineTaskFolder.Replace(line, "$1$2");

    private void ReportRunError(string message, Action<string>? output)
    {
        _logger.Error(message, null, true, true);
        output?.Invoke(message);
    }

    /// <param name="output">Also gets SBY's output, and the reason when SBY can't be started.</param>
    public async Task<bool> RunSbyAsync(string fullPath, Action<string>? output = null)
    {
        if (_projectExplorerService.GetRootFromFile(fullPath) is not { } root) return false;

        var pslFolder = Path.Combine(root.RootFolderPath, BuildFolderName, "psl");
        var tasks = GetTasks(fullPath);
        var logTimesBefore = tasks.Select(x => GetLogTime(pslFolder, x.Folder)).ToList();

        var success = _settingsService.GetSettingValue<string>(SbyRunnerSetting) == SbyRunnerNative
            ? await RunSbyNativeAsync(root.RootFolderPath, fullPath, output)
            : await RunSbyDockerAsync(root.RootFolderPath, fullPath, output);

        // SBY rewrites the log of each task it runs, the traces of the other tasks stay as they are
        var ranTasks = tasks.Where((x, i) => GetLogTime(pslFolder, x.Folder) is { } time && time != logTimesBefore[i]);
        CopyTraces(pslFolder, Path.GetDirectoryName(fullPath)!, ranTasks);

        return success;
    }

    /// <summary>
    /// Copies the traces of the tasks next to the .sby file, replacing the copies of their previous run.
    /// </summary>
    private void CopyTraces(string pslFolder, string sbyFolder, IEnumerable<(string Folder, string CopyBaseName)> tasks)
    {
        try
        {
            foreach (var (folder, copyBaseName) in tasks)
            {
                foreach (var oldCopy in SbyTraces.FindCopies(sbyFolder, copyBaseName)) File.Delete(oldCopy);

                foreach (var (source, copyName) in SbyTraces.FindTaskTraces(Path.Combine(pslFolder, folder), copyBaseName))
                    File.Copy(source, Path.Combine(sbyFolder, copyName), true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"The traces could not be copied to {sbyFolder}: {e.Message}", e);
        }
    }

    private static List<(string Folder, string CopyBaseName)> GetTasks(string sbyFullPath)
    {
        var sbyName = Path.GetFileNameWithoutExtension(sbyFullPath);
        var tasks = File.Exists(sbyFullPath) ? SbyTraces.ReadTaskNames(File.ReadAllText(sbyFullPath)) : [];
        return (tasks.Count == 0 ? [null] : tasks.Cast<string?>())
            .Select(x => (SbyTraces.GetTaskFolderName(sbyName, x), SbyTraces.GetCopyBaseName(sbyName, x)))
            .ToList();
    }

    private static DateTime? GetLogTime(string pslFolder, string taskFolder)
    {
        var log = Path.Combine(pslFolder, taskFolder, "logfile.txt");
        return File.Exists(log) ? File.GetLastWriteTimeUtc(log) : null;
    }

    private async Task<bool> RunSbyNativeAsync(string userPath, string fullPath, Action<string>? output)
    {
        var ossPath = GetOssCadSuitePath(output);
        if (ossPath == null) return false;

        var sbyPath = Path.Combine(ossPath, "bin", $"sby{PlatformHelper.ExecutableExtension}");
        if (!File.Exists(sbyPath))
        {
            ReportRunError($"SymbiYosys was not found in OSS CAD Suite ({sbyPath}).", output);
            return false;
        }

        var yosysCommand = await GetNativeYosysCommandAsync(ossPath, output);
        if (yosysCommand == null) return false;

        // SBY output goes to <project>/build/psl/<name>_<task>, so its copies of the sources stay out of the project
        var workdirPrefix = Path.Combine(userPath, BuildFolderName, "psl", Path.GetFileNameWithoutExtension(fullPath));

        var command = _toolExecutionDispatcherService.CreateToolCommandBuilder("sby")
            .WithExecutable(sbyPath)
            .WithWorkingDirectory(Path.GetDirectoryName(fullPath)!)
            .WithStatus("running bounded model checker...")
            .WithTimer(true)
            .WithOutputHandler(x =>
            {
                var line = ShortenSbyLine(x);
                _outputService.WriteLine(line);
                output?.Invoke(line);
                return true;
            })
            .WithErrorHandler(x =>
            {
                var line = ShortenSbyLine(x);
                _logger.Warning(line);
                output?.Invoke(line);
                return true;
            })
            .ForceStrategy(GracefulStopStrategy.StrategyKey)
            .Add("--yosys", yosysCommand, "--prefix", workdirPrefix, "-f", Path.GetFileName(fullPath))
            .Build();

        var (success, _) = await _toolExecutionDispatcherService.ExecuteAsync(command);
        return success;
    }

    private string? GetOssCadSuitePath(Action<string>? output)
    {
        var ossPath = _settingsService.HasSetting(OssCadSuitePathSetting)
            ? _settingsService.GetSettingValue<string>(OssCadSuitePathSetting)
            : null;

        if (string.IsNullOrWhiteSpace(ossPath) || !Directory.Exists(ossPath))
        {
            ReportRunError("OSS CAD Suite was not found. Install it from the package manager " +
                           "(or set its path in Settings → Tools → OSS Cad Suite), or switch the SBY runner to Docker " +
                           "in Settings → Tools → PSL Extension.", output);
            return null;
        }

        return ossPath;
    }

    /// <summary>
    /// The yosys command SBY should use, with the GHDL plugin loaded. The Linux and macOS OSS CAD Suite
    /// include the plugin, on Windows the build matching the installed OSS CAD Suite is downloaded.
    /// </summary>
    private async Task<string?> GetNativeYosysCommandAsync(string ossPath, Action<string>? output)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (File.Exists(Path.Combine(ossPath, "share", "yosys", "plugins", "ghdl.so"))) return "yosys -m ghdl";

            ReportRunError($"The OSS CAD Suite in {ossPath} does not include the GHDL plugin for yosys, " +
                           "which SymbiYosys needs to read VHDL. Install OSS CAD Suite from the package manager, " +
                           "or switch the SBY runner to Docker in Settings → Tools → PSL Extension.", output);
            return null;
        }

        if (PlatformHelper.Platform != PlatformId.WinX64)
        {
            ReportRunError("Running SymbiYosys natively is only supported on x64 Windows. " +
                           "Switch the SBY runner to Docker in Settings → Tools → PSL Extension.", output);
            return null;
        }

        var pluginPath = await GetWindowsGhdlPluginAsync(ossPath, output);
        // yosys runs through the shell, so the plugin path is quoted in case it contains spaces
        return pluginPath == null ? null : $"yosys -m \"{pluginPath.Replace('\\', '/')}\"";
    }

    private async Task<string?> GetWindowsGhdlPluginAsync(string ossPath, Action<string>? output)
    {
        var versionFile = Path.Combine(ossPath, "VERSION");
        if (!File.Exists(versionFile))
        {
            ReportRunError($"The OSS CAD Suite in {ossPath} has no VERSION file, so the matching GHDL plugin " +
                           "can't be picked. Install OSS CAD Suite from the package manager, " +
                           "or switch the SBY runner to Docker in Settings → Tools → PSL Extension.", output);
            return null;
        }

        var ossVersion = (await File.ReadAllTextAsync(versionFile)).Trim();
        var version = GhdlPluginPackage.FindVersion(ossVersion);
        if (version == null)
        {
            var supported = string.Join(", ", GhdlPluginPackage.Package.Versions!.Select(x => x.Version));
            ReportRunError($"There is no GHDL plugin for OSS CAD Suite {GhdlPluginPackage.ToPackageVersion(ossVersion)} yet " +
                           $"(available for: {supported}). Install one of those OSS CAD Suite versions from the package manager, " +
                           "or switch the SBY runner to Docker in Settings → Tools → PSL Extension.", output);
            return null;
        }

        var pluginPath = Path.Combine(_paths.NativeToolsDirectory, GhdlPluginPackage.Id, "ghdl.so");
        var installedVersion = _packageService.Packages.GetValueOrDefault(GhdlPluginPackage.Id)?.InstalledVersion?.Version;
        if (installedVersion == version.Version && File.Exists(pluginPath)) return pluginPath;

        // The plugin is installed by the user from the package manager, it is not downloaded on its own
        var problem = installedVersion == null || !File.Exists(pluginPath)
            ? "is not installed"
            : $"installed is for OSS CAD Suite {installedVersion}";
        ReportRunError($"Running SymbiYosys natively on Windows needs the GHDL plugin for OSS CAD Suite {version.Version}, " +
                       $"the one {problem}. Install version {version.Version} of \"{GhdlPluginPackage.Package.Name}\" " +
                       "from the package manager (Binaries), or switch the SBY runner to Docker in Settings → Tools → PSL Extension.", output);
        return null;
    }

    private async Task<bool> RunSbyDockerAsync(string userPath, string fullPath, Action<string>? output)
    {
        if (!await CheckDockerAsync(output)) return false;

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

        var command = _toolExecutionDispatcherService.CreateToolCommandBuilder("docker")
            .WithExecutable("docker")
            .WithWorkingDirectory(userPath)
            .WithStatus("running bounded model checker...")
            .WithTimer(true)
            .WithOutputHandler(x =>
            {
                var line = ShortenSbyLine(x);
                _outputService.WriteLine(line);
                output?.Invoke(line);
                return true;
            })
            .WithErrorHandler(x =>
            {
                var line = ShortenSbyLine(x);
                _logger.Warning(line);
                output?.Invoke(line);
                return true;
            })
            .ForceStrategy(GracefulStopStrategy.StrategyKey)
            .AddRange(arguments)
            .Build();

        var (success, _) = await _toolExecutionDispatcherService.ExecuteAsync(command);
        return success;
    }
}