using System.Collections.Concurrent;
using System.ComponentModel;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;

namespace OneWare.PslExtension.Services;

public class PslAiFunctions(PslService pslService, IProjectExplorerService projectExplorerService)
{
    internal const int MaxOutputLines = 200;

    public IEnumerable<IOneWareAiFunction> CreateFunctions()
    {
        yield return new OneWareAiFunction
        {
            Name = "psl_run_sby",
            FriendlyName = "Run SBY",
            DetailExtractor = x => x.TryGetValue("path", out var path) ? Path.GetFileName(path?.ToString()) : null,
            Description = "Runs SymbiYosys (formal verification of PSL properties) on a .sby file and returns its " +
                          "output, ending with the PASS/FAIL result of each task. SBY runs in Docker or from " +
                          "OSS CAD Suite, as set in Settings → Tools → PSL Extension. The .sby file must belong " +
                          "to a project open in OneWare; its results are written to <project>/build/psl.",
            Handler = (Func<string, Task<string>>)RunSbyAsync,
            ConfirmationCheck = _ => "Run SymbiYosys on this .sby file? This runs SBY in Docker or from OSS CAD Suite.",
            RunOnUiThread = true
        };
    }

    private async Task<string> RunSbyAsync(
        [Description("Path of the .sby file, absolute or relative to the root of the active project.")]
        string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: pass the path of a .sby file.";

        var fullPath = ResolvePath(path.Trim());
        if (fullPath == null)
            return $"Error: '{path}' is relative, but no project is active in OneWare. Pass an absolute path.";
        if (!string.Equals(Path.GetExtension(fullPath), ".sby", StringComparison.OrdinalIgnoreCase))
            return $"Error: '{fullPath}' is not a .sby file.";
        if (!File.Exists(fullPath)) return $"Error: '{fullPath}' does not exist.";
        if (projectExplorerService.GetRootFromFile(fullPath) is not { } root)
            return $"Error: '{fullPath}' is not part of a project open in OneWare. Open its project first.";

        await projectExplorerService.SaveOpenFilesForProjectAsync(root);

        var lines = new ConcurrentQueue<string>();
        var success = await pslService.RunSbyAsync(fullPath, lines.Enqueue);

        return FormatResult(Path.GetFileName(fullPath), success, lines.ToList());
    }

    private string? ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

        var project = projectExplorerService.ActiveProject;
        return project == null ? null : Path.GetFullPath(Path.Combine(project.RootFolderPath, path));
    }

    internal static string FormatResult(string fileName, bool success, IReadOnlyList<string> lines)
    {
        string header;
        if (success)
            header = $"SBY passed for '{fileName}'.";
        // SBY ends a completed run with "DONE (PASS, rc=0)" or e.g. "DONE (FAIL, rc=2)". A failing property is a
        // result, not an error of the tool
        else if (lines.Any(x => x.Contains("DONE (")))
            header = $"SBY finished for '{fileName}', but did not pass. The result is below.";
        else
            header = $"Error: SBY could not run '{fileName}'.";

        if (lines.Count == 0) return $"{header} It produced no output.";

        var omitted = Math.Max(0, lines.Count - MaxOutputLines);
        if (omitted > 0)
            header += $"\n(first {omitted} lines omitted, the full output is in OneWare's Output panel)";

        return $"{header}\n\n{string.Join("\n", lines.Skip(omitted))}";
    }
}
