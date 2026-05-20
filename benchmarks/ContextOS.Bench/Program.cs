using System.Diagnostics;
using System.Text;
using ContextOS.Core;
using ContextOS.Embeddings;
using ContextOS.Retrieval;
using ContextOS.Storage;

// ContextOS micro-benchmark
// Usage: dotnet run --project benchmarks/ContextOS.Bench
// Measures insert, recall, and hydrate performance over a temp workspace.
// Uses the ONNX provider if the model file is present; otherwise uses a
// fixed random-vector provider so the benchmark runs without external deps.

const int InsertCount = 1000;
const int RecallRuns = 5;

string tempDir = Path.Combine(Path.GetTempPath(), $"contextos-bench-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);
string dbPath = Path.Combine(tempDir, "bench.db");

Console.WriteLine("ContextOS benchmark");
Console.WriteLine($"  {InsertCount} inserts, {RecallRuns} recalls, 1 hydrate");
Console.WriteLine();

IEmbeddingsProvider provider = CreateProvider();
Console.WriteLine($"  Provider: {provider.Name} (dim={provider.Dimension})");
Console.WriteLine();

try
{
    // ----------------------------------------------------------------
    // Open store
    // ----------------------------------------------------------------
    SqliteStore store = await SqliteStore.OpenAsync(dbPath, provider);
    var workspace = new Workspace("bench", tempDir, "bench", null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    await store.UpsertWorkspaceAsync(workspace);
    var search = new HybridSearch(store.Connection, provider);

    // ----------------------------------------------------------------
    // 1. Insert 1000 memories
    // ----------------------------------------------------------------
    var insertSw = Stopwatch.StartNew();
    for (int i = 0; i < InsertCount; i++)
    {
        await store.AddMemoryAsync(
            "bench",
            i % 4 == 0 ? "decision" : i % 4 == 1 ? "gotcha" : i % 4 == 2 ? "todo" : "note",
            GenerateContent(i),
            importance: (i % 10) / 10.0);
    }
    insertSw.Stop();

    double msPerInsert = (double)insertSw.ElapsedMilliseconds / InsertCount;
    Console.WriteLine($"Insert:  {insertSw.ElapsedMilliseconds}ms total, {msPerInsert:F1}ms/memory  " +
                      (msPerInsert < 50 ? "(PASS)" : "(SLOW — >50ms target)"));

    // ----------------------------------------------------------------
    // 2. Recall (5 different queries, take the worst)
    // ----------------------------------------------------------------
    string[] queries = ["authentication middleware", "database migration", "deployment pipeline",
                        "caching strategy", "error handling"];
    long worstRecall = 0;
    for (int r = 0; r < RecallRuns; r++)
    {
        var sw = Stopwatch.StartNew();
        await search.SearchAsync("bench", queries[r % queries.Length], k: 5);
        sw.Stop();
        if (sw.ElapsedMilliseconds > worstRecall) worstRecall = sw.ElapsedMilliseconds;
    }
    Console.WriteLine($"Recall:  {worstRecall}ms worst-of-{RecallRuns}  " +
                      (worstRecall < 200 ? "(PASS)" : "(SLOW — >200ms target)"));

    // ----------------------------------------------------------------
    // 3. Hydration blob assembly
    // ----------------------------------------------------------------
    var hydrateSw = Stopwatch.StartNew();
    string blob = await HydrationBuilder.BuildAsync(store, "bench", "bench", gitInfo: null);
    hydrateSw.Stop();
    Console.WriteLine($"Hydrate: {hydrateSw.ElapsedMilliseconds}ms, {blob.Length} chars  " +
                      (hydrateSw.ElapsedMilliseconds < 100 ? "(PASS)" : "(SLOW — >100ms target)"));

    // ----------------------------------------------------------------
    // Write PERFORMANCE.md
    // ----------------------------------------------------------------
    string repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();
    string perfPath = Path.Combine(repoRoot, "docs", "PERFORMANCE.md");
    string summary = $"| {DateTimeOffset.UtcNow:yyyy-MM-dd} | {provider.Name} | " +
                     $"{msPerInsert:F1}ms/insert | {worstRecall}ms recall | {hydrateSw.ElapsedMilliseconds}ms hydrate |";
    WritePerfMd(perfPath, summary);
    Console.WriteLine();
    Console.WriteLine($"Results appended to {perfPath}");
}
finally
{
    if (provider is IDisposable dp) dp.Dispose();
    try { Directory.Delete(tempDir, recursive: true); } catch { }
}

// -------------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------------

static IEmbeddingsProvider CreateProvider()
{
    try
    {
        // Try the ONNX provider if the model files are present.
        return OnnxMiniLmProvider.Create();
    }
    catch
    {
        // Fall back to random vectors — bench still measures storage/retrieval.
        return new RandomEmbeddingsProvider(dim: 384);
    }
}

static string GenerateContent(int i)
{
    string[] verbs = ["Use", "Avoid", "Prefer", "Never", "Always", "Consider"];
    string[] nouns = ["Redis", "Postgres", "S3", "Kafka", "gRPC", "REST", "GraphQL", "SQLite"];
    string[] objects = ["for caching", "for persistence", "for messaging", "for auth", "for logging"];
    return $"{verbs[i % verbs.Length]} {nouns[i % nouns.Length]} {objects[i % objects.Length]}. Memory #{i}.";
}

static string? FindRepoRoot()
{
    string? dir = AppContext.BaseDirectory;
    while (dir is not null)
    {
        if (Directory.GetFiles(dir, "*.slnx").Length > 0) return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

static void WritePerfMd(string path, string newRow)
{
    string header =
        "# ContextOS Performance\n\n" +
        "Results from running `dotnet run --project benchmarks/ContextOS.Bench`.\n\n" +
        "| Date | Provider | Insert | Recall (worst of 5) | Hydrate |\n" +
        "|------|----------|--------|---------------------|---------|";

    if (!File.Exists(path))
        File.WriteAllText(path, header + "\n" + newRow + "\n", Encoding.UTF8);
    else
        File.AppendAllText(path, newRow + "\n", Encoding.UTF8);
}

/// <summary>Returns deterministic random unit vectors. Useful for benchmarks when no real model is available.</summary>
sealed class RandomEmbeddingsProvider : IEmbeddingsProvider, IDisposable
{
    private readonly int _dim;
    private readonly Random _rng = new(42);

    public RandomEmbeddingsProvider(int dim) => _dim = dim;
    public string Name => "random";
    public int Dimension => _dim;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(RandomUnitVector());

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult(texts.Select(_ => RandomUnitVector()).ToArray());

    public void Dispose() { }

    private float[] RandomUnitVector()
    {
        float[] v = new float[_dim];
        double norm = 0;
        for (int i = 0; i < _dim; i++)
        {
            v[i] = (float)(_rng.NextDouble() * 2 - 1);
            norm += v[i] * (double)v[i];
        }
        norm = Math.Sqrt(norm);
        for (int i = 0; i < _dim; i++) v[i] = (float)(v[i] / norm);
        return v;
    }
}
