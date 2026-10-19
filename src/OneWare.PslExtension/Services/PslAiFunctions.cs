using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.AI;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.UniversalFpgaProjectSystem.Models;

namespace OneWare.PslExtension.Services;

public class PslAiFunctions(PslService pslService, IProjectExplorerService projectExplorerService)
{
    internal const int MaxOutputLines = 200;

    public IEnumerable<IOneWareAiFunction> CreateFunctions()
    {
        yield return new OneWareAiFunction
        {
            Name = "psl_generate_sby",
            FriendlyName = "Generate SBY file",
            DetailExtractor = GetFileName,
            Description = "Generates a .sby file for a VHDL file with PSL properties, like PSL → Generate SBY file " +
                          "in OneWare, and returns its path and contents. The file is written next to the VHDL " +
                          "file with the same name, replacing an existing one. It lists the VHDL files the entity " +
                          "needs, found through the project, and uses the depth, mode, engine and solver set for " +
                          "the file in OneWare. Run it with psl_run_sby.",
            Handler = (Func<string, Task<string>>)GenerateSbyAsync,
            ConfirmationCheck = ConfirmOverwrite,
            RunOnUiThread = true
        };

        yield return new OneWareAiFunction
        {
            Name = "psl_run_sby",
            FriendlyName = "Run SBY",
            DetailExtractor = GetFileName,
            Description = "Runs SymbiYosys (formal verification of PSL properties) on a .sby file and returns its " +
                          "output, ending with the PASS/FAIL result of each task. SBY runs in Docker or from " +
                          "OSS CAD Suite, as set in Settings → Tools → PSL Extension. The .sby file must belong " +
                          "to a project open in OneWare; its results are written to <project>/build/psl.",
            Handler = (Func<string, Task<string>>)RunSbyAsync,
            ConfirmationCheck = _ => "Run SymbiYosys on this .sby file? This runs SBY in Docker or from OSS CAD Suite.",
            RunOnUiThread = true
        };
    }

    private async Task<string> GenerateSbyAsync(
        [Description("Path of the .vhd or .vhdl file, absolute or relative to the root of the active project.")]
        string path)
    {
        try
        {
            var (fullPath, root) = RequireProjectFile(path, ".vhd", ".vhdl");
            if (root is not UniversalFpgaProjectRoot)
                throw new ToolInputException($"'{fullPath}' is not part of an FPGA project.");

            await projectExplorerService.SaveOpenFilesForProjectAsync(root);

            var sbyPath = PslService.GetSbyPath(fullPath);
            if (!await pslService.GenerateSbyAsync(fullPath))
                return $"Error: '{sbyPath}' could not be written. The reason is in OneWare's Output panel.";

            return $"Generated '{sbyPath}':\n\n{await File.ReadAllTextAsync(sbyPath)}";
        }
        catch (ToolInputException e)
        {
            return $"Error: {e.Message}";
        }
    }

    private async Task<string> RunSbyAsync(
        [Description("Path of the .sby file, absolute or relative to the root of the active project.")]
        string path)
    {
        try
        {
            var (fullPath, root) = RequireProjectFile(path, ".sby");

            await projectExplorerService.SaveOpenFilesForProjectAsync(root);

            var lines = new ConcurrentQueue<string>();
            var success = await pslService.RunSbyAsync(fullPath, lines.Enqueue);

            return FormatResult(Path.GetFileName(fullPath), success, lines.ToList());
        }
        catch (ToolInputException e)
        {
            return $"Error: {e.Message}";
        }
    }

    private string? ConfirmOverwrite(AIFunctionArguments arguments)
    {
        if (!arguments.TryGetValue("path", out var path) || ResolvePath(path?.ToString() ?? "") is not { } fullPath)
            return null;

        var sbyPath = PslService.GetSbyPath(fullPath);
        return File.Exists(sbyPath) ? $"Replace the existing {Path.GetFileName(sbyPath)} with a generated one?" : null;
    }

    private static string? GetFileName(AIFunctionArguments arguments)
    {
        return arguments.TryGetValue("path", out var path) ? Path.GetFileName(path?.ToString()) : null;
    }

    private (string FullPath, IProjectRoot Root) RequireProjectFile(string path, params string[] extensions)
    {
        var kind = string.Join(" or ", extensions);
        if (string.IsNullOrWhiteSpace(path)) throw new ToolInputException($"pass the path of a {kind} file.");

        var fullPath = ResolvePath(path)
                       ?? throw new ToolInputException(
                           $"'{path}' is relative, but no project is active in OneWare. Pass an absolute path.");
        if (!extensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
            throw new ToolInputException($"'{fullPath}' is not a {kind} file.");
        if (!File.Exists(fullPath)) throw new ToolInputException($"'{fullPath}' does not exist.");

        var root = projectExplorerService.GetRootFromFile(fullPath)
                   ?? throw new ToolInputException(
                       $"'{fullPath}' is not part of a project open in OneWare. Open its project first.");
        return (fullPath, root);
    }

    private string? ResolvePath(string path)
    {
        path = path.Trim();
        if (path.Length == 0) return null;
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

    private sealed class ToolInputException(string message) : Exception(message);
}
