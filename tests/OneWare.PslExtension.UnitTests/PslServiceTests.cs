using OneWare.PslExtension.Services;
using Xunit;

namespace OneWare.PslExtension.UnitTests;

public class PslServiceTests
{
    [Theory]
    // Native: SBY gets an absolute Windows --prefix
    [InlineData(@"SBY 14:16:44 [C:\Users\me\git\eth\build\psl\eth_crc32_fv_bmc] engine_0: ##   0:03:36  Checking assertions in step 23..",
        "SBY 14:16:44 [eth_crc32_fv_bmc] engine_0: ##   0:03:36  Checking assertions in step 23..")]
    // Docker: the project is mounted at /workspace
    [InlineData("SBY 11:37:40 [/workspace/build/psl/counter_fv_cover] summary: Elapsed clock time [H:MM:SS (secs)]: 0:00:00 (0)",
        "SBY 11:37:40 [counter_fv_cover] summary: Elapsed clock time [H:MM:SS (secs)]: 0:00:00 (0)")]
    // Paths later in the line are SBY's text and stay as they are
    [InlineData(@"SBY 13:31:02 [C:\p\build\psl\counter_fv_bmc] summary: counterexample trace: C:\p\build\psl\counter_fv_bmc\engine_0\trace.vcd",
        @"SBY 13:31:02 [counter_fv_bmc] summary: counterexample trace: C:\p\build\psl\counter_fv_bmc\engine_0\trace.vcd")]
    [InlineData("SBY 13:31:02 The following tasks failed: ['bmc']", "SBY 13:31:02 The following tasks failed: ['bmc']")]
    [InlineData("SBY 13:31:02 [counter_fv_bmc] DONE (FAIL, rc=2)", "SBY 13:31:02 [counter_fv_bmc] DONE (FAIL, rc=2)")]
    [InlineData("ERROR: Task name 'x' didn't match any lines in [tasks].", "ERROR: Task name 'x' didn't match any lines in [tasks].")]
    public void ShortensTheTaskFolderOfSbyLines(string line, string expected)
    {
        Assert.Equal(expected, PslService.ShortenSbyLine(line));
    }
}
