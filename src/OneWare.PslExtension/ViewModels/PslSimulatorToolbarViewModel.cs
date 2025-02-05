using CommunityToolkit.Mvvm.ComponentModel;
using OneWare.UniversalFpgaProjectSystem.Context;
using OneWare.UniversalFpgaProjectSystem.Services;

namespace OneWare.PslExtension.ViewModels;

public class PslSimulatorToolbarViewModel(TestBenchContext context, IFpgaSimulator simulator) : ObservableObject
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
    
    public string[] AvailableVhdlStandards => ["test"];
    public string VhdlStandard
    {
        get => context.GetBenchProperty(nameof(VhdlStandard)) ?? "93c";
        set
        {
            context.SetBenchProperty(nameof(VhdlStandard), value);
            OnPropertyChanged();
        }
    }
    
    public string[] AvailableWaveOutputFormats => ["VCD", "GHW", "FST"];
    public string WaveOutputFormat
    {
        get => context.GetBenchProperty(nameof(WaveOutputFormat)) ?? "VCD";
        set
        {
            context.SetBenchProperty(nameof(WaveOutputFormat), value);
            OnPropertyChanged();
        }
    }
    
    public string[] AvailableAssertLevels => ["default", "warning", "error", "failure", "none"];
    public string AssertLevel
    {
        get => context.GetBenchProperty(nameof(AssertLevel)) ?? "default";
        set
        {
            if(value == "default") context.RemoveBenchProperty(nameof(AssertLevel));
            else context.SetBenchProperty(nameof(AssertLevel), value);
            OnPropertyChanged();
        }
    }
    
    public string AdditionalGhdlOptions
    {
        get => context.GetBenchProperty(nameof(AdditionalGhdlOptions)) ?? "";
        set
        {
            if(string.IsNullOrWhiteSpace(value)) context.RemoveBenchProperty(nameof(AdditionalGhdlOptions));
            else context.SetBenchProperty(nameof(AdditionalGhdlOptions), value);
            OnPropertyChanged();
        }
    }
    
    public string AdditionalGhdlSimOptions
    {
        get => context.GetBenchProperty(nameof(AdditionalGhdlSimOptions)) ?? "";
        set
        {
            if(string.IsNullOrWhiteSpace(value)) context.RemoveBenchProperty(nameof(AdditionalGhdlSimOptions));
            else context.SetBenchProperty(nameof(AdditionalGhdlSimOptions), value);
            OnPropertyChanged();
        }
    }

}