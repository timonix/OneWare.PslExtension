using OneWare.Essentials.Models;
using OneWare.Essentials.PackageManager;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;
using OneWare.PslExtension.Services;
using Prism.Ioc;
using Prism.Modularity;

namespace OneWare.PslExtension;

public class OneWarePslExtensionModule : IModule
{
    public static readonly Package PslPackage = new()
    {
        Category = "Plugins",
        Id = "psl",
        Name = "PSL",
        Type = "NativeTool",
        Description = "Formal verification tool",
        License = "MIT License",
        IconUrl = "https://raw.githubusercontent.com/ghdl/ghdl/master/logo/icon.png"
        
    };
    
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<PslService>();
    }

    public void OnInitialized(IContainerProvider containerProvider)
    {
        containerProvider.Resolve<IPackageService>().RegisterPackage(PslPackage);
        
        //This example adds a context menu for .vhd files
        containerProvider.Resolve<IProjectExplorerService>().RegisterConstructContextMenu((selected, menuItems) =>
        {
            if (selected is [IProjectFile {Extension: ".vhd"} vhdFile])
            {
                menuItems.Add(new MenuItemViewModel("Hello World")
                {
                    Header = $"Hello World {vhdFile.Header}"
                });
            }
        });
    }
}