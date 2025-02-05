using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using OneWare.Essentials.Models;
using OneWare.Essentials.PackageManager;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;
using OneWare.PslExtension.Services;
using OneWare.UniversalFpgaProjectSystem.Services;
using Prism.Ioc;
using Prism.Modularity;

namespace OneWare.PslExtension;

public class OneWarePslExtensionModule : IModule
{
    
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<PslService>();
    }

    public void OnInitialized(IContainerProvider containerProvider)
    {
        
        var pslService = containerProvider.Resolve<PslService>();
        containerProvider.Resolve<FpgaService>().RegisterSimulator<PslSimulator>();
        
        //This example adds a context menu for .vhd files
        containerProvider.Resolve<IProjectExplorerService>().RegisterConstructContextMenu((selected, menuItems) =>
        {
            if (selected is [IProjectFile {Extension: ".vhd" or ".vhdl"} vhdFile])
            {
                menuItems.Add(new MenuItemViewModel("GHDL")
                {
                    Header = "PSL",
                    Items =
                    [
                        new MenuItemViewModel("GenerateSby")
                        {
                            Header = "Generate SBY file",
                            Command = new AsyncRelayCommand(() => pslService.GenerateSbyAsync(vhdFile))
                        },

                        new MenuItemViewModel("SimulateWithPSL")
                        {
                            
                            Header = "Generate \u2192 simulate SBY file",
                            Command = new AsyncRelayCommand(() => pslService.GenerateAndSimulateSbyAsync(vhdFile)),
                            IconObservable = Application.Current!.GetResourceObservable("Material.Pulse"),
                        }
                    ]
                });
            }else if (selected is [IProjectFile { Extension: ".sby"} sbyFile])
            {
                menuItems.Add(new MenuItemViewModel("SymbiYosys")
                {
                    Header = "SBY",
                    Items =
                    [
                        new MenuItemViewModel("RunSby")
                        {
                            Header = "Run SBY",
                            Command = new AsyncRelayCommand(() => pslService.RunSbyAsync(sbyFile)),
                            IconObservable = Application.Current!.GetResourceObservable("Material.Play"),
                        }
                    ]
                });
            }
        });
    }
}