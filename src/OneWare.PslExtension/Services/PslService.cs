using System.Text;
using System.Text.RegularExpressions;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Binding;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;
using OneWare.PslExtension.ViewModels;
using OneWare.UniversalFpgaProjectSystem.Context;
using OneWare.UniversalFpgaProjectSystem.Models;

namespace OneWare.PslExtension.Services;

public class PslService
{
    private readonly ILogger _logger;
    private readonly IDockService _dockService;
    private readonly IPackageService _packageService;
    private readonly IChildProcessService _childProcessService;
    private readonly IEnvironmentService _environmentService;
    private readonly IOutputService _outputService;
    private readonly ISettingsService _settingsService;
    private readonly IProjectExplorerService _projectExplorerService;
    
    public PslService(ILogger logger, IDockService dockService, ISettingsService settingsService, IPackageService packageService, IChildProcessService childProcessService, IEnvironmentService environmentService,
        IOutputService outputService, IProjectExplorerService projectExplorerService)
    {
        _logger = logger;
        _dockService = dockService;
        _packageService = packageService;
        _childProcessService = childProcessService;
        _environmentService = environmentService;
        _outputService = outputService;
        _settingsService = settingsService;
        _projectExplorerService = projectExplorerService;
        
    }
    
    private static string GetRelativePath(string fromPath, string toPath)
    {
        Uri fromUri = new Uri(fromPath.EndsWith("\\") ? fromPath : fromPath + "\\");
        Uri toUri = new Uri(toPath);
        
        Uri relativeUri = fromUri.MakeRelativeUri(toUri);
        string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
        return relativePath;
    }
    
    static string GetRelativeDescription(string filePath)
    {
        int depth = filePath.Count(c => c == Path.DirectorySeparatorChar);
        return string.Concat(Enumerable.Repeat("..\\", depth));
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
    
    public async Task<bool> GenerateSbyAsync(IProjectFile file)
    {   
        var context = await TestBenchContextManager.LoadContextAsync(file);
        var engine = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.BmcEngine)) ?? "smtbmc";
        var mode = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.BmcMode)) ?? "bmc";
        var solver = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.Solver)) ?? "z3";
        var depth = context.GetBenchProperty(nameof(PslSimulatorToolbarViewModel.SearchDepth)) ?? "25";
        var entity = Path.GetFileNameWithoutExtension(file.Name);
        
        if(file.Root is not UniversalFpgaProjectRoot root) return false;
        
        HashSet<IProjectFile> usedFiles = new HashSet<IProjectFile>();
        usedFiles.Add(file);
        
        for (int i = 0; i < 10; i++)
        {
            foreach (var f in usedFiles)
            {
                Console.WriteLine(f.Name);
            }
            Console.WriteLine($"----{i}-----");
            
            HashSet<string> candidates = new HashSet<string>();
            HashSet<IProjectFile> next = new HashSet<IProjectFile>();
            foreach (var f in usedFiles)
            {
                
                candidates.AddRange(ExtractEntities(f.FullPath));
                candidates.AddRange(ExtractComponents(f.FullPath));
        
                next.AddRange(root.Files
                    .Where(x => !root.CompileExcluded.Contains(x))
                    .Where(x => candidates.Contains(Path.GetFileNameWithoutExtension(x.Name))));

            }
            usedFiles.AddRange(next);
        }
        
        var relativePath = GetRelativeDescription(file.RelativePath);
        var vhdlFilesWithRelativePaths = usedFiles
            .Where(x => !root.CompileExcluded.Contains(x))
            .Where(x => x.Extension is ".vhd" or ".vhdl")
            .Select(x => $"{relativePath}{x.RelativePath}");
        
        var vhdlFilesNamesOnly = usedFiles
            .Where(x => !root.CompileExcluded.Contains(x))
            .Where(x => x.Extension is ".vhd" or ".vhdl")
            .Select(x => x.Name);
        
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
        
        var outputFilePath = $"{file.TopFolder.FullPath}/{entity}.sby";

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
    
    public async Task<bool> GenerateAndSimulateSbyAsync(IProjectFile file)
    {
        await GenerateSbyAsync(file);

        return true;
    }

    public async Task<bool> RunSbyAsync(IProjectFile file)
    {

        var userPath = file.Root.FullPath;
        const string containerWorkspace = "/workspace";
        const string containerImage = "hdlc/formal";
        const string yosysCommand = "yosys -m ghdl";
        var sbyFileName = file.Name;
        
        var relativePath = GetRelativePath(file.Root.FullPath, file.TopFolder.FullPath);

        List<string> arguments = new List<string>
        {
            "run", // "--rm",
            "-v", $"{userPath}:{containerWorkspace}",
            "-w", $"{containerWorkspace}/{relativePath}",
            containerImage,
            "sby", "--yosys", yosysCommand, "-f", sbyFileName
        };

        var run = await _childProcessService.ExecuteShellAsync("docker", arguments, file.Root!.FullPath,
            "running bounded model checker...", AppState.Loading, true, x =>
            {
                _outputService.WriteLine(x);
                return true;
            }, x =>
            {
                _logger.Warning(x);
                return true;
            });
        
        var sbyFileWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);
        var bmcSourceFolder = $"{file.TopFolder.FullPath}/{sbyFileWithoutExtension}_bmc/src";
        foreach (var filePath in Directory.GetFiles(bmcSourceFolder, "*.vhd"))
        {
            var directory = Path.GetDirectoryName(filePath);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var newFilePath = Path.Combine(directory, nameWithoutExtension + ".vhd.test");

            File.Move(filePath, newFilePath);

            Console.WriteLine($"Renamed: {filePath} -> {newFilePath}");
        }

    return run.success;
    }
}