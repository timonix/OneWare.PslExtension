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

    private string _path = string.Empty;

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
    

    public async Task<bool> GenerateSbyAsync(IProjectFile file)
    {
        return true;
    }
    
    public async Task<bool> GenerateAndSimulateSbyAsync(IProjectFile file)
    {
        return true;
    }
    
    static string GetRelativePath(string fromPath, string toPath)
    {
        Uri fromUri = new Uri(fromPath.EndsWith("\\") ? fromPath : fromPath + "\\");
        Uri toUri = new Uri(toPath);
        
        Uri relativeUri = fromUri.MakeRelativeUri(toUri);
        string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
        return relativePath;
        return relativePath.Replace('/', '\\'); // Convert to Windows-style path
    }
    
    public async Task<bool> RunSbyAsync(IProjectFile file)
    {

        var userPath = file.Root.FullPath;
        var containerWorkspace = "/workspace";
        var containerImage = "hdlc/formal";
        var yosysCommand = "yosys -m ghdl";
        var sbyFileName = file.Name;
        var relativePath = GetRelativePath(file.Root.FullPath, file.TopFolder.FullPath);
        
        List<string> arguments = new List<string>
        {
            "run",// "--rm",
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
        return run.success;
    }
}