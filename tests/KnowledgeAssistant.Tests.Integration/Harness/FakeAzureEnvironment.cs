using System.Collections.Concurrent;
using System.Globalization;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Tests.Integration.Harness;

/// <summary>
/// In-memory stand-ins for the Azure-backed ports, wired together so the corpus
/// a test uploads is the corpus it can then ask questions about.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these are not the unit suite's fakes.</b> Those exist to script one
/// scenario at a time — a stage that fails, embeddings returned in the wrong
/// order — and they record a stage trace because that is what a unit test
/// asserts against. These have the opposite job: behave like a plausible Azure
/// for a whole request, and keep enough state that ingestion and retrieval agree
/// with each other. Making one type serve both purposes would leave each doing
/// its job badly, so they are separate on purpose rather than duplicated by
/// omission.
/// </para>
/// <para>
/// <b>What is deliberately not faked.</b> Chunking stays real. It is local
/// (PdfPig, no network), so substituting it would remove the one part of
/// ingestion that can genuinely be exercised end to end here — and an upload
/// test that never extracts text from a real PDF proves considerably less.
/// </para>
/// <para>
/// Each port exposes a settable <see cref="Error"/> so a test can make one
/// dependency fail and assert the resulting ProblemDetails. That is the only
/// scripting these need.
/// </para>
/// </remarks>
internal sealed class FakeAzureEnvironment
{
    public FakeBlobStorage Blob { get; } = new();

    public FakeEmbeddings Embeddings { get; } = new();

    public FakeVectorIndex VectorIndex { get; } = new();

    public FakeSearchIndex SearchIndex { get; }

    public FakeChat Chat { get; } = new();

    public FakeAgent Agent { get; }

    public FakeAzureEnvironment()
    {
        SearchIndex = new FakeSearchIndex(VectorIndex);

        // The agent searches through the same fake index ingestion wrote to, so
        // "upload a document, then ask the agent about it" is one connected test
        // rather than two sharing a process — exactly as it is for the retrieval
        // endpoint. Substituting the agent port with something that invented its
        // own sources would make the agent endpoint's tests prove nothing about
        // whether the corpus reaches it.
        Agent = new FakeAgent(Embeddings, SearchIndex);
    }
}

/// <summary>Blob storage as a dictionary.</summary>
internal sealed class FakeBlobStorage : IBlobStorageService
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public Error? UploadError { get; set; }

    public Error? DownloadError { get; set; }

    public int Count => _blobs.Count;

    public async Task<Result<BlobUploadResult>> UploadAsync(
        Guid documentId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
    {
        if (UploadError is not null)
        {
            return Result.Failure<BlobUploadResult>(UploadError);
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        byte[] bytes = buffer.ToArray();

        string blobName = $"2026/08/01/{documentId}.pdf";
        _blobs[blobName] = bytes;

        return Result.Success(new BlobUploadResult(
            blobName,
            new Uri($"https://fake.blob.core.windows.net/documents/{blobName}"),
            bytes.LongLength));
    }

    public Task<Result<Stream>> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        if (DownloadError is not null)
        {
            return Task.FromResult(Result.Failure<Stream>(DownloadError));
        }

        // Returning what was actually stored is the point: it makes the handler's
        // "upload then re-download before chunking" round trip real, so a
        // regression that chunks the consumed request stream fails here.
        return Task.FromResult(_blobs.TryGetValue(blobName, out byte[]? bytes)
            ? Result.Success<Stream>(new MemoryStream(bytes))
            : Result.Failure<Stream>(Error.Failure("Storage.DownloadFailed", $"No blob named {blobName}.")));
    }
}

/// <summary>
/// Deterministic embeddings derived from the text itself.
/// </summary>
/// <remarks>
/// Derived rather than constant so that two different chunks get two different
/// vectors, which is what lets the fake search index rank at all. The values
/// carry no semantic meaning and are not meant to.
/// </remarks>
internal sealed class FakeEmbeddings : IEmbeddingService
{
    private const int Dimensions = 8;

    public Error? Error { get; set; }

    public int EmbedTextCallCount { get; private set; }

    public Task<Result<IReadOnlyList<ChunkEmbedding>>> GenerateEmbeddingsAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (Error is not null)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<ChunkEmbedding>>(Error));
        }

        IReadOnlyList<ChunkEmbedding> embeddings =
            [.. chunks.Select(chunk => new ChunkEmbedding(chunk.ChunkId, Vector(chunk.Text)))];

        return Task.FromResult(Result.Success(embeddings));
    }

    public Task<Result<ReadOnlyMemory<float>>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken)
    {
        EmbedTextCallCount++;

        return Task.FromResult(Error is not null
            ? Result.Failure<ReadOnlyMemory<float>>(Error)
            : Result.Success(Vector(text)));
    }

    private static ReadOnlyMemory<float> Vector(string text)
    {
        var vector = new float[Dimensions];

        for (int index = 0; index < text.Length; index++)
        {
            vector[index % Dimensions] += text[index] / 1000f;
        }

        return vector;
    }
}

/// <summary>The chunk index, as a list.</summary>
internal sealed class FakeVectorIndex : IVectorIndexService
{
    private readonly List<(VectorIndexRequest Request, VectorIndexChunk Chunk)> _chunks = [];

    public Error? Error { get; set; }

    public IReadOnlyList<(VectorIndexRequest Request, VectorIndexChunk Chunk)> Chunks
    {
        get
        {
            lock (_chunks)
            {
                return [.. _chunks];
            }
        }
    }

    public Task<Result> IndexChunksAsync(VectorIndexRequest request, CancellationToken cancellationToken)
    {
        if (Error is not null)
        {
            return Task.FromResult(Result.Failure(Error));
        }

        lock (_chunks)
        {
            _chunks.AddRange(request.Chunks.Select(chunk => (request, chunk)));
        }

        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// The document index, plus retrieval served from whatever the vector index holds.
/// </summary>
/// <remarks>
/// Retrieval reads the chunks ingestion actually wrote, so "upload a document,
/// then ask a question about it" is a single connected test rather than two
/// unrelated ones sharing a fixture.
/// </remarks>
internal sealed class FakeSearchIndex(FakeVectorIndex vectorIndex) : IAzureSearchService
{
    private readonly List<DocumentIndexRequest> _documents = [];

    public Error? IndexError { get; set; }

    public Error? SearchError { get; set; }

    public IReadOnlyList<DocumentIndexRequest> Documents
    {
        get
        {
            lock (_documents)
            {
                return [.. _documents];
            }
        }
    }

    public Task<Result> IndexDocumentAsync(DocumentIndexRequest request, CancellationToken cancellationToken)
    {
        if (IndexError is not null)
        {
            return Task.FromResult(Result.Failure(IndexError));
        }

        lock (_documents)
        {
            _documents.Add(request);
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result<IReadOnlyList<ChunkSearchResult>>> SearchChunksAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        if (SearchError is not null)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<ChunkSearchResult>>(SearchError));
        }

        IReadOnlyList<ChunkSearchResult> results =
        [
            .. vectorIndex.Chunks
                .Take(topK)
                .Select((entry, rank) => new ChunkSearchResult(
                    entry.Chunk.ChunkId,
                    entry.Request.DocumentId,
                    entry.Chunk.ChunkOrder,
                    entry.Chunk.Text,
                    entry.Request.BlobUri,
                    1.0 - (rank * 0.01))),
        ];

        return Task.FromResult(Result.Success(results));
    }
}

/// <summary>
/// An agent that always searches once, through the same ports the real one uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>It substitutes the agent runtime, not the retrieval.</b> Foundry is what
/// cannot be reached from an offline suite; the embedding and search behind the
/// agent's tool are already faked in this file and are shared with ingestion. So
/// this stands in for the model's decision to search — hardcoded to "once, with
/// the question as written" — and lets everything downstream of that decision stay
/// real. An agent fake that returned canned sources would leave the endpoint's
/// tests unable to tell whether the corpus reached it at all.
/// </para>
/// <para>
/// <see cref="SearchCount"/> is settable so a test can reproduce the case the
/// response contract exists to expose: an agent that answered without looking
/// anything up.
/// </para>
/// </remarks>
internal sealed class FakeAgent(FakeEmbeddings embeddings, FakeSearchIndex searchIndex) : IAgentService
{
    public Error? Error { get; set; }

    /// <summary>How many searches to report, and to actually perform.</summary>
    public int SearchCount { get; set; } = 1;

    public int CallCount { get; private set; }

    public AgentQuestion? LastQuestion { get; private set; }

    public async Task<Result<AgentAnswer>> AskAsync(
        AgentQuestion question,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastQuestion = question;

        if (Error is not null)
        {
            return Result.Failure<AgentAnswer>(Error);
        }

        var sources = new List<ChunkSearchResult>();

        for (int search = 0; search < SearchCount; search++)
        {
            Result<ReadOnlyMemory<float>> vector = await embeddings
                .GenerateEmbeddingAsync(question.Question, cancellationToken);

            if (vector.IsFailure)
            {
                return Result.Failure<AgentAnswer>(vector.Error);
            }

            Result<IReadOnlyList<ChunkSearchResult>> found = await searchIndex
                .SearchChunksAsync(vector.Value, question.MaxSources, cancellationToken);

            if (found.IsFailure)
            {
                return Result.Failure<AgentAnswer>(found.Error);
            }

            // Deduplicated across searches, as the port promises. Repeated searches
            // here return the same passages, which is precisely the case the
            // promise exists for.
            sources.AddRange(found.Value.Where(chunk =>
                !sources.Any(existing => existing.ChunkId == chunk.ChunkId)));
        }

        string answer = string.Create(
            CultureInfo.InvariantCulture,
            $"Agent answer from {sources.Count} sources after {SearchCount} searches [1].");

        return Result.Success(new AgentAnswer(answer, sources, SearchCount, new TokenUsage(900, 60, 960)));
    }
}

/// <summary>A chat model that answers by naming the sources it was given.</summary>
/// <remarks>
/// The canned answer echoes the source count so a test can tell an answer that
/// was grounded in retrieved evidence from one that was not, without asserting
/// on model output nobody controls.
/// </remarks>
internal sealed class FakeChat : IChatService
{
    public Error? Error { get; set; }

    public int CallCount { get; private set; }

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public Task<Result<ChatCompletionResult>> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastMessages = messages;

        if (Error is not null)
        {
            return Task.FromResult(Result.Failure<ChatCompletionResult>(Error));
        }

        string prompt = messages[^1].Content;
        int sourceCount = prompt.Split("[", StringSplitOptions.RemoveEmptyEntries).Length - 1;

        string answer = string.Create(
            CultureInfo.InvariantCulture,
            $"Grounded answer from {sourceCount} sources [1].");

        return Task.FromResult(Result.Success(new ChatCompletionResult(answer, new TokenUsage(120, 30, 150))));
    }
}
