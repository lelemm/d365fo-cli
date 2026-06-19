using D365FO.Core.Index;
using Microsoft.Data.Sqlite;
using System.Linq;
using Xunit;

namespace D365FO.Core.Tests;

public class MetadataRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public MetadataRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"d365fo-test-{Guid.NewGuid():N}.sqlite");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void EnsureSchema_creates_tables()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();
        // Idempotent:
        repo.EnsureSchema();
        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public void SearchClasses_returns_match_with_bool_coercion()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(repo.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Models(Name,IsCustom) VALUES('AppFound',0);
            INSERT INTO Classes(Name,ModelId,ExtendsName,IsAbstract,IsFinal,SourcePath)
              VALUES('CustTable_Extension',1,'CustTable',0,1,'/x');";
        cmd.ExecuteNonQuery();

        var hits = repo.SearchClasses("Cust");
        var one = Assert.Single(hits);
        Assert.Equal("CustTable_Extension", one.Name);
        Assert.False(one.IsAbstract);
        Assert.True(one.IsFinal);
        Assert.Equal("AppFound", one.Model);
    }

    [Fact]
    public void GetTable_missing_returns_null()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();
        Assert.Null(repo.GetTableDetails("DoesNotExist"));
    }

    [Fact]
    public void FindExtensions_accepts_full_extension_name_for_base_target()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(repo.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Models(Name,IsCustom) VALUES('Contoso',1);
            INSERT INTO ObjectExtensions(Kind,TargetName,ExtensionName,ModelId,SourcePath)
              VALUES('Table','CustTable','CustTable.Extension',1,'/x');";
        cmd.ExecuteNonQuery();

        // Both the base table name and the full extension name resolve to the
        // same indexed row (a dot marks an extension suffix; AOT names have none).
        var byBase = repo.FindExtensions("CustTable", "Table");
        var byFull = repo.FindExtensions("CustTable.Extension", "Table");
        Assert.Single(byBase);
        Assert.Single(byFull);
        Assert.Equal("CustTable.Extension", byFull[0].ExtensionName);
    }

    [Fact]
    public void RecordExtractionRun_roundtrips_via_GetExtractionRuns()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();
        repo.RecordExtractionRun("Contoso", DateTime.UtcNow, 1234, 10, 20, 3, 4, 50, true);
        repo.RecordExtractionRun("ApplicationSuite", DateTime.UtcNow, 42_000, 500, 1000, 0, 0, 100, false);

        var rows = repo.GetExtractionRuns(10);
        Assert.Equal(2, rows.Count);
        // Newest first (by RunId DESC).
        Assert.Equal("ApplicationSuite", rows[0].Model);
        Assert.Equal(42_000, rows[0].ElapsedMs);
        Assert.False(rows[0].IsCustom);

        var filtered = repo.GetExtractionRuns(10, "Contoso");
        Assert.Single(filtered);
        Assert.True(filtered[0].IsCustom);
    }

    [Fact]
    public void ApplyExtract_stamps_fingerprint_and_LastExtractedUtc()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();
        var batch = ExtractBatch.Empty("Contoso") with { Publisher = "Contoso", Layer = "usr", IsCustom = true };
        repo.ApplyExtract(batch, sourceFingerprint: "42:1234567890");

        var fps = repo.GetModelFingerprints();
        Assert.Equal("42:1234567890", fps["Contoso"]);

        // Re-apply without fingerprint should NOT wipe it (COALESCE in UPDATE).
        repo.ApplyExtract(ExtractBatch.Empty("Contoso") with { IsCustom = true }, sourceFingerprint: null);
        Assert.Equal("42:1234567890", repo.GetModelFingerprints()["Contoso"]);
    }

    [Fact]
    public void GetDependencyGraph_deduplicates_edges()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();

        // Insert two models and manually add duplicate dependency rows via raw SQL.
        repo.UpsertModel("Fleet", null, null, true);
        repo.UpsertModel("ApplicationSuite", null, null, false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Deliberately insert the same dependency edge twice to simulate re-extraction.
        cmd.CommandText = @"
            INSERT INTO ModelDependencies(ModelId, Target)
                SELECT ModelId, 'ApplicationSuite' FROM Models WHERE Name='Fleet';
            INSERT INTO ModelDependencies(ModelId, Target)
                SELECT ModelId, 'ApplicationSuite' FROM Models WHERE Name='Fleet';";
        cmd.ExecuteNonQuery();

        var graph = repo.GetDependencyGraph();
        var fleetDeps = graph["Fleet"];
        // Must deduplicate: only one entry for ApplicationSuite despite two DB rows.
        Assert.Single(fleetDeps, d => string.Equals(d, "ApplicationSuite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetClassDetails_returns_class_and_methods()
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();

        var batch = ExtractBatch.Empty("Fleet") with
        {
            Classes = new[]
            {
                new ExtractedClass("FleetService", null, false, true, "/x.xml",
                    new[]
                    {
                        new ExtractedMethod("run", "public void run()", "void", false),
                        new ExtractedMethod("init", "public void init()", "void", false),
                    })
            }
        };
        repo.ApplyExtract(batch);

        var detail = repo.GetClassDetails("FleetService");
        Assert.NotNull(detail);
        Assert.Equal("FleetService", detail!.Class.Name);
        Assert.Equal(2, detail.Methods.Count);
        Assert.Contains(detail.Methods, m => m.Name == "run");
        Assert.Contains(detail.Methods, m => m.Name == "init");
    }
}
