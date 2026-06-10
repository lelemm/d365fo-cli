using D365FO.Core;
using D365FO.Core.Guardrails;
using D365FO.Core.Index;
using D365FO.Core.Validation;
using System.Xml.Linq;

namespace D365FO.Cli.Commands.Generate;

/// <summary>
/// Write-side grounding enforcement for extension-shaped generate commands —
/// port of the upstream MCP server's fail-closed gate (provenance token +
/// <c>gateOnReferenceErrors</c>).
///
/// Behaviour:
///   - Default: checks run, problems surface as warnings, the write proceeds.
///   - <c>D365FO_GROUNDING_ENFORCE=true</c>: a valid object-bound grounding
///     token (from <c>d365fo prepare change/create</c>) is required, and the
///     write is rejected when the generated X++ contains identifiers the index
///     cannot prove (hallucinations) or BP errors.
/// A gate failure must never be caused by gate infrastructure itself — index
/// errors degrade to warnings, mirroring upstream ("resolver failure must
/// never block writes").
/// </summary>
internal static class GroundingGate
{
    public sealed record GateResult(object Grounding, List<string> Warnings, ToolResult<object>? Failure);

    /// <param name="token">Value of --grounding-token (may be null).</param>
    /// <param name="targetObject">The AOT object this write is bound to (CoC target, extension target…).</param>
    /// <param name="doc">Scaffolded XML; X++ inside Declaration/Source elements is validated.</param>
    /// <param name="requiredMethods">Methods that must exist on their owner (e.g. CoC-wrapped methods).</param>
    /// <param name="requiredSymbols">AOT names that must exist in the index (e.g. an extension's target object).</param>
    public static GateResult Check(
        string? token,
        string targetObject,
        XDocument? doc,
        IEnumerable<(string Owner, string Method)>? requiredMethods = null,
        IEnumerable<string>? requiredSymbols = null)
    {
        var enforce = ProvenanceStore.EnforcementEnabled;
        var warnings = new List<string>();

        // ── 1. Grounding token ───────────────────────────────────────────────
        bool tokenValid = false;
        string? tokenReason = null;
        if (!string.IsNullOrWhiteSpace(token) || enforce)
        {
            (tokenValid, var reason) = ProvenanceStore.Validate(token, targetObject);
            if (!tokenValid)
            {
                tokenReason = reason;
                if (enforce)
                {
                    return new GateResult(new { enforced = true, tokenValid = false }, warnings,
                        ToolResult<object>.Fail("GROUNDING_REQUIRED", reason,
                            $"Run `d365fo prepare change {targetObject}` (or `prepare create`) and pass the returned token via --grounding-token."));
                }
                warnings.Add($"grounding: {reason}");
            }
        }

        // ── 2. Semantic + BP self-check over the generated X++ ───────────────
        int refErrors = 0, refWarnings = 0, bpErrors = 0, bpWarnings = 0, verified = 0;
        var violationDetails = new List<string>();
        var xpp = ExtractXppSource(doc);
        MetadataRepository? repo = null;
        try { repo = RepoFactory.Create(); } catch { /* no index — checks degrade below */ }

        if (repo is not null)
        {
            if (!string.IsNullOrWhiteSpace(xpp))
            {
                try
                {
                    var resolved = ReferenceResolver.Resolve(xpp, repo);
                    verified = resolved.VerifiedCount;
                    foreach (var v in resolved.Violations)
                    {
                        if (v.Severity == "error") refErrors++; else refWarnings++;
                        violationDetails.Add($"[{v.Kind}] line {v.Line}: {v.Identifier} — {v.Detail}");
                    }

                    var stats = repo.HasPropertyStats() ? repo : (IPropertyStatsProvider?)null;
                    foreach (var v in XppValidator.Validate(xpp, XppValidator.CodeTypeXpp, stats))
                    {
                        if (v.Severity == "error") bpErrors++; else bpWarnings++;
                        violationDetails.Add($"[{v.Rule}] line {v.Line}: {v.Excerpt} — {v.Fix}");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"grounding self-check skipped: {ex.Message}");
                }
            }

            foreach (var (owner, method) in requiredMethods ?? Array.Empty<(string, string)>())
            {
                try
                {
                    if (repo.FindMethod(owner, method) is null)
                    {
                        refErrors++;
                        violationDetails.Add($"[unknown-method] {owner}::{method} — not found in the index (checked inheritance chain and extensions). " +
                                             $"Use `d365fo get class {owner}` / `d365fo get table {owner}` for the real method list.");
                    }
                    else
                    {
                        verified++;
                    }
                }
                catch { /* index hiccup — do not block */ }
            }

            foreach (var symbol in requiredSymbols ?? Array.Empty<string>())
            {
                try
                {
                    if (repo.SymbolKinds(symbol).Count == 0)
                    {
                        refErrors++;
                        violationDetails.Add($"[unknown-type] {symbol} — not found in the index. " +
                                             $"Use `d365fo search any {symbol}` to find the correct name.");
                    }
                    else
                    {
                        verified++;
                    }
                }
                catch { /* index hiccup — do not block */ }
            }
        }
        else
        {
            warnings.Add("grounding self-check skipped: no index available (run `d365fo index build` + `extract`).");
        }

        var grounding = new
        {
            enforced = enforce,
            tokenSupplied = !string.IsNullOrWhiteSpace(token),
            tokenValid,
            tokenReason,
            verifiedReferences = verified,
            referenceErrors = refErrors,
            referenceWarnings = refWarnings,
            bpErrors,
            bpWarnings,
            violations = violationDetails.Count > 0 ? violationDetails : null,
        };

        if ((refErrors > 0 || bpErrors > 0) && enforce)
        {
            return new GateResult(grounding, warnings,
                ToolResult<object>.Fail("VALIDATION_FAILED",
                    $"Generated code contains {refErrors} unresolved reference(s) and {bpErrors} BP error(s) (D365FO_GROUNDING_ENFORCE=true):\n" +
                    string.Join("\n", violationDetails),
                    "Fix the identifiers (use the suggested lookup commands), then retry. " +
                    "Run `d365fo validate references` on the corrected code to confirm it is clean."));
        }

        foreach (var detail in violationDetails)
            warnings.Add($"grounding: {detail}");

        return new GateResult(grounding, warnings, null);
    }

    /// <summary>Concatenate X++ from Declaration/Source elements of a scaffolded AOT XML.</summary>
    internal static string ExtractXppSource(XDocument? doc)
    {
        if (doc?.Root is null) return "";
        var parts = doc.Root.Descendants()
            .Where(e => e.Name.LocalName is "Declaration" or "Source")
            .Select(e => e.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v));
        return string.Join("\n", parts);
    }
}
