using System;
using System.IO;
using System.Linq;
using OneWare.PslExtension.Services;
using Xunit;

namespace OneWare.PslExtension.UnitTests;

public class SbyTracesTests
{
    [Fact]
    public void ReadsTaskNamesLikeSby()
    {
        const string sby = """
                           [tasks]
                           # comment
                           bmc
                           cover tag_a tag_b
                           prove_a prove_b : group

                           [options]
                           bmc: mode bmc
                           """;

        Assert.Equal(["bmc", "cover", "prove_a", "prove_b"], SbyTraces.ReadTaskNames(sby));
        Assert.Empty(SbyTraces.ReadTaskNames("[options]\nmode bmc\n"));
    }

    [Fact]
    public void SbyFilesWithoutTasksGetTraceCopiesOfTheirOwn()
    {
        Assert.Equal("counter_fv_bmc", SbyTraces.GetTaskFolderName("counter_fv", "bmc"));
        Assert.Equal("counter_fv_bmc", SbyTraces.GetCopyBaseName("counter_fv", "bmc"));

        // SBY runs a .sby file without tasks in the --prefix folder itself, the copy must not become counter_fv.vcd,
        // the name a simulator gives the waveform of a test bench counter_fv
        Assert.Equal("counter_fv", SbyTraces.GetTaskFolderName("counter_fv", null));
        Assert.Equal("counter_fv_trace", SbyTraces.GetCopyBaseName("counter_fv", null));
    }

    [Fact]
    public void NamesCopiesAfterSbyFileTaskAndTrace()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(folder.FullName, "engine_0"));
            Directory.CreateDirectory(Path.Combine(folder.FullName, "engine_1"));
            foreach (var file in new[] { "engine_0/trace.vcd", "engine_0/trace0.vcd", "engine_0/trace1.vcd",
                         "engine_0/trace_induct.vcd", "engine_0/trace0_tb.v", "engine_0/other.vcd",
                         "engine_1/trace.vcd" })
                File.WriteAllText(Path.Combine(folder.FullName, file), "");

            var names = SbyTraces.FindTaskTraces(folder.FullName, "counter_fv_bmc").Select(x => x.CopyName);

            Assert.Equal(["counter_fv_bmc.vcd", "counter_fv_bmc_0.vcd", "counter_fv_bmc_1.vcd",
                "counter_fv_bmc_induct.vcd", "counter_fv_bmc_engine1.vcd"], names);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void FindsOnlyTheCopiesOfOneTask()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            foreach (var file in new[] { "counter_fv_cover_0.vcd", "counter_fv_cover_1.vcd", "counter_fv_cover_induct.vcd",
                         "counter_fv_cover_engine1.vcd", "counter_fv_cover_0.vcdconf", "counter_fv_bmc.vcd",
                         "counter_fv_cover_extra.vcd" })
                File.WriteAllText(Path.Combine(folder.FullName, file), "");

            var copies = SbyTraces.FindCopies(folder.FullName, "counter_fv_cover").Select(Path.GetFileName);

            Assert.Equal(["counter_fv_cover_0.vcd", "counter_fv_cover_1.vcd", "counter_fv_cover_engine1.vcd",
                "counter_fv_cover_induct.vcd"], copies);
        }
        finally
        {
            folder.Delete(true);
        }
    }
}
