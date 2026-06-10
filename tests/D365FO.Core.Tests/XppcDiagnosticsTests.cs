using D365FO.Core.Validation;
using Xunit;

namespace D365FO.Core.Tests;

public class XppcDiagnosticsTests
{
    [Fact]
    public void Parses_full_dynamics_uri_line()
    {
        var log = "Compile Error: Class Method dynamics://MyModel/MyClass/myMethod: [(28,27),(28,28)]: ';' expected.";
        var d = Assert.Single(XppcDiagnostics.Parse(log));
        Assert.Equal("error", d.Severity);
        Assert.Equal("Class Method", d.Kind);
        Assert.Equal("MyModel", d.Model);
        Assert.Equal("MyClass", d.Object);
        Assert.Equal("myMethod", d.Member);
        Assert.Equal(28, d.Line);
        Assert.Equal(27, d.Column);
        Assert.Equal("';' expected.", d.Message);
        Assert.NotNull(d.Hint); // semicolon hint
    }

    [Fact]
    public void Parses_line_without_member()
    {
        var log = "Compile Warning: Table dynamics://MyModel/MyTable: [(1,1)]: Some warning text";
        var d = Assert.Single(XppcDiagnostics.Parse(log));
        Assert.Equal("warning", d.Severity);
        Assert.Equal("MyTable", d.Object);
        Assert.Null(d.Member);
    }

    [Fact]
    public void Falls_back_to_simple_severity_prefix()
    {
        var log = "Compile Fatal Error: Out of memory during AOT compilation";
        var d = Assert.Single(XppcDiagnostics.Parse(log));
        Assert.Equal("error", d.Severity);
        Assert.Null(d.Object);
        Assert.Contains("Out of memory", d.Message);
    }

    [Fact]
    public void Ignores_unrelated_lines()
    {
        var log = """
            Build started.
            Compile Error: Class Method dynamics://M/C/m: [(1,2)]: unknown type 'FooBar'
            1 Warning(s)
            """;
        var diags = XppcDiagnostics.Parse(log);
        var d = Assert.Single(diags);
        Assert.Contains("unknown type", d.Message);
        Assert.Contains("validate references", d.Hint);
    }

    [Fact]
    public void Detects_stale_symbols()
    {
        Assert.True(XppcDiagnostics.IndicatesStaleSymbols(
            "Class Foo has not been successfully compiled since it was last changed."));
        Assert.False(XppcDiagnostics.IndicatesStaleSymbols("All good."));
    }
}
