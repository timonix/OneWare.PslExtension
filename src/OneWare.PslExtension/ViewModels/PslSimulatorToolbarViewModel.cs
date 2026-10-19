using CommunityToolkit.Mvvm.ComponentModel;
using OneWare.UniversalFpgaProjectSystem.Context;

namespace OneWare.PslExtension.ViewModels;

public class PslSimulatorToolbarViewModel(TestBenchContext context) : ObservableObject
{
    public string SimulationStopTime
    {
        get => context.GetBenchProperty(nameof(SimulationStopTime)) ?? "";
        set
        {
            if(string.IsNullOrWhiteSpace(value)) context.RemoveBenchProperty(nameof(SimulationStopTime));
            else context.SetBenchProperty(nameof(SimulationStopTime), value);
            OnPropertyChanged();
        }
    }

    public string SearchDepth
    {
        get => context.GetBenchProperty(nameof(SearchDepth))?? "25";
        set
        {
            if(string.IsNullOrWhiteSpace(value)) context.RemoveBenchProperty(nameof(SearchDepth));
            else context.SetBenchProperty(nameof(SearchDepth), value);
            OnPropertyChanged();
        }
    }
    
    public string[] AvailableBmcModes => ["bmc","cover","prove"];
    public string BmcMode
    {
        get => context.GetBenchProperty(nameof(BmcMode)) ?? "bmc";
        set
        {
            context.SetBenchProperty(nameof(BmcMode), value);
            OnPropertyChanged();
        }
    }
    
    public string[] AvailableBmcEngines => ["smtbmc"];
    public string BmcEngine
    {
        get => context.GetBenchProperty(nameof(BmcEngine)) ?? "smtbmc";
        set
        {
            context.SetBenchProperty(nameof(BmcEngine), value);
            OnPropertyChanged();
        }
    }
    
    public string[] AvailableSolvers => ["z3"];
    public string Solver
    {
        get => context.GetBenchProperty(nameof(Solver)) ?? "z3";
        set
        {
            context.SetBenchProperty(nameof(Solver), value);
            OnPropertyChanged();
        }
    }
    
    public string AdditionalOptions
    {
        get => context.GetBenchProperty(nameof(AdditionalOptions)) ?? "";
        set
        {
            if(string.IsNullOrWhiteSpace(value)) context.RemoveBenchProperty(nameof(AdditionalOptions));
            else context.SetBenchProperty(nameof(AdditionalOptions), value);
            OnPropertyChanged();
        }
    }

}