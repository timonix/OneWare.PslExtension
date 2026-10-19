using System.Text.RegularExpressions;

namespace OneWare.PslExtension.Services;

/// <summary>
/// The traces of a .sby file: copies of the VCD files SBY writes into its task folders, kept next to the .sby file
/// and named &lt;sby name&gt;_&lt;task&gt;.vcd, like simulators keep their waveform next to the test bench.
/// </summary>
internal static class SbyTraces
{
    /// <summary>
    /// The task names of a .sby file, or an empty list when it has none. Follows SBY: a line of [tasks] is a task
    /// name followed by tags, or task names, a colon and tags.
    /// </summary>
    public static IReadOnlyList<string> ReadTaskNames(string sbyText)
    {
        var tasks = new List<string>();
        var inTasks = false;

        foreach (var rawLine in sbyText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inTasks = line == "[tasks]";
                continue;
            }

            if (!inTasks || line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split(':');
            var names = parts[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            tasks.AddRange(parts.Length == 1 ? names.Take(1) : names);
        }

        return tasks.Distinct().ToList();
    }

    /// <summary>
    /// The folder SBY runs a task in, below the --prefix folder.
    /// </summary>
    public static string GetTaskFolderName(string sbyName, string? task) => task == null ? sbyName : $"{sbyName}_{task}";

    /// <summary>
    /// The name the trace copies of a task start with. A .sby file without tasks gets _trace, so its copy can't
    /// replace the &lt;top&gt;.vcd waveform a simulator writes for a test bench of the same name.
    /// </summary>
    public static string GetCopyBaseName(string sbyName, string? task) => $"{sbyName}_{task ?? "trace"}";

    /// <summary>
    /// The traces SBY wrote into a task folder, with the names of their copies: trace.vcd becomes &lt;base&gt;.vcd,
    /// trace0.vcd &lt;base&gt;_0.vcd (cover mode) and trace_induct.vcd &lt;base&gt;_induct.vcd (prove mode). Traces of
    /// engines other than engine_0 also get _engine&lt;n&gt;. Other VCD files are not copied.
    /// </summary>
    public static IReadOnlyList<(string Source, string CopyName)> FindTaskTraces(string taskFolder, string baseName)
    {
        if (!Directory.Exists(taskFolder)) return [];

        return Directory.GetDirectories(taskFolder, "engine_*")
            .Select(x => (Folder: x, Index: int.TryParse(Path.GetFileName(x)["engine_".Length..], out var i) ? i : -1))
            .Where(x => x.Index >= 0)
            .OrderBy(x => x.Index)
            .SelectMany(engine => Directory.GetFiles(engine.Folder, "trace*.vcd")
                .Select(x => (Source: x, Match: TracePattern.Match(Path.GetFileName(x))))
                .Where(x => x.Match.Success)
                .OrderBy(x => x.Source, StringComparer.Ordinal)
                .Select(x => (x.Source, $"{baseName}{GetSuffix(x.Match.Groups[1].Value)}" +
                                        $"{(engine.Index > 0 ? $"_engine{engine.Index}" : "")}.vcd")))
            .ToList();
    }

    /// <summary>
    /// The trace copies of a task in <paramref name="folder"/>, the ones <see cref="FindTaskTraces"/> names.
    /// Other files, like the .vcdconf files OneWare's waveform viewer saves next to them, are left out.
    /// </summary>
    public static IReadOnlyList<string> FindCopies(string folder, string baseName)
    {
        if (!Directory.Exists(folder)) return [];

        var pattern = new Regex($@"^{Regex.Escape(baseName)}(_\d+|_induct)?(_engine\d+)?\.vcd$",
            RegexOptions.IgnoreCase);
        return Directory.GetFiles(folder, "*.vcd")
            .Where(x => pattern.IsMatch(Path.GetFileName(x)))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // trace.vcd, trace<n>.vcd in cover mode, trace_induct.vcd for the induction step in prove mode
    private static readonly Regex TracePattern = new(@"^trace(\d*|_induct)\.vcd$");

    private static string GetSuffix(string traceKind) => traceKind.Length == 0 || traceKind.StartsWith('_')
        ? traceKind
        : $"_{traceKind}";
}
