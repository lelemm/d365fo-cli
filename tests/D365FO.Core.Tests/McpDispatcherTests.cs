using System.Text.Json;
using D365FO.Core.Index;
using D365FO.Mcp;
using Xunit;

namespace D365FO.Core.Tests;

public class McpDispatcherTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"d365fo-mcp-{Guid.NewGuid():N}.sqlite");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) File.Delete(p);
        }
    }

    private async Task<List<JsonDocument>> Roundtrip(params string[] requests)
    {
        var repo = new MetadataRepository(_dbPath);
        repo.EnsureSchema();
        var dispatcher = new StdioDispatcher(new ToolHandlers(repo));

        using var input = new StringReader(string.Join('\n', requests) + '\n');
        using var output = new StringWriter();
        await dispatcher.RunAsync(input, output);

        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => JsonDocument.Parse(s))
            .ToList();
    }

    [Fact]
    public async Task Initialize_returns_protocol_version_and_capabilities()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        var doc = Assert.Single(resp);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
        Assert.True(result.GetProperty("capabilities").GetProperty("tools").ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task ToolsList_returns_non_empty_catalog()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var doc = Assert.Single(resp);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.True(tools.GetArrayLength() >= 10);
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToHashSet();
        Assert.Contains("search", names);
        Assert.Contains("get_object_info", names);
        Assert.Contains("index_status", names);
    }

    [Fact]
    public async Task UnknownMethod_returns_jsonrpc_error()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","id":3,"method":"does/not/exist"}""");
        var doc = Assert.Single(resp);
        var err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32601, err.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ToolsCall_wraps_tool_result_in_content_block()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"index_status","arguments":{}}}""");
        var doc = Assert.Single(resp);
        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        var first = content[0];
        Assert.Equal("text", first.GetProperty("type").GetString());
        var payload = JsonDocument.Parse(first.GetProperty("text").GetString()!);
        Assert.True(payload.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Notification_does_not_get_a_reply()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        Assert.Empty(resp);
    }

    [Fact]
    public async Task ToolsList_exposes_unified_tools()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","id":10,"method":"tools/list"}""");
        var doc = Assert.Single(resp);
        var names = doc.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToHashSet();
        // The consolidated, discriminator-based tool surface.
        Assert.Contains("search", names);
        Assert.Contains("get_object_info", names);
        Assert.Contains("get_method", names);
        Assert.Contains("labels", names);
        Assert.Contains("security_info", names);
        Assert.Contains("find_extensions", names);
        Assert.Contains("form_pattern", names);
        Assert.Contains("generate", names);
        Assert.Contains("models", names);
        Assert.Contains("analyze", names);
        // The old per-type tools must be gone.
        Assert.DoesNotContain("search_classes", names);
        Assert.DoesNotContain("get_data_entity", names);
        Assert.DoesNotContain("list_models", names);
    }

    [Fact]
    public async Task Models_list_returns_empty_collection_for_fresh_db()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"models","arguments":{"action":"list"}}}""");
        var doc = Assert.Single(resp);
        var payload = JsonDocument.Parse(doc.RootElement.GetProperty("result")
            .GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.True(payload.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, payload.RootElement.GetProperty("data").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GetObjectInfo_unknown_service_returns_structured_notFound_error()
    {
        var resp = await Roundtrip("""{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"get_object_info","arguments":{"objectType":"service","name":"DoesNotExist"}}}""");
        var doc = Assert.Single(resp);
        var payload = JsonDocument.Parse(doc.RootElement.GetProperty("result")
            .GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.False(payload.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("SERVICE_NOT_FOUND", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("class")]
    [InlineData("table")]
    [InlineData("enum")]
    [InlineData("form")]
    public async Task GetObjectInfo_dispatches_per_objectType(string objectType)
    {
        var req = "{\"jsonrpc\":\"2.0\",\"id\":13,\"method\":\"tools/call\",\"params\":{\"name\":\"get_object_info\",\"arguments\":{\"objectType\":\""
                  + objectType + "\",\"name\":\"Nope\"}}}";
        var resp = await Roundtrip(req);
        var doc = Assert.Single(resp);
        var payload = JsonDocument.Parse(doc.RootElement.GetProperty("result")
            .GetProperty("content")[0].GetProperty("text").GetString()!);
        // Each objectType routes to its reader; an absent object yields a typed not-found.
        Assert.False(payload.RootElement.GetProperty("ok").GetBoolean());
        var code = payload.RootElement.GetProperty("error").GetProperty("code").GetString();
        Assert.EndsWith("_NOT_FOUND", code);
    }
}
