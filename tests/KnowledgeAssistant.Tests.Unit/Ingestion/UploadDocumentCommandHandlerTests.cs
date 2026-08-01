using KnowledgeAssistant.Application.Commands.Documents.Upload;
using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Unit.Fakes;

namespace KnowledgeAssistant.Tests.Unit.Ingestion;

/// <summary>
/// The ingestion pipeline: ordering, the chunk/vector join, and failure containment.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is about orchestration, so every collaborator is faked.
/// What the adapters do with a real Azure response is tested in their own files;
/// what this handler must get right is the sequence, the joining, and where it
/// stops when a stage fails.
/// </para>
/// <para>
/// The stage trace does most of the work. "The document index is written last"
/// and "nothing runs after a validation failure" are both statements about
/// sequence, and asserting a recorded sequence reports a break far more legibly
/// than a set of independent call-count checks.
/// </para>
/// </remarks>
public sealed class UploadDocumentCommandHandlerTests
{
    private static readonly string[] FullPipeline =
        ["Upload", "Download", "Chunk", "EmbedChunks", "VectorIndex", "DocumentIndex"];

    private static UploadDocumentCommand Command(
        Stream content,
        string fileName = "report.pdf",
        long sizeInBytes = 4096) =>
        new(fileName, "application/pdf", sizeInBytes, content);

    private static TrackedStream Pdf() => new("%PDF-original-upload"u8.ToArray());

    [Fact]
    public async Task Handle_RunsEveryStageInPipelineOrder()
    {
        var rig = new PipelineRig();

        Result<UploadDocumentResponse> result =
            await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.Trace.Stages.Should().Equal(FullPipeline, "the pipeline sequence is the contract");
    }

    [Fact]
    public async Task Handle_WritesTheDocumentIndexLast()
    {
        // The document index entry is the ingestion-complete marker. Written any
        // earlier, a document that failed to index its chunks would still appear
        // in the corpus — searchable, and unable to answer anything.
        var rig = new PipelineRig();

        await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        rig.Trace.Stages[^1].Should().Be("DocumentIndex");
    }

    [Fact]
    public async Task Handle_ReportsStorageMeasuredSizeRatherThanTheClientsClaim()
    {
        var rig = new PipelineRig();

        Result<UploadDocumentResponse> result = await rig.CreateUploadHandler()
            .Handle(Command(Pdf(), sizeInBytes: 4096), CancellationToken.None);

        result.Value.SizeInBytes.Should().Be(1234, "the byte count comes back from storage, not from the caller");
        result.Value.ContentType.Should().Be("application/pdf");
        result.Value.ChunkCount.Should().Be(3);
        result.Value.BlobName.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task Handle_ChunksTheDownloadedBlobRatherThanTheUploadedStream()
    {
        // The uploaded stream has already been consumed by the upload. Reusing it
        // would hand chunking a stream positioned at its end, which extracts no
        // text from a file that is perfectly valid.
        var rig = new PipelineRig();
        TrackedStream uploaded = Pdf();

        Result<UploadDocumentResponse> result =
            await rig.CreateUploadHandler().Handle(Command(uploaded), CancellationToken.None);

        rig.Blob.DownloadCallCount.Should().Be(1);
        rig.Blob.DownloadedBlobName.Should().Be(result.Value.BlobName);
        rig.Chunking.ReceivedStream.Should().BeSameAs(rig.Blob.DownloadStream);
        rig.Chunking.ReceivedStream.Should().NotBeSameAs(uploaded);
    }

    [Fact]
    public async Task Handle_DisposesTheDownloadedStream()
    {
        var rig = new PipelineRig();

        await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        rig.Blob.DownloadStream.Disposed.Should().BeTrue("the handler owns the stream it downloaded");
    }

    [Fact]
    public async Task Handle_DisposesTheDownloadedStreamOnTheFailurePath()
    {
        var rig = new PipelineRig();
        rig.Chunking.Error = Error.Validation("Chunking.NoTextExtracted", "no text");

        await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        rig.Blob.DownloadStream.Disposed.Should().BeTrue("a leak on the failure path is still a leak");
    }

    [Fact]
    public async Task Handle_PairsEachChunkWithItsOwnVectorWhenEmbeddingsComeBackReordered()
    {
        // The embedding port promises a vector per chunk id, not a vector per
        // position. A handler that zipped the two lists by index would pass every
        // other test in this file and silently mis-attribute every vector.
        var rig = new PipelineRig();
        rig.Chunking.ChunkCount = 5;
        rig.Embedding.ShuffleOrder = true;

        Result<UploadDocumentResponse> result =
            await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        IReadOnlyList<VectorIndexChunk> indexed = rig.VectorIndex.Received!.Chunks;

        indexed.Select(chunk => chunk.ChunkOrder).Should().Equal(0, 1, 2, 3, 4);

        // The fake encodes each chunk's order into vector[0], so a vector paired
        // with the wrong chunk shows up here as a mismatched pair of numbers.
        indexed.Select(chunk => chunk.Vector.Span[0]).Should().Equal(0f, 1f, 2f, 3f, 4f);
        indexed.Select(chunk => chunk.ChunkId).Should().Equal(rig.Chunking.Produced.Select(chunk => chunk.ChunkId));
        indexed.Select(chunk => chunk.Text).Should().Equal(rig.Chunking.Produced.Select(chunk => chunk.Text));
    }

    [Fact]
    public async Task Handle_WhenAChunkHasNoEmbedding_FailsAndIndexesNothing()
    {
        var rig = new PipelineRig();
        rig.Chunking.ChunkCount = 4;
        rig.Embedding.OmitIndex = 2;

        Result<UploadDocumentResponse> result =
            await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Ingestion.EmbeddingMissing");
        rig.VectorIndex.Received.Should().BeNull("a partial document must not reach the index");
    }

    [Fact]
    public async Task Handle_CarriesDocumentMetadataToBothIndexes()
    {
        var rig = new PipelineRig();

        Result<UploadDocumentResponse> result = await rig.CreateUploadHandler()
            .Handle(Command(Pdf(), "Quarterly Report.pdf"), CancellationToken.None);

        VectorIndexRequest vectorRequest = rig.VectorIndex.Received!;
        DocumentIndexRequest documentRequest = rig.Search.ReceivedIndexRequest!;

        vectorRequest.DocumentId.Should().Be(result.Value.DocumentId);
        vectorRequest.BlobUri.ToString().Should().StartWith("https://acct.blob");
        documentRequest.OriginalFileName.Should().Be("Quarterly Report.pdf");
        rig.Chunking.ReceivedDocumentId.Should().Be(result.Value.DocumentId);
    }

    [Fact]
    public async Task Handle_StampsOneTimestampAcrossBothIndexesAndTheResponse()
    {
        // Two clock reads would let the same document carry two "uploaded at"
        // values, which makes a corpus impossible to reconcile by time.
        var rig = new PipelineRig();

        Result<UploadDocumentResponse> result =
            await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        rig.VectorIndex.Received!.UploadedAt.Should().Be(rig.Search.ReceivedIndexRequest!.UploadedAt);
        rig.Search.ReceivedIndexRequest!.UploadedAt.Should().Be(result.Value.ReceivedAtUtc);
    }

    [Theory]
    [InlineData("Upload", "Storage.UploadFailed")]
    [InlineData("Download", "Storage.DownloadFailed")]
    [InlineData("Chunk", "Chunking.NoTextExtracted")]
    [InlineData("EmbedChunks", "Embedding.RateLimited")]
    [InlineData("VectorIndex", "VectorIndex.ChunksRejected")]
    [InlineData("DocumentIndex", "Search.IndexingFailed")]
    public async Task Handle_WhenAStageFails_StopsThereAndPassesTheErrorThroughUnwrapped(
        string failingStage,
        string expectedCode)
    {
        // Unwrapped is the load-bearing word. Re-wrapping "the embedding service
        // is rate limited" as a generic ingestion error would cost the caller the
        // one detail that tells them to retry later rather than fix their file.
        var rig = new PipelineRig();
        FailAt(rig, failingStage, expectedCode);

        Result<UploadDocumentResponse> result =
            await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        rig.Trace.Stages.Should().Equal(
            FullPipeline.TakeWhile(stage => stage != failingStage).Append(failingStage),
            $"the pipeline must stop at {failingStage}. Ran: {rig.Trace.Sequence}");
    }

    [Fact]
    public async Task Handle_AfterAPostUploadFailure_LeavesTheBlobStored()
    {
        // Deliberate: the blob is an orphan, not a rollback. A compensating delete
        // would destroy the only copy of a document whose ingestion might succeed
        // on retry, and this system has no transaction spanning storage and search.
        var rig = new PipelineRig();
        rig.Chunking.Error = Error.Validation("Chunking.NoTextExtracted", "no text");

        await rig.CreateUploadHandler().Handle(Command(Pdf()), CancellationToken.None);

        rig.Trace.Stages.Should().Contain("Upload").And.NotContain("DocumentIndex");
    }

    [Fact]
    public async Task Handle_PassesTheCallersTokenToEveryStage()
    {
        var rig = new PipelineRig();
        using var cts = new CancellationTokenSource();

        await rig.CreateUploadHandler().Handle(Command(Pdf()), cts.Token);

        rig.Trace.Tokens.Should().HaveCount(FullPipeline.Length);
        rig.Trace.Tokens.Should().OnlyContain(token => token == cts.Token);
    }

    [Theory]
    [InlineData("notes.txt", 4096, "a non-PDF extension")]
    [InlineData("", 0, "no file at all")]
    public async Task Handle_WhenValidationFails_TouchesNothing(
        string fileName,
        long sizeInBytes,
        string because)
    {
        // The strong form of the assertion. "Returns a failure" would still hold
        // for a handler that uploaded the blob first and validated afterwards.
        var rig = new PipelineRig();

        Result<UploadDocumentResponse> result = await rig.CreateUploadHandler()
            .Handle(Command(Pdf(), fileName, sizeInBytes), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        rig.Trace.Stages.Should().BeEmpty($"{because} must be rejected before any side effect");
    }

    private static void FailAt(PipelineRig rig, string stage, string code)
    {
        Error error = Error.Failure(code, "scripted failure");

        switch (stage)
        {
            case "Upload":
                rig.Blob.UploadError = error;
                break;
            case "Download":
                rig.Blob.DownloadError = error;
                break;
            case "Chunk":
                rig.Chunking.Error = Error.Validation(code, "scripted failure");
                break;
            case "EmbedChunks":
                rig.Embedding.ChunkError = error;
                break;
            case "VectorIndex":
                rig.VectorIndex.Error = error;
                break;
            case "DocumentIndex":
                rig.Search.IndexError = error;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "unknown pipeline stage");
        }
    }
}
