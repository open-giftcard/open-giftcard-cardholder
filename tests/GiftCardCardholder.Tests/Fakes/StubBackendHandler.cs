using System.Net;
using System.Text;

namespace GiftCardCardholder.Tests.Fakes;

/// <summary>
/// Stands in for the Gift Card Platform API. Responses are queued per path so a
/// test can script the exact backend behaviour it wants — including the
/// password-required refusal that drives the activation branch.
/// </summary>
internal sealed class StubBackendHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<HttpResponseMessage>> responses =
        new(StringComparer.OrdinalIgnoreCase);

    public List<RecordedRequest> Requests { get; } = [];

    public void Enqueue(string pathSuffix, HttpStatusCode status, string json)
    {
        if (!responses.TryGetValue(pathSuffix, out var queue))
        {
            queue = new Queue<HttpResponseMessage>();
            responses[pathSuffix] = queue;
        }

        queue.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    public void EnqueueProblem(string pathSuffix, HttpStatusCode status, string code) =>
        Enqueue(
            pathSuffix,
            status,
            $$"""
            {"status":{{(int)status}},"title":"Problem.","code":"{{code}}",
             "correlationId":"00000000-0000-0000-0000-000000000001"}
            """);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(", ", header.Value),
            StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedRequest(
            request.Method.Method,
            path,
            request.RequestUri.PathAndQuery,
            body,
            headers));

        foreach (var (suffix, queue) in responses)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && queue.Count > 0)
            {
                return queue.Dequeue();
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{"status":404,"title":"Not found.","code":"stub.unconfigured"}""",
                Encoding.UTF8,
                "application/json"),
        };
    }

    internal sealed record RecordedRequest(
        string Method,
        string Path,
        string PathAndQuery,
        string? Body,
        IReadOnlyDictionary<string, string> Headers)
    {
        public string? Header(string name) =>
            Headers.TryGetValue(name, out var value) ? value : null;
    }
}
