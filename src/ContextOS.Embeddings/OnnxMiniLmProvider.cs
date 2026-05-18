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
    /// Loads the model from <paramref name="modelsDir"/> (defaults to
    /// <c>AppContext.BaseDirectory/Models</c>). Throws <see cref="FileNotFoundException"/>
    /// with instructions to run <c>scripts/fetch-model.sh</c> if files are absent.
    /// </summary>
    public static OnnxMiniLmProvider Create(string? modelsDir = null)
    {
        string dir = modelsDir ?? Path.Combine(AppContext.BaseDirectory, "Models");
        string modelPath = Path.Combine(dir, "all-MiniLM-L6-v2.onnx");
        string vocabPath = Path.Combine(dir, "vocab.txt");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ONNX model not found at '{modelPath}'. " +
                "Download it by running: bash scripts/fetch-model.sh",
                modelPath);

        if (!File.Exists(vocabPath))
            throw new FileNotFoundException(
                $"Tokenizer vocabulary not found at '{vocabPath}'. " +
                "Download it by running: bash scripts/fetch-model.sh",
                vocabPath);

        var opts = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        return new OnnxMiniLmProvider(
            new InferenceSession(modelPath, opts),
            new BertWordPieceTokenizer(vocabPath));
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
