using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeAssistant.Application.Queries.Documents.Ask;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Integration.Harness;

namespace KnowledgeAssistant.Tests.Integration.Questions;

/// <summary>
/// <c>POST /api/questions</c> driven over HTTP.
/// </summary>
/// <remarks>
/// The fake search index serves whatever the fake vector index holds, so the
/// round trip below — upload a document, then ask about it — exercises both
/// pipelines against one corpus. That connection is what makes this an
/// integration test rather than two endpoint tests sharing a process.
/// </remarks>
public sealed class AskEndpointTests
{
    private static readonly Uri Questions = new("/api/questions", UriKind.Relative);
    private static readonly Uri Documents = new("/api/documents", UriKind.Relative);

    [Fact]
    public async Task Ask_AfterAnUpload_AnswersFromTheIngestedDocumentWithCitations()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf(), "Handbook.pdf");
        using HttpResponseMessage upload = await client.PostAsync(Documents, form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        using StringContent question = TestContent.Question("What does the handbook say?", topK: 3);
        using HttpResponseMessage response = await client.PostAsync(Questions, question);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        AskQuestionResponse body = (await response.Content.ReadFromJsonAsync<AskQuestionResponse>())!;

        body.RetrievedChunkCount.Should().Be(3);
        body.Citations.Should().HaveCount(3);
        body.Citations.Select(citation => citation.ReferenceNumber).Should().Equal(1, 2, 3);
        body.Citations.Should().OnlyContain(citation => citation.Text.Length > 0);
        body.TokenUsage.Should().NotBeNull();
    }

    [Fact]
    public async Task Ask_SendsTheRetrievedChunksToTheModelAsNumberedSources()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf());
        using HttpResponseMessage upload = await client.PostAsync(Documents, form);
        upload.EnsureSuccessStatusCode();

        using StringContent question = TestContent.Question("What is in the document?", topK: 2);
        using HttpResponseMessage response = await client.PostAsync(Questions, question);

        response.EnsureSuccessStatusCode();

        factory.Azure.Chat.CallCount.Should().Be(1);

        IReadOnlyList<KnowledgeAssistant.Application.Interfaces.ChatMessage> messages =
            factory.Azure.Chat.LastMessages!;

        messages.Should().HaveCount(2);
        messages[1].Content.Should().Contain("[1]").And.Contain("[2]");
        messages[1].Content.Should().Contain("What is in the document?");
    }

    [Fact]
    public async Task Ask_UsesTheDefaultRetrievalDepthWhenTopKIsOmitted()
    {
        // The default lives in the controller, so it is only observable here.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf());
        using HttpResponseMessage upload = await client.PostAsync(Documents, form);
        upload.EnsureSuccessStatusCode();

        using StringContent question = TestContent.Question("Anything?");
        using HttpResponseMessage response = await client.PostAsync(Questions, question);

        AskQuestionResponse body = (await response.Content.ReadFromJsonAsync<AskQuestionResponse>())!;

        body.RetrievedChunkCount.Should().Be(5);
    }

    [Fact]
    public async Task Ask_WithAnEmptyCorpus_SucceedsWithoutCallingTheModel()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent question = TestContent.Question("Anything about unicorns?");
        using HttpResponseMessage response = await client.PostAsync(Questions, question);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an uncovered question is a valid answer, not an error");

        AskQuestionResponse body = (await response.Content.ReadFromJsonAsync<AskQuestionResponse>())!;

        body.Citations.Should().BeEmpty();
        body.RetrievedChunkCount.Should().Be(0);
        body.TokenUsage.Should().BeNull();
        factory.Azure.Chat.CallCount.Should().Be(0, "there is no point paying for an ungrounded guess");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ask_WithABlankQuestion_ReturnsAValidationProblem(string question)
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent body = TestContent.Question(question);
        using HttpResponseMessage response = await client.PostAsync(Questions, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        factory.Azure.Embeddings.EmbedTextCallCount.Should().Be(0, "an invalid question must not be embedded");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Ask_WithATopKOutsideTheAllowedRange_ReturnsAValidationProblem(int topK)
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent body = TestContent.Question("A fine question?", topK);
        using HttpResponseMessage response = await client.PostAsync(Questions, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ask_ReportsValidationFailuresKeyedByErrorCodeRatherThanPropertyName()
    {
        // The error code is the stable contract. Keying by property name would
        // churn every client's error handling the day a command field is renamed.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent body = TestContent.Question(string.Empty);
        using HttpResponseMessage response = await client.PostAsync(Questions, body);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        IEnumerable<string> keys = problem.RootElement.GetProperty("errors")
            .EnumerateObject()
            .Select(property => property.Name);

        keys.Should().OnlyContain(key => key.Contains('.', StringComparison.Ordinal),
            "error codes are dotted, like Question.Missing; property names are not");
    }

    [Fact]
    public async Task Ask_WhenTheModelFails_ReturnsAServerProblemNamingTheError()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        factory.Azure.Chat.Error = Error.Failure("Chat.RateLimited", "The deployment is throttling requests.");
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf());
        using HttpResponseMessage upload = await client.PostAsync(Documents, form);
        upload.EnsureSuccessStatusCode();

        using StringContent question = TestContent.Question("Anything?");
        using HttpResponseMessage response = await client.PostAsync(Questions, question);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        problem.RootElement.GetProperty("title").GetString().Should().Be("Chat.RateLimited");
    }

    [Fact]
    public async Task Ask_WhenRetrievalFails_ReturnsAServerProblem()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        factory.Azure.SearchIndex.SearchError =
            Error.Failure("Search.ChunkIndexUnavailable", "The chunk index does not exist.");
        using HttpClient client = factory.CreateClient();

        using StringContent question = TestContent.Question("Anything?");
        using HttpResponseMessage response = await client.PostAsync(Questions, question);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        factory.Azure.Chat.CallCount.Should().Be(0, "the pipeline must stop where it failed");
    }
}
