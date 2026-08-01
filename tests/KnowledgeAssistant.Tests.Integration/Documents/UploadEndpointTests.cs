using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeAssistant.Application.Commands.Documents.Upload;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Integration.Harness;

namespace KnowledgeAssistant.Tests.Integration.Documents;

/// <summary>
/// <c>POST /api/documents</c> driven over HTTP.
/// </summary>
/// <remarks>
/// The happy path here is a genuine end-to-end ingestion: a real PDF crosses a
/// real multipart boundary, is bound by the real model binder, validated by the
/// real validator, and chunked by the real PdfPig extractor. Only the four Azure
/// writes are substituted. Nothing about that sequence is asserted by any unit
/// test, because every part of it lives between the socket and the ports.
/// </remarks>
public sealed class UploadEndpointTests
{
    [Fact]
    public async Task Upload_WithAValidPdf_IngestsItAndReportsWhatWasStored()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();
        byte[] pdf = TestContent.Pdf();

        using MultipartFormDataContent form = TestContent.Upload(pdf, "Quarterly Report.pdf");
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        UploadDocumentResponse? body = await response.Content.ReadFromJsonAsync<UploadDocumentResponse>();

        body.Should().NotBeNull();
        body!.FileName.Should().Be("Quarterly Report.pdf");
        body.ContentType.Should().Be("application/pdf");
        body.SizeInBytes.Should().Be(pdf.LongLength, "the size reported is the one storage measured");
        body.ChunkCount.Should().BeGreaterThan(1, "a three-page document exceeds one 800-character chunk");
        body.DocumentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Upload_WritesTheBlobTheChunksAndTheDocumentIndexEntry()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf(), "Handbook.pdf");
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        UploadDocumentResponse body = (await response.Content.ReadFromJsonAsync<UploadDocumentResponse>())!;

        factory.Azure.Blob.Count.Should().Be(1);
        factory.Azure.VectorIndex.Chunks.Should().HaveCount(body.ChunkCount);
        factory.Azure.SearchIndex.Documents.Should().ContainSingle()
            .Which.OriginalFileName.Should().Be("Handbook.pdf");
    }

    [Fact]
    public async Task Upload_ExtractsTextFromEveryPageOfTheDocument()
    {
        // The real extractor is running, so this asserts something no fake could:
        // that the bytes which crossed the wire were parsed as a PDF.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf(pages: 3));
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        IEnumerable<string> text = factory.Azure.VectorIndex.Chunks.Select(entry => entry.Chunk.Text);

        text.Should().Contain(chunk => chunk.Contains("Page1", StringComparison.Ordinal));
        text.Should().Contain(chunk => chunk.Contains("Page2", StringComparison.Ordinal));
        text.Should().Contain(chunk => chunk.Contains("Page3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_SanitisesAClientSuppliedPathInTheFileName()
    {
        // IFormFile.FileName is attacker-controlled. The controller strips the
        // directory component, so a traversal sequence cannot reach storage or be
        // echoed back to the caller.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form =
            TestContent.Upload(TestContent.Pdf(pages: 1, linesPerPage: 5), @"..\..\windows\system32\evil.pdf");
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        UploadDocumentResponse body = (await response.Content.ReadFromJsonAsync<UploadDocumentResponse>())!;

        body.FileName.Should().Be("evil.pdf");
        body.FileName.Should().NotContain("..");
    }

    [Fact]
    public async Task Upload_WithNoFile_ReturnsAValidationProblem()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using var empty = new MultipartFormDataContent();
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        factory.Azure.Blob.Count.Should().Be(0, "nothing may be stored for a request that failed validation");
    }

    [Fact]
    public async Task Upload_WithANonPdfExtension_ReturnsAValidationProblemAndStoresNothing()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form =
            TestContent.Upload("plain text"u8.ToArray(), "notes.txt", "text/plain");
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Should().NotBeEmpty("the validator's error codes are the client's contract");

        factory.Azure.Blob.Count.Should().Be(0);
    }

    [Fact]
    public async Task Upload_WhenAPdfContainsNoText_ReturnsAValidationProblem()
    {
        // A caller error, not a server fault: the file is a valid PDF that this
        // system cannot use. It must come back as a 400 the user can act on.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.TextFreePdf(), "blank.pdf");
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Upload_WhenTheBytesAreNotAPdf_ReturnsAValidationProblem()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form =
            TestContent.Upload("this is definitely not a pdf"u8.ToArray(), "lies.pdf");
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a file whose extension lies about its content is the caller's mistake");
    }

    [Fact]
    public async Task Upload_WhenStorageFails_ReturnsAServerProblemNamingTheError()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        factory.Azure.Blob.UploadError = Error.Failure("Storage.UploadFailed", "The storage account is unreachable.");
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf(pages: 1, linesPerPage: 5));
        using HttpResponseMessage response = await client.PostAsync(new Uri("/api/documents", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        problem.RootElement.GetProperty("title").GetString().Should().Be("Storage.UploadFailed",
            "the error code survives the trip to the client, so support can act on it");
    }
}
