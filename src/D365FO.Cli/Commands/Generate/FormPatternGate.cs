namespace D365FO.Cli.Commands.Generate;

/// <summary>
/// Write gate for the form-pattern validator — mirrors the upstream MCP
/// <c>FORM_PATTERN_ENFORCE</c> switch. Enforcement is ON by default; only an
/// explicit <c>D365FO_FORM_PATTERN_ENFORCE=false</c> disables it. Structural
/// violations (FP001-FP005, FP007) block `generate form` writes while enforced;
/// recommendations (FP002 drift, FP006, FP008-FP010) only warn.
/// </summary>
internal static class FormPatternGate
{
    public const string EnforceEnvVar = "D365FO_FORM_PATTERN_ENFORCE";

    public static bool EnforcementEnabled =>
        !string.Equals(Environment.GetEnvironmentVariable(EnforceEnvVar), "false", StringComparison.OrdinalIgnoreCase);
}
