using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
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
    
    public OneWareUiExtension? TestBenchToolbarTopUiExtension { get; } 

    public PslSimulator(PslService pslService)
    {
        _pslService = pslService;
        
        TestBenchToolbarTopUiExtension = new OneWareUiExtension(x =>
        {
            if (x is TestBenchContext tb)
            {
                return new PslSimulatorToolbarView()
                {
                    DataContext = new PslSimulatorToolbarViewModel(tb)
                };
            }
            return null;
        });
    }
    
    public Task<bool> SimulateAsync(string fullPath)
    {
        if (Path.GetExtension(fullPath) is ".vhd" or ".vhdl") return _pslService.GenerateAndSimulateSbyAsync(fullPath);
        if (Path.GetExtension(fullPath) is ".sby") return _pslService.RunSbyAsync(fullPath);
        return Task.FromResult(false);
    }
}