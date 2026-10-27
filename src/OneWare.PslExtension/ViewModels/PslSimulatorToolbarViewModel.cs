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
    
    // The SBY engines and what each one runs: solvers, for abc its commands. Not every one supports every mode,
    // and not every one is installed everywhere (the hdlc/formal image has the fewest), SBY reports both
    private static readonly Dictionary<string, string[]> SolversByEngine = new()
    {
        ["smtbmc"] = ["z3", "yices", "boolector", "bitwuzla", "cvc4", "cvc5"],
        ["abc"] = ["bmc3", "sim3", "pdr"],
        ["aiger"] = ["aigbmc", "suprove", "avy", "rIC3"],
        ["btor"] = ["btormc", "pono", "rIC3"]
    };

    public string[] AvailableBmcEngines => SolversByEngine.Keys.ToArray();
    public string BmcEngine
    {
        get => context.GetBenchProperty(nameof(BmcEngine)) ?? "smtbmc";
        set
        {
            // The combo box clears its selection while its items change
            if (string.IsNullOrEmpty(value) || value == BmcEngine) return;
            context.SetBenchProperty(nameof(BmcEngine), value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailableSolvers));
            Solver = AvailableSolvers.Contains(Solver) ? Solver : AvailableSolvers[0];
        }
    }

    public string[] AvailableSolvers => SolversByEngine.GetValueOrDefault(BmcEngine, []);
    public string Solver
    {
        get => context.GetBenchProperty(nameof(Solver)) ?? AvailableSolvers.FirstOrDefault("z3");
        set
        {
            if (string.IsNullOrEmpty(value)) return;
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