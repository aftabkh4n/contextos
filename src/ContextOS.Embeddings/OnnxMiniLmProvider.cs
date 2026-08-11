using ContextOS.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ContextOS.Embeddings;

/// <summary>
/// Runs all-MiniLM-L6-v2 via ONNX Runtime and returns 384-dim L2-normalised vectors.
/// Use <see cref="Create"/> to construct — it validates that model files are present.
/// </summary>
public sealed class OnnxMiniLmProvider : IEmbeddingsProvider, IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertWordPieceTokenizer _tokenizer;
    private const int Dim = 384;

    public string Name => "onnx";
    public int Dimension => Dim;

    private OnnxMiniLmProvider(InferenceSession session, BertWordPieceTokenizer tokenizer)
    {
        _session = session;
        _tokenizer = tokenizer;
    }

    /// <summary>
    /// Loads the model from <paramref name="modelsDir"/>, or searches a set of candidate
    /// directories when <paramref name="modelsDir"/> is <see langword="null"/>. Throws
    /// <see cref="FileNotFoundException"/> listing every path checked when no files are found.
    /// </summary>
    public static OnnxMiniLmProvider Create(string? modelsDir = null)
    {
        List<string> candidateDirs;
        if (modelsDir is not null)
        {
            candidateDirs = [modelsDir];
        }
        else
        {
            candidateDirs = new List<string>();

            // 1. Next to the DLL — populated via CopyToOutputDirectory in the csproj.
            candidateDirs.Add(Path.Combine(AppContext.BaseDirectory, "Models"));

            // 2. Source Models dir when running from a bin/Debug/net10.0/ layout.
            //    Walk up 4 levels (net10.0 -> Debug -> bin -> project) to arrive at src/,
            //    then step into the Embeddings project's Models folder.
            candidateDirs.Add(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "ContextOS.Embeddings", "Models"));

            // 3. Repo-root relative path — covers CI where build precedes model fetch.
            string? repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
            if (repoRoot is not null)
                candidateDirs.Add(Path.Combine(repoRoot, "src", "ContextOS.Embeddings", "Models"));
        }

        string? resolvedDir = null;
        foreach (string candidate in candidateDirs)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(full, "all-MiniLM-L6-v2.onnx")) &&
                File.Exists(Path.Combine(full, "vocab.txt")))
            {
                resolvedDir = full;
                break;
            }
        }

        if (resolvedDir is null)
        {
            string checkedList = string.Join(
                Environment.NewLine,
                candidateDirs.Select(d => "  " + Path.GetFullPath(d)));
            throw new FileNotFoundException(
                $"ONNX model files (all-MiniLM-L6-v2.onnx + vocab.txt) not found in any of:{Environment.NewLine}" +
                $"{checkedList}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"The ONNX model is not bundled in the .NET tool package.{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"Options:{Environment.NewLine}" +
                $"  1. Download the model manually:{Environment.NewLine}" +
                $"       mkdir -p ~/.contextos/models{Environment.NewLine}" +
                $"       curl -L -o ~/.contextos/models/all-MiniLM-L6-v2.onnx \\{Environment.NewLine}" +
                $"         https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx{Environment.NewLine}" +
                $"       curl -L -o ~/.contextos/models/vocab.txt \\{Environment.NewLine}" +
                $"         https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt{Environment.NewLine}" +
                $"     Then add to ~/.contextos/config.json:{Environment.NewLine}" +
                $"       {{\"embeddings\":{{\"modelsDir\":\"~/.contextos/models\"}}}}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"  2. Use Ollama instead (no model download needed):{Environment.NewLine}" +
                $"       Install Ollama, then: ollama pull nomic-embed-text{Environment.NewLine}" +
                $"     Then add to ~/.contextos/config.json:{Environment.NewLine}" +
                $"       {{\"embeddings\":{{\"provider\":\"ollama\"}}}}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                $"  3. Use the platform binary (includes the model):{Environment.NewLine}" +
                $"       https://github.com/aftabkh4n/contextos/releases/latest");
        }

        string modelPath = Path.Combine(resolvedDir, "all-MiniLM-L6-v2.onnx");
        string vocabPath = Path.Combine(resolvedDir, "vocab.txt");

        var opts = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        return new OnnxMiniLmProvider(
            new InferenceSession(modelPath, opts),
            new BertWordPieceTokenizer(vocabPath));
    }

    private static string? FindRepoRoot(string start)
    {
        string? dir = start;
        while (dir is not null)
        {
            if (Directory.GetFiles(dir, "ContextOS.slnx").Length > 0)
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RunInference(text));
    }

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            results[i] = RunInference(texts[i]);
        }
        return Task.FromResult(results);
    }

    private float[] RunInference(string text)
    {
        (long[] inputIds, long[] attMask, long[] typeIds) = _tokenizer.Encode(text);
        int seqLen = inputIds.Length;

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(inputIds.AsMemory(), new[] { 1, seqLen })),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(attMask.AsMemory(), new[] { 1, seqLen })),
            NamedOnnxValue.CreateFromTensor("token_type_ids",
                new DenseTensor<long>(typeIds.AsMemory(), new[] { 1, seqLen })),
        };

        using var outputs = _session.Run(inputs);

        // Some exports include a pre-pooled sentence_embedding; use it if present.
        var prePooled = outputs.FirstOrDefault(o => o.Name == "sentence_embedding");
        if (prePooled is not null)
        {
            float[] flat = ((DenseTensor<float>)prePooled.Value).Buffer.ToArray();
            return L2Normalize(flat);
        }

        // Standard sentence-transformers export: mean-pool last_hidden_state.
        var hidden = (DenseTensor<float>)outputs.First(o => o.Name == "last_hidden_state").Value;
        return MeanPoolAndNormalize(hidden, attMask, seqLen);
    }

    private static float[] MeanPoolAndNormalize(DenseTensor<float> hidden, long[] attMask, int seqLen)
    {
        // hidden.Buffer is row-major: element [0, t, d] is at index t*Dim + d
        var buf = hidden.Buffer.Span;
        float maskSum = 0f;
        float[] pooled = new float[Dim];

        for (int t = 0; t < seqLen; t++)
        {
            float m = attMask[t];
            if (m == 0f) continue;
            maskSum += m;
            int offset = t * Dim;
            for (int d = 0; d < Dim; d++)
                pooled[d] += buf[offset + d] * m;
        }

        float invMask = 1f / MathF.Max(maskSum, 1e-9f);
        for (int d = 0; d < Dim; d++)
            pooled[d] *= invMask;

        return L2Normalize(pooled);
    }

    private static float[] L2Normalize(float[] v)
    {
        float norm = 0f;
        for (int i = 0; i < v.Length; i++)
            norm += v[i] * v[i];
        norm = MathF.Sqrt(norm);
        float scale = 1f / MathF.Max(norm, 1e-9f);
        for (int i = 0; i < v.Length; i++)
            v[i] *= scale;
        return v;
    }

    public void Dispose() => _session.Dispose();
}
