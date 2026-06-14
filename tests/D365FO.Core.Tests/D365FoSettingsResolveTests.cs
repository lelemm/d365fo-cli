using Xunit;

namespace D365FO.Core.Tests;

public class D365FoSettingsResolveTests
{
    // Use unlikely key names so a real settings.json on the test host cannot
    // collide with these deterministic env-var assertions.
    private const string Key = "D365FO_TEST_RESOLVE_KEY_XYZ";
    private const string FlagKey = "D365FO_TEST_RESOLVE_FLAG_XYZ";

    [Fact]
    public void Resolve_returns_env_value_when_set()
    {
        var prev = Environment.GetEnvironmentVariable(Key);
        try
        {
            Environment.SetEnvironmentVariable(Key, "hello");
            Assert.Equal("hello", D365FoSettings.Resolve(Key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Key, prev);
        }
    }

    [Fact]
    public void Resolve_returns_null_when_unset_in_env_and_json()
    {
        var prev = Environment.GetEnvironmentVariable(Key);
        try
        {
            Environment.SetEnvironmentVariable(Key, null);
            Assert.Null(D365FoSettings.Resolve(Key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Key, prev);
        }
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    public void ResolveFlag_parses_truthy_values(string value, bool expected)
    {
        var prev = Environment.GetEnvironmentVariable(FlagKey);
        try
        {
            Environment.SetEnvironmentVariable(FlagKey, value);
            Assert.Equal(expected, D365FoSettings.ResolveFlag(FlagKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FlagKey, prev);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveFlag_returns_default_when_unset(bool defaultValue)
    {
        var prev = Environment.GetEnvironmentVariable(FlagKey);
        try
        {
            Environment.SetEnvironmentVariable(FlagKey, null);
            Assert.Equal(defaultValue, D365FoSettings.ResolveFlag(FlagKey, defaultValue));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FlagKey, prev);
        }
    }
}
