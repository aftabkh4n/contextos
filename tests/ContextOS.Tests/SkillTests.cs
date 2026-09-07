using System.Text.Json;
using ContextOS.Core;
using ContextOS.Embeddings;
using ContextOS.Mcp;
using ContextOS.Mcp.Tools;
using ContextOS.Retrieval;
using ContextOS.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextOS.Tests;

// -------------------------------------------------------------------------
// Unit tests — no ONNX required
// -------------------------------------------------------------------------

public sealed class SkillTests : IAsyncLifetime
{
    private SqliteStore _store = null!;

    private const string Ws   = "ws-skill-tests";
    private const string Name = "skill-workspace";

    public async Task InitializeAsync()
    {
        _store = await SqliteStore.OpenAsync(":memory:");
        await _store.UpsertWorkspaceAsync(
            new Workspace(Ws, "/test/skills", Name, null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public async Task DisposeAsync() => await _store.DisposeAsync();

    private RememberSkillTool MakeTool() =>
        new(_store, new WorkspaceContext(Ws, "/test/skills"),
            NullLogger<RememberSkillTool>.Instance);

    // -------------------------------------------------------------------------
    // remember_skill stores correctly
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RememberSkill_StoresCorrectTypeImportanceDecayDaysAndContent()
    {
        RememberSkillTool tool = MakeTool();

        string result = await tool.RememberSkillAsync(
            name: "deploy to production",
            steps: "1. dotnet publish\n2. copy to server\n3. restart service",
            outcome: "Service is running in production.");

        using JsonDocument doc = JsonDocument.Parse(result);
        string id = doc.RootElement.GetProperty("id").GetString()!;

        Memory? m = await _store.GetMemoryByIdAsync(id);
        Assert.NotNull(m);
        Assert.Equal(MemoryTypes.Skill, m.Type);
        Assert.Equal(0.8, m.Importance);
        Assert.Equal(0, m.DecayDays);
        Assert.Contains("Skill:", m.Content);
        Assert.Contains("Steps:", m.Content);
        Assert.Contains("Outcome:", m.Content);
        Assert.Contains("deploy to production", m.Content);
    }

    [Fact]
    public async Task RememberSkill_ReturnsMessageWithSkillName()
    {
        RememberSkillTool tool = MakeTool();

        string result = await tool.RememberSkillAsync(
            name: "run the migration",
            steps: "1. backup\n2. dotnet ef database update",
            outcome: "Schema is up to date.");

        using JsonDocument doc = JsonDocument.Parse(result);
        string? message = doc.RootElement.GetProperty("message").GetString();
        Assert.Equal("Skill 'run the migration' remembered.", message);
    }

    [Fact]
    public async Task RememberSkill_WithCustomImportance_StoresCorrectly()
    {
        RememberSkillTool tool = MakeTool();

        string result = await tool.RememberSkillAsync(
            name: "hotfix process",
            steps: "1. branch from main\n2. fix\n3. PR\n4. deploy",
            outcome: "Fix is live on production.",
            importance: 0.95);

        using JsonDocument doc = JsonDocument.Parse(result);
        string id = doc.RootElement.GetProperty("id").GetString()!;

        Memory? m = await _store.GetMemoryByIdAsync(id);
        Assert.NotNull(m);
        Assert.Equal(0.95, m.Importance, precision: 6);
        Assert.Equal(0, m.DecayDays);
    }

    // -------------------------------------------------------------------------
    // context tool shows Skills section
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Context_ShowsSkillsSectionHeader_WhenNoSkillsExist()
    {
        string md = await ContextBuilder.BuildAsync(_store, Ws, Name);

        Assert.Contains("## Skills", md);
        Assert.Contains("None recorded.", md);
    }

    [Fact]
    public async Task Context_ShowsStoredSkill_InSkillsSection()
    {
        RememberSkillTool tool = MakeTool();
        await tool.RememberSkillAsync(
            name: "roll back a deployment",
            steps: "1. find the previous image tag\n2. helm rollback",
            outcome: "Previous version is live.");

        string md = await ContextBuilder.BuildAsync(_store, Ws, Name);

        Assert.Contains("## Skills", md);
        Assert.Contains("roll back a deployment", md);
        Assert.DoesNotContain("None recorded.", md);
    }

    // -------------------------------------------------------------------------
    // hydration blob includes skill
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Hydration_IncludesSkill_InAutoInjectedContext()
    {
        RememberSkillTool tool = MakeTool();
        await tool.RememberSkillAsync(
            name: "create a hotfix branch",
            steps: "1. git checkout main\n2. git checkout -b hotfix/xxx",
            outcome: "Hotfix branch is ready.");

        string blob = await HydrationBuilder.BuildAsync(_store, Ws, Name, gitInfo: null);

        Assert.Contains("create a hotfix branch", blob);
    }

    // -------------------------------------------------------------------------
    // decay: skill with decay_days=0 is never archived
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Skill_WithDecayDaysZero_IsNotArchivedByDecayPass()
    {
        RememberSkillTool tool = MakeTool();
        string result = await tool.RememberSkillAsync(
            name: "pin this skill",
            steps: "1. do the thing",
            outcome: "Thing is done.");

        using JsonDocument doc = JsonDocument.Parse(result);
        string id = doc.RootElement.GetProperty("id").GetString()!;

        // Back-date created_at by 500 days to simulate an old skill.
        long oldTs = DateTimeOffset.UtcNow.AddDays(-500).ToUnixTimeMilliseconds();
        using var cmd = _store.Connection.CreateCommand();
        cmd.CommandText = "UPDATE memories SET created_at = @v WHERE id = @id";
        cmd.Parameters.AddWithValue("@v", oldTs);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();

        var decayService = new DecayService(new MemoryConfig(DecayDays: 90));
        await decayService.RunDecayPassAsync(_store, NullLogger.Instance);

        Memory? after = await _store.GetMemoryByIdAsync(id);
        Assert.NotNull(after);
        Assert.Null(after.ArchivedAt);
    }
}

// -------------------------------------------------------------------------
// Retrieval tests — requires ONNX model
// -------------------------------------------------------------------------

public sealed class SkillRetrievalTests : IAsyncLifetime
{
    private OnnxMiniLmProvider _provider = null!;
    private SqliteStore _store = null!;
    private HybridSearch _search = null!;

    private const string Ws = "ws-skill-retrieval";

    public async Task InitializeAsync()
    {
        _provider = OnnxMiniLmProvider.Create();
        _store = await SqliteStore.OpenAsync(":memory:", _provider);
        _search = new HybridSearch(_store.Connection, _provider);

        await _store.UpsertWorkspaceAsync(
            new Workspace(Ws, "/test/skill-retrieval", "SkillRetrievalTests", null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        _provider.Dispose();
    }

    private Task<Memory> AddSkill(string name, string steps, string outcome, string? tags = null) =>
        _store.AddMemoryAsync(
            Ws,
            MemoryTypes.Skill,
            $"Skill: {name}\n\nSteps:\n{steps}\n\nOutcome: {outcome}",
            tags: tags,
            importance: 0.8,
            decayDays: 0);

    [Fact]
    public async Task Recall_FindsSkill_ViaKeywordSearch()
    {
        Memory skill = await AddSkill(
            "deploy to production",
            "1. dotnet publish\n2. copy artifacts\n3. restart service",
            "Service is running in production.");

        // Noise
        await _store.AddMemoryAsync(Ws, MemoryTypes.Note, "standup is at 9am");
        await _store.AddMemoryAsync(Ws, MemoryTypes.Note, "remember to water the plants");

        IReadOnlyList<SearchResult> results = await _search.SearchAsync(Ws, "deploy to production", k: 5);

        Assert.Contains(results, r => r.Id == skill.Id);
    }

    [Fact]
    public async Task Recall_FindsSkill_ViaSemanticSearch()
    {
        Memory skill = await AddSkill(
            "release to live environment",
            "1. tag the release\n2. push artifacts\n3. restart pods",
            "Application is live.");

        // Noise — lexically different but on unrelated topics
        await _store.AddMemoryAsync(Ws, MemoryTypes.Note, "team lunch is on Friday");
        await _store.AddMemoryAsync(Ws, MemoryTypes.Note, "the parking permit expires next month");
        await _store.AddMemoryAsync(Ws, MemoryTypes.Note, "order more coffee for the office");

        // Semantically related query that shares no keywords with the skill content
        IReadOnlyList<SearchResult> results = await _search.SearchAsync(Ws, "ship code to production", k: 5);

        Assert.Contains(results, r => r.Id == skill.Id);
    }
}
