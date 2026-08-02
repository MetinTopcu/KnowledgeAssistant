using KnowledgeAssistant.Application.Commands.Documents.Upload;
using KnowledgeAssistant.Application.Queries.Documents.Ask;
using KnowledgeAssistant.Application.Queries.Documents.AskAgent;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAssistant.Tests.Unit.Fakes;

/// <summary>
/// Assembles a handler over faked ports that all record to one
/// <see cref="StageTrace"/>.
/// </summary>
/// <remarks>
/// <para>
/// The scratch probes each carried their own <c>BuildRig</c>, and the two copies
/// had already diverged: the ingestion one named the embedding stage
/// <c>"Embed"</c>, the question one used the same name for a different overload
/// of the same port. A shared rig makes that impossible — one trace vocabulary,
/// one construction site — which is the point of consolidating them.
/// </para>
/// <para>
/// Both pipelines are built from the <i>same</i> fake instances rather than one
/// set each. They genuinely share two ports (embedding and search), and a rig
/// that handed each pipeline a private copy would quietly permit a test to
/// assert against a fake the handler never called.
/// </para>
/// <para>
/// The handlers are <c>internal</c>, and reached here by ordinary construction
/// rather than reflection. That is what <c>InternalsVisibleTo</c> buys: a
/// constructor parameter added to a handler breaks this file in the build,
/// instead of at run time inside an <c>Activator.CreateInstance</c> call.
/// </para>
/// </remarks>
internal sealed class PipelineRig
{
    public PipelineRig()
    {
        Blob = new FakeBlobStorageService(Trace);
        Chunking = new FakeDocumentChunkingService(Trace);
        Embedding = new FakeEmbeddingService(Trace);
        VectorIndex = new FakeVectorIndexService(Trace);
        Search = new FakeAzureSearchService(Trace);
        Chat = new FakeChatService(Trace);
        Agent = new FakeAgentService(Trace);
    }

    public StageTrace Trace { get; } = new();

    public FakeBlobStorageService Blob { get; }

    public FakeDocumentChunkingService Chunking { get; }

    public FakeEmbeddingService Embedding { get; }

    public FakeVectorIndexService VectorIndex { get; }

    public FakeAzureSearchService Search { get; }

    public FakeChatService Chat { get; }

    public FakeAgentService Agent { get; }

    /// <summary>
    /// Builds the ingestion handler with the real validator.
    /// </summary>
    /// <remarks>
    /// The real validator, not a stub. "Validation runs before any side effect"
    /// is one of the properties under test, and a permissive stub would assert
    /// nothing while appearing to.
    /// </remarks>
    public UploadDocumentCommandHandler CreateUploadHandler() =>
        new(
            new UploadDocumentCommandValidator(),
            Blob,
            Chunking,
            Embedding,
            VectorIndex,
            Search,
            TimeProvider.System,
            NullLogger<UploadDocumentCommandHandler>.Instance);

    /// <summary>Builds the question handler with the real validator.</summary>
    public AskQuestionQueryHandler CreateAskHandler() =>
        new(
            new AskQuestionQueryValidator(),
            Embedding,
            Search,
            Chat,
            TimeProvider.System,
            NullLogger<AskQuestionQueryHandler>.Instance);

    /// <summary>Builds the agent handler with the real validator.</summary>
    /// <remarks>
    /// Note which fakes this one does <i>not</i> receive. The agent handler holds
    /// no retrieval: embedding and search reach it only through the agent port, and
    /// wiring them in here would let a test assert against a fake the handler
    /// cannot reach — which is precisely the mistake a shared rig exists to make
    /// impossible.
    /// </remarks>
    public AskAgentQueryHandler CreateAgentHandler() =>
        new(
            new AskAgentQueryValidator(),
            Agent,
            TimeProvider.System,
            NullLogger<AskAgentQueryHandler>.Instance);
}
