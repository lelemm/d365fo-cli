using System.Text.Json;

namespace D365FO.Core;

/// <summary>
/// Resolved runtime configuration. Values are sourced, in order:
/// (1) explicit CLI flags, (2) process environment variables,
/// (3) JSON config file at <see cref="GetDefaultConfigPath"/>,
/// (4) built-in defaults. See docs/CONFIGURATION.md.
/// </summary>
public sealed record D365FoSettings(
    string? PackagesPath,
    string? WorkspacePath,
    string DatabasePath,
    IReadOnlyList<string> CustomModels,
    IReadOnlyList<string> LabelLanguages,
    IReadOnlyList<string> ExtraPackagesPaths)
{
    public const string DefaultDatabaseFile = "d365fo-index.sqlite";
    public const string ConfigFileName      = "settings.json";

    /// <summary>
    /// Returns the path to the JSON config file used as a fallback when
    /// environment variables are not set. Typically
    /// <c>%LOCALAPPDATA%\d365fo-cli\settings.json</c> on Windows.
    /// </summary>
    public static string GetDefaultConfigPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "d365fo-cli", ConfigFileName);

    public static D365FoSettings FromEnvironment(string? databaseOverride = null)
    {
        // Load JSON config file as fallback; env vars always take priority.
        var jsonConfig = LoadJsonConfig();

        string Env(string k)
        {
            var v = Environment.GetEnvironmentVariable(k);
            if (!string.IsNullOrWhiteSpace(v)) return v;
            return jsonConfig.TryGetValue(k, out var jv) ? jv ?? string.Empty : string.Empty;
        }

        var models = Split(Env("D365FO_CUSTOM_MODELS"));
        var langs = Split(Env("D365FO_LABEL_LANGUAGES"));
        if (langs.Count == 0) langs = new[] { "en-us" };

        var db = databaseOverride
                 ?? (string.IsNullOrWhiteSpace(Env("D365FO_INDEX_DB")) ? null : Env("D365FO_INDEX_DB"))
                 ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "d365fo-cli", DefaultDatabaseFile);

        return new D365FoSettings(
            PackagesPath: NullIfEmpty(Env("D365FO_PACKAGES_PATH")),
            WorkspacePath: NullIfEmpty(Env("D365FO_WORKSPACE_PATH")),
            DatabasePath: db,
            CustomModels: models,
            LabelLanguages: langs,
            ExtraPackagesPaths: Split(Env("D365FO_EXTRA_PACKAGES_PATH")));
    }

    /// <summary>
    /// Persists the supplied key/value pairs to the JSON config file,
    /// merging with any values already present. Existing keys are overwritten;
    /// keys absent from <paramref name="values"/> are preserved.
    /// </summary>
    public static void SaveJsonConfig(IReadOnlyDictionary<string, string> values)
    {
        var path = GetDefaultConfigPath();
        var dir  = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var existing = LoadJsonConfig();
        var merged   = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in values) merged[k] = v;

        File.WriteAllText(path, JsonSerializer.Serialize(merged, D365Json.Pretty));
    }

    // ---- private helpers -----------------------------------------------------

    private static Dictionary<string, string> LoadJsonConfig()
    {
        var path = GetDefaultConfigPath();
        if (!File.Exists(path)) return new();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, D365Json.Options)
                   ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static IReadOnlyList<string> Split(string s) =>
        string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
