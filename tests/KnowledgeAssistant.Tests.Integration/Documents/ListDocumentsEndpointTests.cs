using System.Net;
using System.Net.Http.Json;
using KnowledgeAssistant.Application.Queries.Documents.List;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Integration.Harness;

namespace KnowledgeAssistant.Tests.Integration.Documents;

/// <summary>
/// <c>GET /api/documents</c> driven over HTTP.
/// </summary>
/// <remarks>
/// The test worth reading is the round trip: upload through the real endpoint,
/// then list through the real endpoint, and check that the second sees what the
/// first wrote. That is the property the screen depends on, and it spans two
/// slices — so no unit test can state it.
/// </remarks>
public sealed class ListDocumentsEndpointTests
{
    private static readonly Uri Documents = new("/api/documents", UriKind.Relative);

    private static Uri WithLimit(int maxResults) =>
        new($"/api/documents?maxResults={maxResults}", UriKind.Relative);

    [Fact]
    public async Task List_OnAnEmptyCorpus_Returns200AndNoDocuments()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Documents);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an empty corpus is an answer, not an error");

        ListDocumentsResponse? body = await response.Content.ReadFromJsonAsync<ListDocumentsResponse>();

        body.Should().NotBeNull();
        body!.Documents.Should().BeEmpty();
        body.Count.Should().Be(0);
        body.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task List_ReturnsTheDocumentsThatWereUploadedThroughTheApi()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent first = TestContent.Upload(TestContent.Pdf(), "Handbook.pdf");
        using HttpResponseMessage upload = await client.PostAsync(Documents, first);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage response = await client.GetAsync(Documents);
        ListDocumentsResponse body = (await response.Content.ReadFromJsonAsync<ListDocumentsResponse>())!;

        DocumentSummary summary = body.Documents.Should().ContainSingle().Subject;
        summary.FileName.Should().Be("Handbook.pdf");
        summary.DocumentId.Should().NotBeEmpty();
        summary.BlobName.Should().NotBeEmpty();
        summary.UploadedAtUtc.Should().NotBe(default);
        body.Count.Should().Be(1);
    }

    [Fact]
    public async Task List_HonoursTheRequestedLimitAndSaysWhenItWasFilled()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        foreach (string name in new[] { "One.pdf", "Two.pdf", "Three.pdf" })
        {
            using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf(), name);
            using HttpResponseMessage upload = await client.PostAsync(Documents, form);
            upload.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using HttpResponseMessage response = await client.GetAsync(WithLimit(2));
        ListDocumentsResponse body = (await response.Content.ReadFromJsonAsync<ListDocumentsResponse>())!;

        body.Documents.Should().HaveCount(2);
        body.Truncated.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ListDocumentsQueryValidator.MaxResultsCeiling + 1)]
    public async Task List_WithALimitOutsideTheAllowedRange_ReturnsAValidationProblem(int maxResults)
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(WithLimit(maxResults));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task List_WhenTheIndexIsUnavailable_Returns500AsAProblemDocument()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        factory.Azure.SearchIndex.ListError =
            Error.Failure("Search.SearchFailed", "The search could not be completed.");

        using HttpResponseMessage response = await client.GetAsync(Documents);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
