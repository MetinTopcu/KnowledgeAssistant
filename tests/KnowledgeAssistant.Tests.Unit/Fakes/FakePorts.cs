using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;

namespace KnowledgeAssistant.Tests.Unit.Fakes;

/// <summary>
/// One fake per outbound port, shared by every orchestration test.
/// </summary>
/// <remarks>
/// <para>
/// Consolidated deliberately. The ingestion and question pipelines both need a
/// fake embedding service and a fake search service, and two independent copies
/// drift: one grows a behaviour the other lacks, and a test starts asserting
/// against a stub that no longer resembles the port.
/// </para>
/// <para>
/// Each fake records the stage it was called at rather than throwing on the path
/// it does not expect. The assertion then lives in the test, as an expected stage
/// sequence — which reads better in a failure report than a stack trace, and
/// catches an unexpected call just as surely.
/// </para>
/// </remarks>
internal sealed class FakeBlobStorageService(StageTrace trace) : IBlobStorageService
{
    public Error? UploadError { get; set; }

    public Error? DownloadError { get; set; }

    public Stream? UploadedStream { get; private set; }

    public string? DownloadedBlobName { get; private set; }

    public TrackedStream DownloadStream { get; } = new("%PDF-downloaded-bytes"u8.ToArray());

    public int DownloadCallCount { get; private set; }

    public Task<Result<BlobUploadResult>> UploadAsync(
        Guid documentId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
    {
        trace.Record("Upload", cancellationToken);
        UploadedStream = content;

        // A real upload consumes the stream. Reproducing that is what makes
        // "the handler does not reuse the uploaded stream" a meaningful test.
        content.CopyTo(Stream.Null);

        return Task.FromResult(UploadError is not null
            ? Result.Failure<BlobUploadResult>(UploadError)
            : Result.Success(new BlobUploadResult(
                $"2026/08/01/{documentId}.pdf",
                new Uri($"https://acct.blob.core.windows.net/documents/2026/08/01/{documentId}.pdf"),
                1234)));
    }

    public Task<Result<Stream>> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        trace.Record("Download", cancellationToken);
        DownloadCallCount++;
        DownloadedBlobName = blobName;

        return Task.FromResult(DownloadError is not null
            ? Result.Failure<Stream>(DownloadError)
            : Result.Success<Stream>(DownloadStream));
    }
}

internal sealed class FakeDocumentChunkingService(StageTrace trace) : IDocumentChunkingService
{
    public Error? Error { get; set; }

    public int ChunkCount { get; set; } = 3;

    public Stream? ReceivedStream { get; private set; }

    public Guid ReceivedDocumentId { get; private set; }

    public List<DocumentChunk> Produced { get; } = [];

    public Task<Result<IReadOnlyList<DocumentChunk>>> ChunkAsync(
        Guid documentId,
        Stream content,
        CancellationToken cancellationToken)
    {
        trace.Record("Chunk", cancellationToken);
        ReceivedStream = content;
        ReceivedDocumentId = documentId;

        if (Error is not null)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<DocumentChunk>>(Error));
        }

        Produced.Clear();

        for (int index = 0; index < ChunkCount; index++)
        {
            Produced.Add(new DocumentChunk(Guid.CreateVersion7(), index, $"chunk text {index}"));
        }

        return Task.FromResult(Result.Success<IReadOnlyList<DocumentChunk>>(Produced));
    }
}

internal sealed class FakeEmbeddingService(StageTrace trace) : IEmbeddingService
{
    public Error? ChunkError { get; set; }

    public Error? TextError { get; set; }

    /// <summary>Returns embeddings in reverse order, to prove the join is by id.</summary>
    public bool ShuffleOrder { get; set; }

    /// <summary>Omits the embedding for this chunk index.</summary>
    public int OmitIndex { get; set; } = -1;

    public string? ReceivedText { get; private set; }

    public ReadOnlyMemory<float> QueryVector { get; set; } = new float[] { 0.1f, 0.2f, 0.3f };

    public Task<Result<IReadOnlyList<ChunkEmbedding>>> GenerateEmbeddingsAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        trace.Record("EmbedChunks", cancellationToken);

        if (ChunkError is not null)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<ChunkEmbedding>>(ChunkError));
        }

        var embeddings = new List<ChunkEmbedding>();

        for (int index = 0; index < chunks.Count; index++)
        {
            if (index == OmitIndex)
            {
                continue;
            }

            // vector[0] carries the chunk's order, so a mis-pairing is detectable.
            embeddings.Add(new ChunkEmbedding(
                chunks[index].ChunkId,
                new float[] { chunks[index].ChunkOrder, 0.5f }));
        }

        if (ShuffleOrder)
        {
            embeddings.Reverse();
        }

        return Task.FromResult(Result.Success<IReadOnlyList<ChunkEmbedding>>(embeddings));
    }

    public Task<Result<ReadOnlyMemory<float>>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken)
    {
        trace.Record("EmbedText", cancellationToken);
        ReceivedText = text;

        return Task.FromResult(TextError is not null
            ? Result.Failure<ReadOnlyMemory<float>>(TextError)
            : Result.Success(QueryVector));
    }
}

internal sealed class FakeVectorIndexService(StageTrace trace) : IVectorIndexService
{
    public Error? Error { get; set; }

    public VectorIndexRequest? Received { get; private set; }

    public Task<Result> IndexChunksAsync(VectorIndexRequest request, CancellationToken cancellationToken)
    {
        trace.Record("VectorIndex", cancellationToken);
        Received = request;

        return Task.FromResult(Error is not null ? Result.Failure(Error) : Result.Success());
    }
}

internal sealed class FakeAzureSearchService(StageTrace trace) : IAzureSearchService
{
    public Error? IndexError { get; set; }

    public Error? SearchError { get; set; }

    public DocumentIndexRequest? ReceivedIndexRequest { get; private set; }

    public int ResultCount { get; set; } = 3;

    public int ResultTextLength { get; set; } = 50;

    public ReadOnlyMemory<float> ReceivedVector { get; private set; }

    public int ReceivedTopK { get; private set; } = -1;

    public List<ChunkSearchResult> Produced { get; } = [];

    public Task<Result> IndexDocumentAsync(DocumentIndexRequest request, CancellationToken cancellationToken)
    {
        trace.Record("DocumentIndex", cancellationToken);
        ReceivedIndexRequest = request;

        return Task.FromResult(IndexError is not null ? Result.Failure(IndexError) : Result.Success());
    }

    public Task<Result<IReadOnlyList<ChunkSearchResult>>> SearchChunksAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        trace.Record("Search", cancellationToken);
        ReceivedVector = queryVector;
        ReceivedTopK = topK;

        if (SearchError is not null)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<ChunkSearchResult>>(SearchError));
        }

        Produced.Clear();

        for (int index = 0; index < ResultCount; index++)
        {
            Produced.Add(new ChunkSearchResult(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                index,
                new string((char)('a' + (index % 26)), ResultTextLength),
                new Uri($"https://acct.blob.core.windows.net/documents/doc{index}.pdf"),
                0.9 - (index * 0.1)));
        }

        return Task.FromResult(Result.Success<IReadOnlyList<ChunkSearchResult>>(Produced));
    }
}

/// <summary>
/// The agent port, scripted rather than simulated.
/// </summary>
/// <remarks>
/// Deliberately dumb. What the agent decides to search for, and how often, is the
/// adapter's concern and is tested against the adapter's own collaborators; the
/// handler above it only has to shape whatever comes back. A fake that pretended
/// to reason would test the fake.
/// </remarks>
internal sealed class FakeAgentService(StageTrace trace) : IAgentService
{
    public Error? Error { get; set; }

    public string Answer { get; set; } = "The handbook says 42 [1].";

    public int SearchCount { get; set; } = 1;

    public TokenUsage? Usage { get; set; } = new(900, 60, 960);

    public List<ChunkSearchResult> Sources { get; } =
    [
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, "first passage",
            new Uri("https://acct.blob.core.windows.net/documents/a.pdf"), 0.9),
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "second passage",
            new Uri("https://acct.blob.core.windows.net/documents/b.pdf"), 0.8),
    ];

    public AgentQuestion? ReceivedQuestion { get; private set; }

    public int CallCount { get; private set; }

    public Task<Result<AgentAnswer>> AskAsync(AgentQuestion question, CancellationToken cancellationToken)
    {
        trace.Record("Agent", cancellationToken);
        CallCount++;
        ReceivedQuestion = question;

        return Task.FromResult(Error is not null
            ? Result.Failure<AgentAnswer>(Error)
            : Result.Success(new AgentAnswer(Answer, Sources, SearchCount, Usage)));
    }
}

internal sealed class FakeChatService(StageTrace trace) : IChatService
{
    public Error? Error { get; set; }

    public string Answer { get; set; } = "The answer is 42 [1].";

    public TokenUsage? Usage { get; set; } = new(120, 30, 150);

    public IReadOnlyList<ChatMessage>? ReceivedMessages { get; private set; }

    public int CallCount { get; private set; }

    public Task<Result<ChatCompletionResult>> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        trace.Record("Chat", cancellationToken);
        CallCount++;
        ReceivedMessages = messages;

        return Task.FromResult(Error is not null
            ? Result.Failure<ChatCompletionResult>(Error)
            : Result.Success(new ChatCompletionResult(Answer, Usage)));
    }
}
