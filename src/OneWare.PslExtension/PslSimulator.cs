using OneWare.Essentials.Models;
using OneWare.PslExtension.Services;
using OneWare.PslExtension.ViewModels;
using OneWare.PslExtension.Views;
using OneWare.UniversalFpgaProjectSystem.Context;
using OneWare.UniversalFpgaProjectSystem.Services;

namespace OneWare.PslExtension;

public class PslSimulator : IFpgaSimulator
{
    private readonly PslService _pslService;
    public string Name => "PSL";
    
    public UiExtension? TestBenchToolbarTopUiExtension { get; } 

    public PslSimulator(PslService pslService)
    {
        _pslService = pslService;
        
        TestBenchToolbarTopUiExtension = new UiExtension(x =>
        {
            if (x is TestBenchContext tb)
            {
                return new PslSimulatorToolbarView()
                {
                    DataContext = new PslSimulatorToolbarViewModel(tb, this)
                };
            }
            return null;
        });
    }
    
    public Task<bool> SimulateAsync(IFile file)
    {
        if (file is IProjectFile { Extension: ".vhd" or "vhdl" } vhdFile) return _pslService.GenerateAndSimulateSbyAsync(vhdFile);
        if (file is IProjectFile { Extension: ".sby" } sbyFile) return _pslService.RunSbyAsync(sbyFile);
        return Task.FromResult(false);
    }
}