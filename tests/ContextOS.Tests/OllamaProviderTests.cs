using System.Net;
using System.Net.Sockets;
using ContextOS.Embeddings;

namespace ContextOS.Tests;

public sealed class OllamaProviderTests
{
    private const string Url   = "http://localhost:11434";
    private const string Model = "nomic-embed-text";

    private static OllamaProvider Provider(FakeHttpMessageHandler h) =>
        new(Url, Model, new HttpClient(h));

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmbedAsync_HappyPath_ReturnsL2NormalizedVector()
    {
        // Raw vector with known norm (3-4-5 right triangle scaled by factor).
        float[] raw = [3f, 4f, 0f]; // norm = 5
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.OkJson("""{"embedding":[3,4,0]}"""));

        using OllamaProvider provider = Provider(handler);
        float[] result = await provider.EmbedAsync("test");

        Assert.Equal(3, result.Length);
        // Expect normalized: [0.6, 0.8, 0]
        Assert.Equal(0.6f, result[0], precision: 5);
        Assert.Equal(0.8f, result[1], precision: 5);
        Assert.Equal(0.0f, result[2], precision: 5);
    }

    [Fact]
    public async Task EmbedBatchAsync_SequentialCalls_ReturnsAllResults()
    {
        int callCount = 0;
        string[] inputs = ["first", "second", "third"];
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            return FakeHttpMessageHandler.OkJson("""{"embedding":[1,0,0]}""");
        });

        using OllamaProvider provider = Provider(handler);
        float[][] results = await provider.EmbedBatchAsync(inputs);

        Assert.Equal(3, results.Length);
        Assert.Equal(3, callCount); // one HTTP call per input (sequential batch)
    }

    // -------------------------------------------------------------------------
    // Error handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmbedAsync_ConnectionRefused_ThrowsFriendlyError()
    {
        var socketEx = new SocketException((int)SocketError.ConnectionRefused);
        var httpEx = new HttpRequestException("Connection refused", socketEx);
        var handler = new FakeHttpMessageHandler(_ =>
            Task.FromException<HttpResponseMessage>(httpEx));

        using OllamaProvider provider = Provider(handler);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync("test"));

        Assert.Contains("Ollama not reachable", ex.Message);
        Assert.Contains(Url, ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_ServerError_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"model not found\"}")
            });

        using OllamaProvider provider = Provider(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.EmbedAsync("test"));
    }

    [Fact]
    public async Task EmbedAsync_ErrorFieldInResponse_ThrowsInvalidOperation()
    {
        // Ollama returns 200 but with an error field in some cases.
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.OkJson("""{"error":"pull model manifest: file does not exist"}"""));

        using OllamaProvider provider = Provider(handler);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync("test"));

        Assert.Contains("Ollama returned error", ex.Message);
    }

    // -------------------------------------------------------------------------
    // HTTP request shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmbedAsync_SendsModelAndPromptInBody()
    {
        string? capturedBody = null;
        var handler = new FakeHttpMessageHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return FakeHttpMessageHandler.OkJson("""{"embedding":[1,0]}""");
        });

        using OllamaProvider provider = Provider(handler);
        await provider.EmbedAsync("hello world");

        Assert.NotNull(capturedBody);
        Assert.Contains("nomic-embed-text", capturedBody);
        Assert.Contains("hello world", capturedBody);
    }
}
