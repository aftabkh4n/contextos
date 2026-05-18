using ContextOS.Embeddings;

namespace ContextOS.Tests;

public sealed class EmbeddingsTests
{
    [Fact]
    public async Task Embed_ReturnsCorrectDimension()
    {
        using var provider = OnnxMiniLmProvider.Create();
        float[] vec = await provider.EmbedAsync("hello world");
        Assert.Equal(384, vec.Length);
    }

    [Fact]
    public async Task Embed_IdenticalInputs_ProduceIdenticalVectors()
    {
        using var provider = OnnxMiniLmProvider.Create();
        float[] a = await provider.EmbedAsync("the quick brown fox");
        float[] b = await provider.EmbedAsync("the quick brown fox");
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Embed_DifferentInputs_ProduceDifferentVectors()
    {
        using var provider = OnnxMiniLmProvider.Create();
        float[] a = await provider.EmbedAsync("hello world");
        float[] b = await provider.EmbedAsync("goodbye universe");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Embed_OutputIsL2Normalized()
    {
        using var provider = OnnxMiniLmProvider.Create();
        float[] vec = await provider.EmbedAsync("normalization test");
        double norm = Math.Sqrt(vec.Sum(v => (double)v * v));
        Assert.Equal(1.0, norm, precision: 5);
    }
}
