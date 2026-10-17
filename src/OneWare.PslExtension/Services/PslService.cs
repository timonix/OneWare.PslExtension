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
    
    static HashSet<string> ExtractComponents(string filePath)
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
            Console.WriteLine($"Error reading file: {ex.Message}");
        }

        return components;
    }
    static HashSet<string> ExtractEntities(string filePath)
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
            Console.WriteLine($"Error reading file: {ex.Message}");
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
        
        // Project files are relative to the project root folder
        var projectFiles = root.GetFiles()
            .Where(x => !root.IsCompileExcluded(x))
            .ToList();
        
        HashSet<string> usedFiles = new HashSet<string>();
        usedFiles.Add(Path.GetRelativePath(root.RootFolderPath, fullPath));
        
        for (int i = 0; i < 10; i++)
        {
            foreach (var f in usedFiles)
            {
                Console.WriteLine(f);
            }
            
            HashSet<string> candidates = new HashSet<string>();
            HashSet<string> next = new HashSet<string>();
            foreach (var f in usedFiles)
            {
                var fileFullPath = Path.Combine(root.RootFolderPath, f);
                candidates.UnionWith(ExtractEntities(fileFullPath));
                candidates.UnionWith(ExtractComponents(fileFullPath));
        
                next.UnionWith(projectFiles
                    .Where(x => candidates.Contains(Path.GetFileNameWithoutExtension(x))));

            }
            usedFiles.UnionWith(next);
        }
        
        var vhdlFiles = usedFiles
            .Where(x => !root.IsCompileExcluded(x))
            .Where(x => Path.GetExtension(x) is ".vhd" or ".vhdl")
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
        sb.Append("bmc: ghdl --std=08 ");
        sb.Append(string.Join(" ", vhdlFilesNamesOnly.Select(x => x)));
        sb.AppendLine($" -e {entity}");
        sb.AppendLine($"prep -top {entity}");
        sb.AppendLine();
    
        sb.AppendLine("[files]");
        foreach (var vhdlFile in vhdlFilesWithRelativePaths)
        {
            sb.AppendLine(vhdlFile.Replace("\\","/"));
        }
        
        var outputFilePath = Path.Combine(sbyDirectory, $"{entity}.sby");

        try
        {
            await File.WriteAllTextAsync(outputFilePath, sb.ToString());
            Console.WriteLine($"SBY file successfully generated at {outputFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing SBY file: {ex.Message}");
            return false;
        }
        
        return true;
    }
    
    public async Task<bool> GenerateAndSimulateSbyAsync(string fullPath)
    {
        await GenerateSbyAsync(fullPath);

        return true;
    }

    public async Task<bool> RunSbyAsync(string fullPath)
    {
        if (_projectExplorerService.GetRootFromFile(fullPath) is not { } root) return false;

        var userPath = root.RootFolderPath;
        const string containerWorkspace = "/workspace";
        const string containerImage = "hdlc/formal";
        const string yosysCommand = "yosys -m ghdl";
        var sbyFileName = Path.GetFileName(fullPath);
        
        var relativePath = Path.GetRelativePath(userPath, Path.GetDirectoryName(fullPath)!).Replace('\\', '/');

        List<string> arguments = new List<string>
        {
            "run", // "--rm",
            "-v", $"{userPath}:{containerWorkspace}",
            "-w", $"{containerWorkspace}/{relativePath}",
            containerImage,
            "sby", "--yosys", yosysCommand, "-f", sbyFileName
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