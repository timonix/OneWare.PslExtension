using System.Linq;
using OneWare.PslExtension.Services;
using Xunit;

namespace OneWare.PslExtension.UnitTests;

public class PslAiFunctionsTests
{
    [Fact]
    public void PassingRunIsNotAnError()
    {
        var result = PslAiFunctions.FormatResult("counter.sby", true, ["SBY 12:00:00 [counter] DONE (PASS, rc=0)"]);

        Assert.StartsWith("SBY passed for 'counter.sby'.", result);
        Assert.Contains("DONE (PASS, rc=0)", result);
    }

    [Fact]
    public void FailingPropertyIsAResultNotAnError()
    {
        var result = PslAiFunctions.FormatResult("counter.sby", false,
            ["SBY 12:00:00 [counter] summary: Assert failed in counter", "SBY 12:00:00 [counter] DONE (FAIL, rc=2)"]);

        Assert.DoesNotContain("Error:", result);
        Assert.Contains("did not pass", result);
        Assert.Contains("Assert failed in counter", result);
    }

    [Fact]
    public void RunThatCouldNotStartIsAnError()
    {
        var result = PslAiFunctions.FormatResult("counter.sby", false, ["Docker was not found."]);

        Assert.StartsWith("Error: SBY could not run 'counter.sby'.", result);
        Assert.Contains("Docker was not found.", result);
    }

    [Fact]
    public void LongOutputKeepsTheLastLines()
    {
        var lines = Enumerable.Range(1, PslAiFunctions.MaxOutputLines + 50).Select(x => $"line {x}").ToList();

        var result = PslAiFunctions.FormatResult("counter.sby", true, lines);

        Assert.Contains("first 50 lines omitted", result);
        Assert.DoesNotContain("line 50\n", result);
        Assert.Contains("line 51\n", result);
        Assert.EndsWith($"line {lines.Count}", result);
    }

    [Fact]
    public void RunSbyFunctionIsOffered()
    {
        var functions = new PslAiFunctions(null!, null!).CreateFunctions().ToList();

        var runSby = Assert.Single(functions);
        Assert.Equal("psl_run_sby", runSby.Name);
        Assert.NotNull(runSby.ConfirmationCheck);
    }
}
