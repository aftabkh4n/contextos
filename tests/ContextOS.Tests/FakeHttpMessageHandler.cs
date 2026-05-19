namespace ContextOS.Tests;

/// <summary>
/// A minimal <see cref="HttpMessageHandler"/> that delegates to a caller-supplied function.
/// Lets tests intercept <see cref="HttpClient"/> calls without Moq or other mocking libraries.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;

    internal FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        _send = send;

    internal FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) =>
        _send = req => Task.FromResult(send(req));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        _send(request);

    internal static HttpResponseMessage OkJson(string json) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
}
