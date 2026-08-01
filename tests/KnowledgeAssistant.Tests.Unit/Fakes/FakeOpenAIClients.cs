using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI.Embeddings;

namespace KnowledgeAssistant.Tests.Unit.Fakes;

/// <summary>Minimal <see cref="PipelineResponseHeaders"/> over a dictionary.</summary>
internal sealed class FakePipelineHeaders(Dictionary<string, string> values) : PipelineResponseHeaders
{
    public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => values.GetEnumerator();

    public override bool TryGetValue(string name, out string? value) => values.TryGetValue(name, out value);

    public override bool TryGetValues(string name, out IEnumerable<string>? values2)
    {
        if (values.TryGetValue(name, out string? single))
        {
            values2 = [single];
            return true;
        }

        values2 = null;
        return false;
    }
}

/// <summary>
/// A <see cref="PipelineResponse"/> carrying a status and optional headers.
/// </summary>
/// <remarks>
/// Exists so a <see cref="ClientResultException"/> can be constructed with a real
/// status and a real <c>Retry-After</c>. That is what makes the retry policy
/// testable at all: the status decides whether a call is retried, and the header
/// decides how long the wait is.
/// </remarks>
internal sealed class FakePipelineResponse(int status, Dictionary<string, string>? headers = null) : PipelineResponse
{
    private readonly FakePipelineHeaders _headers = new(headers ?? []);

    public override int Status => status;

    public override string ReasonPhrase => "fake";

    public override Stream? ContentStream { get; set; } = new MemoryStream();

    public override BinaryData Content => BinaryData.FromString(string.Empty);

    protected override PipelineResponseHeaders HeadersCore => _headers;

    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Content);

    public override void Dispose() => ContentStream?.Dispose();
}

/// <summary>
/// A scripted <see cref="EmbeddingClient"/>: each call consumes the next
/// behaviour, then falls back to a default.
/// </summary>
/// <remarks>
/// The SDK is designed for this — a protected parameterless constructor and
/// virtual methods — so the adapter is exercised through its real code path
/// rather than around it.
/// </remarks>
internal sealed class FakeEmbeddingClient : EmbeddingClient
{
    private readonly Queue<Func<int, ClientResult<OpenAIEmbeddingCollection>>> _script = new();
    private Func<int, ClientResult<OpenAIEmbeddingCollection>> _default = count => Build(count, 8);

    public List<int> CallInputCounts { get; } = [];

    public List<string[]> CallInputs { get; } = [];

    public int CallCount => CallInputCounts.Count;

    public FakeEmbeddingClient AlwaysSucceed()
    {
        _default = count => Build(count, 8);
        return this;
    }

    public FakeEmbeddingClient AlwaysWrongDimensions(int actual)
    {
        _default = count => Build(count, actual);
        return this;
    }

    public FakeEmbeddingClient AlwaysReturnCount(int returnCount)
    {
        _default = _ => Build(returnCount, 8);
        return this;
    }

    public FakeEmbeddingClient ThenThrow(int status, string? retryAfter = null, int times = 1)
    {
        for (int index = 0; index < times; index++)
        {
            _script.Enqueue(_ => throw new ClientResultException(
                $"status {status}",
                new FakePipelineResponse(
                    status,
                    retryAfter is null ? null : new Dictionary<string, string> { ["retry-after"] = retryAfter }),
                null));
        }

        return this;
    }

    public FakeEmbeddingClient ThenThrowException(Exception exception, int times = 1)
    {
        for (int index = 0; index < times; index++)
        {
            _script.Enqueue(_ => throw exception);
        }

        return this;
    }

    public FakeEmbeddingClient ThenSucceed()
    {
        _script.Enqueue(count => Build(count, 8));
        return this;
    }

    private static ClientResult<OpenAIEmbeddingCollection> Build(int count, int dimensions)
    {
        var items = new List<OpenAIEmbedding>(count);

        for (int index = 0; index < count; index++)
        {
            // The first element encodes the input's position, so a mis-pairing is
            // visible to an assertion.
            var vector = new float[dimensions];

            if (dimensions > 0)
            {
                vector[0] = index;
            }

            items.Add(OpenAIEmbeddingsModelFactory.OpenAIEmbedding(index, vector));
        }

        OpenAIEmbeddingCollection collection = OpenAIEmbeddingsModelFactory.OpenAIEmbeddingCollection(
            items,
            "fake-model",
            OpenAIEmbeddingsModelFactory.EmbeddingTokenUsage(1, 1));

        return ClientResult.FromValue(collection, new FakePipelineResponse(200));
    }

    public override Task<ClientResult<OpenAIEmbeddingCollection>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string[] array = [.. inputs];
        CallInputCounts.Add(array.Length);
        CallInputs.Add(array);

        Func<int, ClientResult<OpenAIEmbeddingCollection>> behaviour =
            _script.Count > 0 ? _script.Dequeue() : _default;

        return Task.FromResult(behaviour(array.Length));
    }
}
