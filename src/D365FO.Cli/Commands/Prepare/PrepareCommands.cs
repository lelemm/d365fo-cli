using D365FO.Core;
using D365FO.Core.Guardrails;
using D365FO.Core.Index;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Prepare;

/// <summary>
/// <c>prepare change</c> — single-round context aggregator for D365FO extension
/// work. Port of the upstream MCP <c>prepare_change</c> tool: one call replaces
/// the search → get → find coc → suggest extension sequence (4–6 agentic
/// rounds) and returns an object-bound grounding token (30-min TTL) proving
/// the model looked at the real codebase before writing code.
/// </summary>
public sealed class PrepareChangeCommand : Command<PrepareChangeCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "<OBJECT>")]
        [System.ComponentModel.Description("Name of the D365FO object to extend or modify (class, table, form, …). Example: CustTable, SalesFormLetter.")]
        public string ObjectName { get; init; } = "";

        [CommandOption("--goal <TEXT>")]
        [System.ComponentModel.Description("One-sentence description of the intended change. Example: \"Add CoC on CustTable.validateWrite to enforce a custom rule.\"")]
        public string? Goal { get; init; }

        [CommandOption("--method <NAME>")]
        [System.ComponentModel.Description("Target method name when the change involves a specific method (CoC or event handlers).")]
        public string? Method { get; init; }

        [CommandOption("--type <KIND>")]
        [System.ComponentModel.Description("Object kind: class|table|form|query|view|enum|edt|data-entity|map|report. Auto-detected from the index when omitted.")]
        public string? Type { get; init; }

        [CommandOption("--proposed-name <NAME>")]
        [System.ComponentModel.Description("Proposed name for the new extension class/object — naming validation runs when provided.")]
        public string? ProposedName { get; init; }

        [CommandOption("--prefix <PREFIX>")]
        [System.ComponentModel.Description("Publisher prefix used for naming validation of --proposed-name.")]
        public string? Prefix { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.ObjectName))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Object name required."));

        MetadataRepository repo;
        try { repo = RepoFactory.Create(); }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("NO_INDEX",
                $"prepare change requires the SQLite index: {ex.Message}",
                "Run `d365fo index build` then `d365fo index extract` first."));
        }

        var objectName = settings.ObjectName.Trim();
        var goal = settings.Goal ?? "(not stated)";

        // Resolve object type from the index when not provided.
        var kinds = repo.SymbolKinds(objectName);
        var objectType = settings.Type?.Trim().ToLowerInvariant()
                         ?? kinds.FirstOrDefault();
        if (kinds.Count == 0 && settings.Type is null)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("OBJECT_NOT_FOUND",
                $"\"{objectName}\" not found in the index.",
                $"Use `d365fo search any {objectName}` to find the correct name — do not invent one."));
        }

        // Method signature + CoC eligibility.
        object? methodInfo = null;
        if (!string.IsNullOrEmpty(settings.Method))
        {
            var method = repo.FindMethod(objectName, settings.Method!);
            if (method is null)
            {
                methodInfo = new
                {
                    name = settings.Method,
                    found = false,
                    eligibility = $"Method \"{settings.Method}\" not found on {objectName} (checked inheritance chain and extensions). " +
                                  $"Use `d365fo get {(objectType == "table" ? "table" : "class")} {objectName}` to list real methods.",
                };
            }
            else
            {
                var attrs = repo.GetMethodAttributes(objectName, settings.Method!);
                var blockers = new List<string>();
                foreach (var (attrName, rawArgs) in attrs)
                {
                    var falseArg = rawArgs?.Contains("false", StringComparison.OrdinalIgnoreCase) ?? false;
                    if (attrName.Contains("Hookable", StringComparison.OrdinalIgnoreCase) && falseArg)
                        blockers.Add("[Hookable(false)] — CoC is blocked on this method.");
                    if (attrName.Contains("Wrappable", StringComparison.OrdinalIgnoreCase) && falseArg)
                        blockers.Add("[Wrappable(false)] — wrapping is blocked on this method.");
                }
                var isFinal = method.Signature?.Contains("final", StringComparison.OrdinalIgnoreCase) ?? false;
                if (isFinal && blockers.Count == 0)
                    blockers.Add("Method is final — requires [Wrappable(true)] to enable CoC.");
                methodInfo = new
                {
                    name = settings.Method,
                    found = true,
                    signature = method.Signature ?? "(signature unavailable — method proven via extension metadata)",
                    eligibility = blockers.Count > 0 ? string.Join(" ", blockers) : "Method appears CoC-eligible.",
                };
            }
        }

        // Existing CoC wrappers — copy their pattern, never duplicate them.
        var coc = repo.FindCocExtensions(objectName, settings.Method)
            .Select(c => new { c.TargetMethod, c.ExtensionClass, c.Model })
            .ToList();

        // Naming validation for the proposed extension object.
        object? naming = null;
        if (!string.IsNullOrEmpty(settings.ProposedName))
        {
            var nameKind = objectType == "table" ? "Coc" : "Coc";
            var violations = ObjectNamingRules.Validate(nameKind, settings.ProposedName!, settings.Prefix);
            var collision = repo.SymbolKinds(settings.ProposedName!);
            naming = new
            {
                proposedName = settings.ProposedName,
                ok = !violations.Any(v => v.Severity == "error") && collision.Count == 0,
                collision = collision.Count > 0 ? $"\"{settings.ProposedName}\" already exists ({string.Join(", ", collision)})." : null,
                violations = violations.Select(v => new { code = v.Code, severity = v.Severity, message = v.Message }),
            };
        }

        // Similar objects worth copying patterns from.
        var similar = repo.FindSimilarObjects(objectType ?? "class", LastToken(objectName))
            .Where(s => !s.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
            .Select(s => new { s.Name, s.Model })
            .ToList();

        var token = ProvenanceStore.CreateToken(new ProvenanceContext(
            goal, objectName, settings.Method, objectType, settings.ProposedName));

        var result = ToolResult<object>.Success(new
        {
            goal,
            objectName,
            objectType,
            method = methodInfo,
            existingCocExtensions = coc,
            recommendedStrategies = StrategiesFor(objectType),
            namingValidation = naming,
            similarObjects = similar,
            groundingToken = token,
            groundingNote = ProvenanceStore.EnforcementEnabled
                ? $"D365FO_GROUNDING_ENFORCE=true — pass --grounding-token to `d365fo generate coc/extension/event-handler`. " +
                  $"The token is bound to \"{objectName}\" and expires in 30 minutes."
                : "Pass --grounding-token to `d365fo generate …` to confirm this context was used. " +
                  "Set D365FO_GROUNDING_ENFORCE=true to require it.",
            nextSteps = new[]
            {
                "Generate the extension (`d365fo generate coc/extension/event-handler … --grounding-token <token>`).",
                "Run `d365fo validate references` and `d365fo validate xpp` on any hand-written X++ before writing it.",
            },
        });

        return RenderHelpers.Render(kind, result, _ =>
        {
            AnsiConsole.MarkupLine($"[bold]{RenderHelpers.Escape(objectName)}[/] ({objectType ?? "?"}) — {coc.Count} existing CoC extension(s)");
            AnsiConsole.MarkupLine($"grounding token: [green]{token}[/]");
        });
    }

    internal static string LastToken(string name)
    {
        var tokens = System.Text.RegularExpressions.Regex
            .Split(name, "(?=[A-Z])")
            .Where(t => t.Length >= 4)
            .ToList();
        return tokens.Count > 0 ? tokens[^1] : name;
    }

    internal static IReadOnlyList<string> StrategiesFor(string? objectType) => objectType switch
    {
        "table" => new[]
        {
            "Table extension (AxTableExtension) — add fields, indexes, relations, field groups: `d365fo generate extension table <Target> <Suffix>`",
            "Table extension class [ExtensionOf(tableStr(...))] — CoC on table methods: `d365fo generate coc <Target> --method <m>`",
            "Event handler [DataEventHandler(tableStr(X), DataEventType::...)] — subscribe to data events: `d365fo generate event-handler`",
            "New standalone class — if no suitable extension point exists",
        },
        "class" => new[]
        {
            "Class extension [ExtensionOf(classStr(...))] — CoC on class methods: `d365fo generate coc <Target> --method <m>`",
            "Event handler [SubscribesTo(...)] — subscribe to delegate events: `d365fo generate event-handler`",
            "New standalone class — if no suitable extension point exists",
        },
        "form" => new[]
        {
            "Form extension (AxFormExtension) — add controls, data sources, menu items: `d365fo generate extension form <Target> <Suffix>`",
            "Form extension class [ExtensionOf(formStr(...))] — CoC on form methods",
            "Form datasource extension [ExtensionOf(formDataSourceStr(...))] — CoC on DS methods",
            "New standalone class — if no suitable extension point exists",
        },
        "map" => new[]
        {
            "Map extension class [ExtensionOf(mapStr(...))] — add/wrap map methods",
            "New standalone class — if no suitable extension point exists",
        },
        _ => new[]
        {
            "Extension class via [ExtensionOf] — check the object type for supported extension mechanisms",
            "New standalone class — if no suitable extension point exists",
        },
    };
}

/// <summary>
/// <c>prepare create</c> — single-round context aggregator for NEW D365FO
/// objects. Port of the upstream MCP <c>prepare_create</c> tool: one call
/// replaces the search → validate name → suggest edt → search labels sequence
/// and returns collision check, naming validation, similar objects, EDT
/// suggestions, reusable labels, mined property defaults, and a grounding token.
/// </summary>
public sealed class PrepareCreateCommand : Command<PrepareCreateCommand.Settings>
{
    public sealed class Settings : D365OutputSettings
    {
        [CommandArgument(0, "<NAME>")]
        [System.ComponentModel.Description("Proposed BASE name of the new object (the same value you would pass to `d365fo generate`). Example: ImportParameters.")]
        public string Name { get; init; } = "";

        [CommandOption("--type <KIND>")]
        [System.ComponentModel.Description("Object kind: class|table|form|enum|edt|query|view|data-entity|report|menu-item|privilege|duty|role.")]
        public string Type { get; init; } = "class";

        [CommandOption("--goal <TEXT>")]
        [System.ComponentModel.Description("One-sentence description of what the new object is for.")]
        public string? Goal { get; init; }

        [CommandOption("--field <NAME>")]
        [System.ComponentModel.Description("For tables/views: planned field names (repeatable). Each gets EDT suggestions from the index.")]
        public string[]? Fields { get; init; }

        [CommandOption("--prefix <PREFIX>")]
        [System.ComponentModel.Description("Publisher prefix the generated object will carry (e.g. Contoso). Collision check covers both base and prefixed name.")]
        public string? Prefix { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var kind = OutputMode.Resolve(settings.Output);
        if (string.IsNullOrWhiteSpace(settings.Name))
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("BAD_INPUT", "Object name required."));

        MetadataRepository repo;
        try { repo = RepoFactory.Create(); }
        catch (Exception ex)
        {
            return RenderHelpers.Render(kind, ToolResult<object>.Fail("NO_INDEX",
                $"prepare create requires the SQLite index: {ex.Message}",
                "Run `d365fo index build` then `d365fo index extract` first."));
        }

        var baseName = settings.Name.Trim();
        var goal = settings.Goal ?? "(not stated)";
        var objectType = settings.Type.Trim().ToLowerInvariant();
        var finalName = string.IsNullOrEmpty(settings.Prefix) || baseName.StartsWith(settings.Prefix!, StringComparison.OrdinalIgnoreCase)
            ? baseName
            : settings.Prefix + baseName;

        // Collision check: exact + prefixed variant.
        var collisions = new List<object>();
        foreach (var candidate in new[] { baseName, finalName }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hit = repo.SymbolKinds(candidate);
            if (hit.Count > 0)
                collisions.Add(new { name = candidate, existsAs = hit });
        }

        // Naming validation incl. the prefix the generator will apply.
        var namingKind = objectType switch
        {
            "data-entity" => "Entity",
            "menu-item" => "MenuItem",
            _ => char.ToUpperInvariant(objectType[0]) + objectType[1..],
        };
        var violations = ObjectNamingRules.Validate(namingKind, finalName, settings.Prefix);

        // Similar existing objects to copy patterns from.
        var similar = repo.FindSimilarObjects(objectType, PrepareChangeCommand.LastToken(baseName))
            .Select(s => new { s.Name, s.Model })
            .ToList();

        // EDT suggestions per planned field.
        var fieldSuggestions = new List<object>();
        foreach (var field in (settings.Fields ?? Array.Empty<string>()).Take(10))
        {
            var suggestions = EdtSuggester.Suggest(repo, field, 3);
            fieldSuggestions.Add(new
            {
                field,
                edts = suggestions.Select(s => new { s.Edt.Name, s.Confidence, s.Reason }),
                hint = suggestions.Count == 0
                    ? $"No EDT match — use `d365fo suggest edt {field}` or base it on a primitive + label."
                    : null,
            });
        }

        // Reusable labels matching the object name.
        var words = System.Text.RegularExpressions.Regex.Replace(baseName, "([A-Z])", " $1").Trim();
        IReadOnlyList<LabelMatch> labels;
        try { labels = repo.SearchLabels(words, new[] { "en-us" }, 5); }
        catch { labels = Array.Empty<LabelMatch>(); }

        // Mined property defaults (tables only).
        object? propertyDefaults = null;
        if (objectType == "table" && repo.HasPropertyStats())
        {
            var props = new List<object>();
            foreach (var prop in new[] { "Label", "TableGroup", "ClusteredIndex", "AlternateKeyIndex", "CacheLookup" })
            {
                var (present, total, ratio) = repo.GetPropertyPresenceRatio("AxTable", prop);
                if (total == 0) continue;
                props.Add(new { property = prop, standardUsage = Math.Round(ratio * 100) + "%", required = ratio >= 0.8 });
            }
            var dist = repo.GetPropertyValueDistribution("AxTable", "TableGroup", 4);
            propertyDefaults = new
            {
                properties = props,
                tableGroupValues = dist.Select(d => new { d.Value, d.Count }),
            };
        }

        var token = ProvenanceStore.CreateToken(new ProvenanceContext(
            goal, baseName, null, objectType, finalName));

        var result = ToolResult<object>.Success(new
        {
            goal,
            objectType,
            baseName,
            finalName,
            collisions = collisions.Count > 0 ? collisions : null,
            collisionVerdict = collisions.Count > 0
                ? "Name already exists — pick a different name or extend the existing object instead."
                : $"No collision — neither \"{finalName}\" nor \"{baseName}\" exists in the index.",
            namingViolations = violations.Select(v => new { code = v.Code, severity = v.Severity, message = v.Message }),
            similarObjects = similar,
            fieldEdtSuggestions = fieldSuggestions.Count > 0 ? fieldSuggestions : null,
            reusableLabels = labels.Select(l => new { token = $"@{l.File}:{l.Key}", l.Value, l.Language }),
            labelHint = labels.Count > 0
                ? "Reuse instead of creating duplicates (rule: `d365fo search label` before `d365fo label create`)."
                : "No matching labels — create new ones via `d365fo label create`.",
            propertyDefaults,
            groundingToken = token,
            nextSteps = new[]
            {
                $"Generate the object: `d365fo generate {objectType} {baseName} … --grounding-token {token}`.",
                "Run `d365fo validate references` + `d365fo validate xpp` on any X++ before writing it.",
            },
        });

        return RenderHelpers.Render(kind, result, _ =>
        {
            AnsiConsole.MarkupLine(collisions.Count > 0
                ? $"[red]collision[/] — {collisions.Count} existing object(s)"
                : $"[green]no collision[/] for {RenderHelpers.Escape(finalName)}");
            AnsiConsole.MarkupLine($"grounding token: [green]{token}[/]");
        });
    }
}
