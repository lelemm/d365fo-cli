using D365FO.Core;
using Spectre.Console.Cli;

namespace D365FO.Cli.Commands.Agent;

public sealed class SchemaCommand : Command<SchemaCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--full")]
        [System.ComponentModel.Description("Emit every CLI command. Default emits the compact agent-first surface.")]
        public bool Full { get; init; }
    }

    private sealed record CommandSpec(
        string Command,
        string Description,
        string[] Args,
        string[] Options,
        string[] ReplacesMcp,
        bool Preferred = false);

    public override int Execute(CommandContext ctx, Settings settings)
    {
        var commands = Commands();
        var selected = settings.Full ? commands : commands.Where(c => c.Preferred).ToArray();

        var payload = new
        {
            name = "d365fo",
            version = typeof(SchemaCommand).Assembly.GetName().Version?.ToString() ?? "0.1.0-dev",
            defaultOutput = "json when stdout/stderr are redirected; table in an interactive TTY",
            envelope = new
            {
                ok = "bool",
                data = "T",
                error = new { code = "string", message = "string", hint = "string?" },
            },
            guidance = new[]
            {
                "Prefer CLI commands over MCP when a shell is available; command text is cheaper than MCP tool schemas over multi-turn work.",
                "Use compact commands first: search any, search batch, get object, find related, read class/table/form.",
                "Use dedicated commands when you need a narrower command or a bridge-backed live read.",
                "Generated AOT XML is written to files; stdout returns a JSON summary to avoid loading large XML into the prompt.",
            },
            workflows = Workflows(),
            commands = selected,
            fullManifestHint = settings.Full ? null : "Run `d365fo schema --full` only when you need the complete command catalog.",
        };

        Console.Out.WriteLine(D365Json.Serialize(ToolResult<object>.Success(payload), indented: true));
        return 0;
    }

    private static object[] Workflows() =>
    [
        new
        {
            name = "discover object",
            commands = new[]
            {
                "d365fo search any <name> --output json",
                "d365fo get object <kind> <name> --output json",
                "d365fo find related name-search <name> --output json",
            },
        },
        new
        {
            name = "author extension",
            commands = new[]
            {
                "d365fo suggest extension <target> --output json",
                "d365fo find related extensions <target> --output json",
                "d365fo generate extension <kind> <target> --suffix <suffix> --out <path> --output json",
            },
        },
        new
        {
            name = "safe table/form scaffolding",
            commands = new[]
            {
                "d365fo search batch <new-name> <primary-table> --output json",
                "d365fo get object table <primary-table> --output json",
                "d365fo find form-patterns --table <primary-table> --output json",
                "d365fo generate form <name> --pattern <pattern> --table <primary-table> --out <path> --output json",
            },
        },
        new
        {
            name = "security trace",
            commands = new[]
            {
                "d365fo get security <menu-item> --type Menuitem --output json",
                "d365fo get object role <role> --output json",
                "d365fo get object duty <duty> --output json",
                "d365fo get object privilege <privilege> --output json",
            },
        },
    ];

    private static CommandSpec[] Commands() =>
    [
        C("search any", "Scope-agnostic search across every indexed kind.", ["<QUERY>"], ["--limit", "--output"], ["search", "search_any"], true),
        C("search batch", "Run several scope-agnostic searches in one process.", ["<QUERY>..."], ["--limit", "--output"], ["batch_search"], true),
        C("get object", "Generic get by kind/name across object types.", ["<KIND>", "<NAME>"], ["--output", "--resolve-labels"], ["get_*", "get_object"], true),
        C("find related", "Generic relation lookup by relation/name.", ["<RELATION>", "<NAME>"], ["--kind", "--method", "--limit", "--output"], ["find_*", "find_related"], true),
        C("read class", "Read X++ source embedded in an AxClass.", ["<NAME>"], ["--method", "--declaration", "--lines", "--around", "--output"], [], true),
        C("read table", "Read X++ source embedded in an AxTable.", ["<NAME>"], ["--method", "--declaration", "--lines", "--around", "--output"], [], true),
        C("read form", "Read X++ source embedded in an AxForm.", ["<NAME>"], ["--method", "--declaration", "--lines", "--around", "--output"], [], true),
        C("suggest edt", "Suggest EDTs for a field name.", ["<FIELDNAME>"], ["--limit", "--output"], ["suggest_edt"], true),
        C("suggest extension", "Recommend CoC/event-handler/AOT-extension strategy.", ["<TARGET>"], ["--kind", "--output"], ["suggest_extension_strategy"], true),
        C("validate name", "Check object name against naming conventions.", ["<KIND>", "<NAME>"], ["--prefix", "--output"], ["validate_object_naming"], true),
        C("stats", "Aggregate counters over the index.", [], ["--top", "--output"], ["stats"], true),
        C("lint", "In-process best-practice gate over the index.", [], ["--category", "--all-models", "--format", "--output"], ["lint"], true),
        C("schema", "Emit this JSON command manifest.", [], ["--full"], [], true),
        C("agent-prompt", "Emit the CLI-first LLM system prompt.", [], ["--out"], [], true),

        C("search class", "Find X++ classes by substring.", ["<QUERY>"], ["--model", "--limit", "--output"], ["search_classes"]),
        C("search table", "Find tables by substring.", ["<QUERY>"], ["--model", "--limit", "--output"], ["search_tables"]),
        C("search edt", "Find Extended Data Types by substring.", ["<QUERY>"], ["--limit", "--output"], ["search_edts"]),
        C("search enum", "Find base enums by substring.", ["<QUERY>"], ["--limit", "--output"], ["search_enums"]),
        C("search label", "Search label keys/values; FTS5 is preferred automatically.", ["<QUERY>"], ["--lang", "--limit", "--fts", "--raw-text", "--output"], ["search_labels", "search_labels_fts"]),
        C("search query", "Find AOT queries.", ["<QUERY>"], ["--limit", "--output"], ["search_queries"]),
        C("search view", "Find AOT views.", ["<QUERY>"], ["--limit", "--output"], ["search_views"]),
        C("search entity", "Find data entities by AOT/OData name.", ["<QUERY>"], ["--limit", "--output"], ["search_data_entities"]),
        C("search report", "Find SSRS/RDL reports.", ["<QUERY>"], ["--limit", "--output"], ["search_reports"]),
        C("search service", "Find SOAP services.", ["<QUERY>"], ["--limit", "--output"], ["search_services"]),
        C("search workflow", "Find workflow types.", ["<QUERY>"], ["--limit", "--output"], ["search_workflow_types"]),

        C("get table", "Get table fields, relations, indexes, methods, and delete actions.", ["<NAME>"], ["--include", "--output", "--resolve-labels"], ["get_table_details", "get_table_methods", "get_table_indexes", "get_table_delete_actions"]),
        C("get edt", "Get an EDT definition.", ["<NAME>"], ["--output"], ["get_edt_details"]),
        C("get class", "Get class metadata and method signatures.", ["<NAME>"], ["--output"], ["get_class_details"]),
        C("get enum", "Get enum values.", ["<NAME>"], ["--output"], ["get_enum_details"]),
        C("get menu-item", "Resolve menu item to launched object.", ["<NAME>"], ["--output"], ["get_menu_item"]),
        C("get security", "Get role/duty/privilege coverage for an object.", ["<OBJECT>"], ["--type", "--output"], ["get_security_coverage_for_object"]),
        C("get label", "Resolve a label entry or token.", ["<FILE_OR_KEY>", "[KEY]"], ["--lang", "--raw-text", "--output"], ["get_label", "resolve_label"]),
        C("get form", "Get form data sources and metadata.", ["<NAME>"], ["--output"], ["get_form"]),
        C("get role", "Get security role duties/privileges.", ["<NAME>"], ["--output"], ["get_security_role"]),
        C("get duty", "Get security duty privileges.", ["<NAME>"], ["--output"], ["get_security_duty"]),
        C("get privilege", "Get security privilege entry points.", ["<NAME>"], ["--output"], ["get_security_privilege"]),
        C("get query", "Get query metadata and joins.", ["<NAME>"], ["--output"], ["get_query"]),
        C("get view", "Get view fields and source query.", ["<NAME>"], ["--output"], ["get_view"]),
        C("get entity", "Get data entity metadata and OData names.", ["<NAME>"], ["--output"], ["get_data_entity"]),
        C("get report", "Get report datasets.", ["<NAME>"], ["--output"], ["get_report"]),
        C("get service", "Get service operations.", ["<NAME>"], ["--output"], ["get_service"]),
        C("get service-group", "Get service group members.", ["<NAME>"], ["--output"], ["get_service_group"]),

        C("find coc", "Find Chain-of-Command extensions.", ["<TARGET>"], ["--output"], ["find_coc_extensions"]),
        C("find relations", "Find table relations.", ["<TABLE>"], ["--output"], ["get_table_relations"]),
        C("find usages", "Find indexed entities whose names contain a substring.", ["<SYMBOL>"], ["--limit", "--output"], ["find_usages"]),
        C("find extensions", "Find Table/Form/Edt/Enum extensions targeting an object.", ["<TARGET>"], ["--kind", "--output"], ["find_extensions", "get_table_extension_info"]),
        C("find handlers", "Find event subscribers.", ["<OBJECT>"], ["--kind", "--output"], ["find_event_subscribers"]),
        C("find refs", "Scan indexed X++ source for reverse references.", ["<NAME>"], ["--kind", "--model", "--limit", "--xref", "--output"], []),
        C("find form-patterns", "Find forms by Microsoft form pattern, table, or peer form.", [], ["--pattern", "--table", "--similar-to", "--output"], []),

        C("resolve label", "Resolve @SYS12345-style label token across languages.", ["<TOKEN>"], ["--lang", "--raw-text", "--output"], ["resolve_label"]),
        C("label create", "Create or update a label entry.", ["<KEY>", "<VALUE>"], ["--file", "--overwrite", "--output"], ["create_label"]),
        C("label rename", "Rename a label key.", ["<OLD>", "<NEW>"], ["--file", "--overwrite", "--output"], ["rename_label"]),
        C("label delete", "Delete a label key.", ["<KEY>"], ["--file", "--output"], ["delete_label"]),

        C("index build", "Create or ensure the metadata index schema.", [], ["--db", "--output"], ["index_status"]),
        C("index status", "Report index table counts and config.", [], ["--output"], ["index_status"]),
        C("index extract", "Walk PackagesLocalDirectory and ingest AOT metadata.", [], ["--packages", "--db", "--model", "--since", "--output"], []),
        C("index refresh", "Incremental extract using model fingerprints.", [], ["--packages", "--db", "--model", "--since", "--force", "--output"], []),
        C("index history", "Show recent extraction telemetry.", [], ["--db", "--model", "--limit", "--output"], ["index_history"]),
        C("models list", "List indexed models.", [], ["--output"], ["list_models"]),
        C("models deps", "Show dependencies for a model.", ["<NAME>"], ["--output"], ["get_model_dependencies"]),
        C("models coupling", "Show fan-in/fan-out/instability/cycles.", [], ["--top", "--only-cycles", "--output"], ["models_coupling"]),

        C("generate table", "Scaffold AxTable XML.", ["<NAME>"], ["--out", "--overwrite", "--install-to", "--label", "--field", "--pattern", "--table-type", "--primary-key", "--output"], ["generate_smart_table"]),
        C("generate class", "Scaffold AxClass XML.", ["<NAME>"], ["--out", "--overwrite", "--install-to", "--extends", "--non-final", "--output"], []),
        C("generate coc", "Scaffold a Chain-of-Command class.", ["<TARGET>"], ["--out", "--overwrite", "--install-to", "--method", "--output"], []),
        C("generate form", "Scaffold AxForm XML for the supported form patterns.", ["<FORM_NAME>"], ["--out", "--overwrite", "--install-to", "--pattern", "--table", "--caption", "--field", "--section", "--lines-table", "--output"], ["generate_smart_form"]),
        C("generate simple-list", "Deprecated alias for generate form --pattern SimpleList.", ["<FORM_NAME>"], ["--out", "--table", "--overwrite", "--output"], ["generate_smart_form"]),
        C("generate entity", "Scaffold AxDataEntityView XML.", ["<ENTITY>"], ["--out", "--overwrite", "--install-to", "--table", "--public-entity", "--public-collection", "--field", "--all-fields", "--output"], []),
        C("generate extension", "Scaffold Table/Form/Edt/Enum extension XML.", ["<KIND>", "<TARGET>"], ["--suffix", "--out", "--overwrite", "--install-to", "--output"], []),
        C("generate event-handler", "Scaffold an event subscriber class.", ["<CLASS_NAME>"], ["--source-kind", "--source-object", "--event", "--method", "--out", "--overwrite", "--install-to", "--output"], []),
        C("generate privilege", "Scaffold a security privilege.", ["<NAME>"], ["--entry-point", "--entry-kind", "--entry-object", "--access", "--label", "--into-role", "--out", "--overwrite", "--install-to", "--output"], []),
        C("generate duty", "Scaffold a security duty.", ["<NAME>"], ["--privilege", "--label", "--into-role", "--out", "--overwrite", "--install-to", "--output"], []),
        C("generate role", "Scaffold or merge a security role.", ["<NAME>"], ["--duty", "--privilege", "--label", "--description", "--add-to", "--out", "--overwrite", "--install-to", "--output"], []),
        C("generate report", "Scaffold AxReport and RDP skeleton.", ["<NAME>"], ["--dp", "--tmp", "--dataset", "--caption", "--field", "--parameter", "--extra-dataset", "--out-dp", "--out-contract", "--out", "--overwrite", "--install-to", "--output"], []),

        C("analyze completeness", "Cross-check workspace AOT XML against the index.", [], ["--workspace", "--output"], []),
        C("review diff", "Inspect AOT changes vs. a git revision.", [], ["--base", "--head", "--repo", "--output"], []),
        C("build", "Invoke MSBuild on a D365FO project.", [], ["--msbuild", "--project", "--config", "--output"], []),
        C("sync", "Run DB sync.", [], ["--tool", "--full", "--output"], []),
        C("test run", "Invoke SysTestRunner.", [], ["--runner", "--suite", "--output"], []),
        C("bp check", "Invoke xppbp best-practice checks.", [], ["--tool", "--model", "--packages", "--metadata", "--output"], []),
        C("daemon start", "Start warm JSON-RPC daemon.", [], ["--db", "--packages", "--foreground", "--no-watch", "--watch-debounce"], []),
        C("daemon stop", "Stop daemon.", [], [], []),
        C("daemon status", "Report daemon status.", [], [], []),
        C("doctor", "Diagnose environment.", [], ["--output"], []),
        C("init", "Quickstart index/profile setup.", [], ["--packages", "--db", "--run-extract", "--dry-run", "--persist-profile"], []),
        C("version", "Print version information.", [], ["--output"], []),
    ];

    private static CommandSpec C(
        string command,
        string description,
        string[] args,
        string[] options,
        string[] replacesMcp,
        bool preferred = false)
        => new(command, description, args, options, replacesMcp, preferred);
}
