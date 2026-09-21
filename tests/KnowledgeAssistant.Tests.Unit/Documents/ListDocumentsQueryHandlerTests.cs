using KnowledgeAssistant.Application.Interfaces;
using KnowledgeAssistant.Application.Queries.Documents.List;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Unit.Fakes;

namespace KnowledgeAssistant.Tests.Unit.Documents;

/// <summary>
/// The corpus listing: validate, read one port, map.
/// </summary>
/// <remarks>
/// The assertions worth reading twice are the mapping ones. This slice's whole
/// job is to turn what the index holds into what a client is told, and the two
/// ways that can go wrong quietly are dropping a field and inventing one — a
/// blank cell and a fabricated number look equally plausible on a screen.
/// </remarks>
public sealed class ListDocumentsQueryHandlerTests
{
    private static DocumentIndexEntry Document(string fileName, DateTimeOffset uploadedAt) =>
        new(
            DocumentId: Guid.CreateVersion7(),
            OriginalFileName: fileName,
            BlobName: $"2026/09/20/{fileName}",
            BlobUri: new Uri($"https://acct.blob.core.windows.net/documents/{fileName}"),
            UploadedAt: uploadedAt);

    [Fact]
    public async Task Handle_ReturnsEverySummaryTheIndexHolds()
    {
        var rig = new PipelineRig();
        var uploadedAt = new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
        rig.Search.Documents.Add(Document("handbook.pdf", uploadedAt));

        Result<ListDocumentsResponse> result = await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 50), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        DocumentSummary summary = result.Value.Documents.Should().ContainSingle().Subject;
        summary.DocumentId.Should().Be(rig.Search.Documents[0].DocumentId);
        summary.FileName.Should().Be("handbook.pdf");
        summary.BlobName.Should().Be("2026/09/20/handbook.pdf");
        summary.UploadedAtUtc.Should().Be(uploadedAt);
    }

    [Fact]
    public async Task Handle_PreservesThePortsOrdering()
    {
        // The port promises newest first. Re-sorting here would hide a broken
        // OrderBy in the adapter behind a handler that happens to fix it.
        var rig = new PipelineRig();
        rig.Search.Documents.Add(Document("newest.pdf", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero)));
        rig.Search.Documents.Add(Document("older.pdf", new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero)));

        Result<ListDocumentsResponse> result = await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 50), CancellationToken.None);

        result.Value.Documents.Select(document => document.FileName)
            .Should().Equal("newest.pdf", "older.pdf");
    }

    [Fact]
    public async Task Handle_PassesTheRequestedLimitToThePort()
    {
        var rig = new PipelineRig();

        await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 7), CancellationToken.None);

        rig.Search.ReceivedMaxResults.Should().Be(7, "the caller's page size is not the handler's to override");
    }

    [Fact]
    public async Task Handle_ReportsAnEmptyCorpusAsSuccess()
    {
        // An empty corpus is a fact, not a fault: a client rendering a list has
        // an obvious response to it, and an error would force every caller to
        // decode a failure to learn that nothing went wrong.
        var rig = new PipelineRig();

        Result<ListDocumentsResponse> result = await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 50), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Documents.Should().BeEmpty();
        result.Value.Count.Should().Be(0);
        result.Value.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_FlagsTruncationWhenTheLimitIsFilled()
    {
        var rig = new PipelineRig();
        rig.Search.Documents.Add(Document("a.pdf", DateTimeOffset.UnixEpoch));
        rig.Search.Documents.Add(Document("b.pdf", DateTimeOffset.UnixEpoch));
        rig.Search.Documents.Add(Document("c.pdf", DateTimeOffset.UnixEpoch));

        Result<ListDocumentsResponse> result = await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 2), CancellationToken.None);

        result.Value.Documents.Should().HaveCount(2);
        result.Value.Count.Should().Be(2);
        result.Value.Truncated.Should().BeTrue("a full page means there may be more");
    }

    [Fact]
    public async Task Handle_DoesNotFlagTruncationBelowTheLimit()
    {
        var rig = new PipelineRig();
        rig.Search.Documents.Add(Document("only.pdf", DateTimeOffset.UnixEpoch));

        Result<ListDocumentsResponse> result = await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 50), CancellationToken.None);

        result.Value.Truncated.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ListDocumentsQueryValidator.MaxResultsCeiling + 1)]
    public async Task Handle_RejectsALimitOutsideTheAllowedRangeWithoutCallingThePort(int maxResults)
    {
        var rig = new PipelineRig();

        Result<ListDocumentsResponse> result = await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(maxResults), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        rig.Search.ReceivedMaxResults.Should().Be(-1, "validation runs before the port is reached");
    }

    [Fact]
    public async Task Handle_PassesASearchFailureThroughUnaltered()
    {
        var rig = new PipelineRig();
        rig.Search.ListError = Error.Failure("Search.SearchFailed", "The search could not be completed.");

        Result<ListDocumentsResponse> result = await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 50), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Search.SearchFailed", "the code is what tells an operator which dependency broke");
    }

    [Fact]
    public async Task Handle_PropagatesCancellationToThePort()
    {
        var rig = new PipelineRig();
        using var cts = new CancellationTokenSource();

        await rig.CreateListDocumentsHandler()
            .Handle(new ListDocumentsQuery(MaxResults: 50), cts.Token);

        rig.Trace.Stages.Should().Equal(["ListDocuments"]);
        rig.Trace.Tokens.Should().AllSatisfy(token => token.Should().Be(cts.Token));
    }
}
