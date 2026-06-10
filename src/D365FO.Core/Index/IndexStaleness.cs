namespace D365FO.Core.Index;

/// <summary>
/// Index staleness detection — port of the upstream MCP server's
/// <c>indexStaleness</c> module.
///
/// The index is the single source of truth for grounding — but only while it
/// reflects the current workspace. This compares the newest XML/label mtime
/// under the packages root(s) with the index's <c>Models.LastExtractedUtc</c>
/// bookkeeping and produces the <c>stale-index</c> warning when the workspace
/// has changed since the last (re)extract.
/// </summary>
public static class IndexStaleness
{
    /// <summary>Hard cap on stat'ed files so the scan stays fast on huge roots.</summary>
    public const int MaxScannedFiles = 5000;

    /// <summary>Files newer than the index by less than this are tolerated (clock skew, in-flight writes).</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(60);

    public sealed record MtimeScanResult(DateTime NewestMtimeUtc, string NewestFile, int ScannedFiles, bool Truncated);

    public sealed record StalenessResult(
        bool IsStale,
        string? Detail,
        DateTime? NewestFileMtimeUtc,
        string? NewestFile,
        DateTime? LastExtractedUtc,
        int ScannedFiles,
        bool Truncated);

    /// <summary>
    /// Recursively find the newest metadata file mtime under <paramref name="rootDir"/>.
    /// Returns null when the directory does not exist or contains no metadata files.
    /// </summary>
    public static MtimeScanResult? FindNewestMetadataMtime(string rootDir)
    {
        if (!Directory.Exists(rootDir)) return null;
        DateTime newest = DateTime.MinValue;
        string newestFile = "";
        int scanned = 0;
        bool truncated = false;

        void Walk(string dir)
        {
            if (truncated) return;
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); }
            catch { return; }
            foreach (var entry in entries)
            {
                if (truncated) return;
                if (Directory.Exists(entry))
                {
                    // bin/obj and Descriptor churn are not metadata edits.
                    var name = Path.GetFileName(entry);
                    if (name is "bin" or "obj" or ".git" or "XppMetadata") continue;
                    Walk(entry);
                }
                else if (entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                         || entry.EndsWith(".label.txt", StringComparison.OrdinalIgnoreCase))
                {
                    scanned++;
                    if (scanned >= MaxScannedFiles) truncated = true;
                    DateTime mtime;
                    try { mtime = File.GetLastWriteTimeUtc(entry); }
                    catch { continue; }
                    if (mtime > newest)
                    {
                        newest = mtime;
                        newestFile = entry;
                    }
                }
            }
        }

        Walk(rootDir);
        if (scanned == 0) return null;
        return new MtimeScanResult(newest, newestFile, scanned, truncated);
    }

    /// <summary>
    /// Compare the newest metadata mtime under the custom-model folders (or the
    /// whole roots when no custom models are known) against the index bookkeeping.
    /// </summary>
    public static StalenessResult Check(MetadataRepository repo, IReadOnlyCollection<string> packagesRoots)
    {
        DateTime? lastExtracted = repo.GetNewestExtractTimestampUtc();
        if (lastExtracted is null)
        {
            return new StalenessResult(true, "Index has never been extracted — run `d365fo index extract`.",
                null, null, null, 0, false);
        }

        // Scan only custom models' folders when known: standard models do not
        // change outside platform updates, and the 5000-file cap goes further.
        var roots = new List<string>();
        try
        {
            var customModels = repo.ListModels()
                .Where(m => m.IsCustom)
                .Select(m => m.Name)
                .ToList();
            foreach (var root in packagesRoots)
            {
                foreach (var model in customModels)
                {
                    var dir = Path.Combine(root, model);
                    if (Directory.Exists(dir)) roots.Add(dir);
                }
            }
        }
        catch { /* fall back to full roots */ }
        if (roots.Count == 0) roots.AddRange(packagesRoots.Where(Directory.Exists));

        MtimeScanResult? newest = null;
        foreach (var root in roots)
        {
            var scan = FindNewestMetadataMtime(root);
            if (scan is null) continue;
            if (newest is null || scan.NewestMtimeUtc > newest.NewestMtimeUtc)
                newest = scan with { ScannedFiles = (newest?.ScannedFiles ?? 0) + scan.ScannedFiles };
        }
        if (newest is null)
        {
            return new StalenessResult(false, null, null, null, lastExtracted, 0, false);
        }

        var stale = newest.NewestMtimeUtc > lastExtracted.Value + Tolerance;
        return new StalenessResult(
            stale,
            stale
                ? $"Workspace changed after the last extract: \"{newest.NewestFile}\" " +
                  $"({newest.NewestMtimeUtc:O}) is newer than the index ({lastExtracted.Value:O}). " +
                  "Run `d365fo index refresh`."
                : null,
            newest.NewestMtimeUtc,
            newest.NewestFile,
            lastExtracted,
            newest.ScannedFiles,
            newest.Truncated);
    }
}
