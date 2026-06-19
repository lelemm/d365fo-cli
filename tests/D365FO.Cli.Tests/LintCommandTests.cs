using D365FO.Cli.Commands.Lint;
using Spectre.Console.Cli;

namespace D365FO.Cli.Tests;

public sealed class LintCommandTests
{
    [Fact]
    public void Lint_accepts_optional_file_argument()
    {
        var property = typeof(LintCommand.Settings).GetProperty(nameof(LintCommand.Settings.File));

        Assert.NotNull(property);
        Assert.Contains(property!.GetCustomAttributes(inherit: false), attribute => attribute is CommandArgumentAttribute);
    }

    [Fact]
    public void Lint_file_mode_exposes_code_type_and_context_options()
    {
        var codeType = typeof(LintCommand.Settings).GetProperty(nameof(LintCommand.Settings.CodeType));
        var context = typeof(LintCommand.Settings).GetProperty(nameof(LintCommand.Settings.Context));
        var backend = typeof(LintCommand.Settings).GetProperty(nameof(LintCommand.Settings.Backend));
        var model = typeof(LintCommand.Settings).GetProperty(nameof(LintCommand.Settings.Model));

        Assert.NotNull(codeType);
        Assert.NotNull(context);
        Assert.NotNull(backend);
        Assert.NotNull(model);
        Assert.Contains(codeType!.GetCustomAttributes(inherit: false), attribute => attribute is CommandOptionAttribute);
        Assert.Contains(context!.GetCustomAttributes(inherit: false), attribute => attribute is CommandOptionAttribute);
        Assert.Contains(backend!.GetCustomAttributes(inherit: false), attribute => attribute is CommandOptionAttribute);
        Assert.Contains(model!.GetCustomAttributes(inherit: false), attribute => attribute is CommandOptionAttribute);
    }

    [Theory]
    [InlineData("auto", "Auto")]
    [InlineData("bridge", "Bridge")]
    [InlineData("legacy", "Legacy")]
    [InlineData("BRIDGE", "Bridge")]
    public void Lint_backend_resolver_accepts_supported_values(string raw, string expected)
    {
        var ok = LintBackendResolver.TryResolve(raw, out var backend, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, backend.ToString());
    }

    [Fact]
    public void Lint_backend_resolver_rejects_unknown_values()
    {
        var ok = LintBackendResolver.TryResolve("custom", out _, out var error);

        Assert.False(ok);
        Assert.Contains("auto, bridge, or legacy", error);
    }
}
