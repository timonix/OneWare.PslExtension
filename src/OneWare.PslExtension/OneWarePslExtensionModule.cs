using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Extensions;
using OneWare.Essentials.Helpers;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.Essentials.ToolEngine;
using OneWare.PslExtension.Services;
using OneWare.UniversalFpgaProjectSystem.Services;

namespace OneWare.PslExtension;

public class OneWarePslExtensionModule : OneWareModuleBase
{

    public override void RegisterServices(IServiceCollection containerRegistry)
    {
        containerRegistry.AddSingleton<PslService>();
        containerRegistry.AddSingleton<GracefulStopStrategy>();
        containerRegistry.AddSingleton<PslAiFunctions>();
    }

    public override void Initialize(IServiceProvider serviceProvider)
    {

        var pslService = serviceProvider.Resolve<PslService>();
        serviceProvider.Resolve<FpgaService>().RegisterSimulator<PslSimulator>();

        //SBY runs inside docker or natively from OSS CAD Suite, tools have to be known to the tool engine before they can be executed
        var toolService = serviceProvider.Resolve<IToolService>();
        if (toolService.GetAllTools().All(x => x.Key != "docker"))
            toolService.Register(new ToolContext("docker", "Container runtime used to run SymbiYosys", "docker"));
        if (toolService.GetAllTools().All(x => x.Key != "sby"))
            toolService.Register(new ToolContext("sby", "SymbiYosys formal verification", "sby"));
        toolService.RegisterStrategy(serviceProvider.Resolve<GracefulStopStrategy>(), ["sby", "docker"]);

        serviceProvider.Resolve<ISettingsService>().RegisterSetting("Tools", "PSL Extension", PslService.SbyRunnerSetting,
            new ComboBoxSetting("SBY Runner", PslService.SbyRunnerDocker,
                new object[] { PslService.SbyRunnerDocker, PslService.SbyRunnerNative })
            {
                HoverDescription = "Docker: run SymbiYosys in the hdlc/formal container. " +
                                   "Native: run it from the OSS CAD Suite installed in OneWare " +
                                   "(on Windows it also needs the matching GHDL plugin, from the package manager under Binaries)."
            });

        var aiFunctionProvider = serviceProvider.Resolve<IAiFunctionProvider>();
        foreach (var function in serviceProvider.Resolve<PslAiFunctions>().CreateFunctions())
            aiFunctionProvider.RegisterFunction(function);

        //Windows OSS CAD Suite lacks the GHDL plugin, users install the matching build from the package manager.
        //Linux and macOS OSS CAD Suite include it, so it is not offered there
        if (PlatformHelper.Platform == PlatformId.WinX64)
            serviceProvider.Resolve<IPackageService>().RegisterPackage(GhdlPluginPackage.Package);

        //This example adds a context menu for .vhd files
        serviceProvider.Resolve<IProjectExplorerService>().RegisterConstructContextMenu((selected, menuItems) =>
        {
            if (selected is [IProjectFile {Extension: ".vhd" or ".vhdl"} vhdFile])
            {
                menuItems.Add(new MenuItemModel("PSL")
                {
                    Header = "PSL",
                    Items =
                    [
                        new MenuItemModel("GenerateSby")
                        {
                            Header = "Generate SBY file",
                            Command = new AsyncRelayCommand(() => pslService.GenerateSbyAsync(vhdFile.FullPath))
                        },

                        new MenuItemModel("SimulateWithPSL")
                        {

                            Header = "Generate → simulate SBY file",
                            Command = new AsyncRelayCommand(() => pslService.GenerateAndSimulateSbyAsync(vhdFile.FullPath)),
                            Icon = new IconModel("Material.Pulse"),
                        }
                    ]
                });
            }else if (selected is [IProjectFile { Extension: ".sby"} sbyFile])
            {
                menuItems.Add(new MenuItemModel("SymbiYosys")
                {
                    Header = "SBY",
                    Items =
                    [
                        new MenuItemModel("RunSby")
                        {
                            Header = "Run SBY",
                            Command = new AsyncRelayCommand(() => pslService.RunSbyAsync(sbyFile.FullPath)),
                            Icon = new IconModel("Material.Play"),
                        }
                    ]
                });
            }
        });
    }
}
