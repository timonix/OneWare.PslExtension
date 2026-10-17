using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.Essentials.Extensions;
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
    }

    public override void Initialize(IServiceProvider serviceProvider)
    {

        var pslService = serviceProvider.Resolve<PslService>();
        serviceProvider.Resolve<FpgaService>().RegisterSimulator<PslSimulator>();

        //SBY runs inside docker, which has to be known to the tool engine before it can be executed
        var toolService = serviceProvider.Resolve<IToolService>();
        if (toolService.GetAllTools().All(x => x.Key != "docker"))
            toolService.Register(new ToolContext("docker", "Container runtime used to run SymbiYosys", "docker"));

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
