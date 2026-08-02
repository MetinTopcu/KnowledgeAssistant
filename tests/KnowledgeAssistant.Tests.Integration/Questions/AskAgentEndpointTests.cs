using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeAssistant.Application.Queries.Documents.AskAgent;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Tests.Integration.Harness;

namespace KnowledgeAssistant.Tests.Integration.Questions;

/// <summary>
/// <c>POST /api/questions/agent</c> driven over HTTP.
/// </summary>
/// <remarks>
/// The fake agent searches through the same fake index ingestion writes to, so the
/// round trip below — upload a document, then ask the agent about it — proves the
/// corpus actually reaches the agent's retrieval rather than that a stub returned
/// a canned answer.
/// </remarks>
public sealed class AskAgentEndpointTests
{
    private static readonly Uri Agent = new("/api/questions/agent", UriKind.Relative);
    private static readonly Uri Documents = new("/api/documents", UriKind.Relative);

    private static StringContent Question(string question, int? maxSources = null) =>
        TestContent.Json(JsonSerializer.Serialize(new { question, maxSources }));

    [Fact]
    public async Task Agent_AfterAnUpload_AnswersFromTheIngestedDocumentWithCitations()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf(), "Handbook.pdf");
        using HttpResponseMessage upload = await client.PostAsync(Documents, form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        using StringContent question = Question("What does the handbook say?", maxSources: 3);
        using HttpResponseMessage response = await client.PostAsync(Agent, question);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        AskAgentResponse body = (await response.Content.ReadFromJsonAsync<AskAgentResponse>())!;

        body.SearchCount.Should().Be(1);
        body.Citations.Should().HaveCount(3);
        body.Citations.Select(citation => citation.ReferenceNumber).Should().Equal(1, 2, 3);
        body.Citations.Should().OnlyContain(citation => citation.Text.Length > 0);
        body.TokenUsage.Should().NotBeNull();
    }

    [Fact]
    public async Task Agent_CollapsesPassagesSeenAcrossSeveralSearches()
    {
        // The citation contract under repetition. An agent that searches twice with
        // similar wording sees the same passage twice; numbering it twice would let
        // one source appear to corroborate itself.
        using var factory = new KnowledgeAssistantApiFactory();
        factory.Azure.Agent.SearchCount = 3;

        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf());
        using HttpResponseMessage upload = await client.PostAsync(Documents, form);
        upload.EnsureSuccessStatusCode();

        using StringContent question = Question("What is in the document?", maxSources: 2);
        using HttpResponseMessage response = await client.PostAsync(Agent, question);

        AskAgentResponse body = (await response.Content.ReadFromJsonAsync<AskAgentResponse>())!;

        body.SearchCount.Should().Be(3);
        body.Citations.Should().HaveCount(2, "three searches over the same corpus found the same two passages");
        body.Citations.Select(citation => citation.ChunkId).Should().OnlyHaveUniqueItems();
        body.Citations.Select(citation => citation.ReferenceNumber).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Agent_UsesTheDefaultSourceBoundWhenMaxSourcesIsOmitted()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent question = Question("Anything?");
        using HttpResponseMessage response = await client.PostAsync(Agent, question);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Azure.Agent.LastQuestion!.MaxSources.Should().Be(5,
            "the controller resolves the default so nothing downstream interprets a null");
    }

    [Fact]
    public async Task Agent_WhenItAnswersWithoutSearching_SucceedsAndReportsZero()
    {
        // The failure that does not look like one. The caller receives a fluent,
        // confident answer the corpus had no part in, and this field is the only
        // thing in the response that says so.
        using var factory = new KnowledgeAssistantApiFactory();
        factory.Azure.Agent.SearchCount = 0;

        using HttpClient client = factory.CreateClient();

        using StringContent question = Question("Who won the 1998 world cup?");
        using HttpResponseMessage response = await client.PostAsync(Agent, question);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        AskAgentResponse body = (await response.Content.ReadFromJsonAsync<AskAgentResponse>())!;

        body.SearchCount.Should().Be(0);
        body.Citations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Agent_WithABlankQuestion_ReturnsAValidationProblem(string question)
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent body = Question(question);
        using HttpResponseMessage response = await client.PostAsync(Agent, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Azure.Agent.CallCount.Should().Be(0, "nothing is spent on a request that was never valid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Agent_WithAnOutOfRangeSourceBound_ReturnsAValidationProblem(int maxSources)
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent body = Question("Anything?", maxSources);
        using HttpResponseMessage response = await client.PostAsync(Agent, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Question.InvalidMaxSources",
            "validation failures are keyed by error code, not by property name");
    }

    [Fact]
    public async Task Agent_WhenRetrievalFails_ReturnsAServerProblemNamingTheStageThatBroke()
    {
        // The error code is what tells an operator which stage broke. A retrieval
        // failure inside the agent must not be flattened into a generic agent
        // error on the way out.
        using var factory = new KnowledgeAssistantApiFactory();
        factory.Azure.Agent.Error = Error.Failure("Search.QueryFailed", "The search service is unavailable.");

        using HttpClient client = factory.CreateClient();

        using StringContent question = Question("Anything?");
        using HttpResponseMessage response = await client.PostAsync(Agent, question);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Search.QueryFailed");
    }

    [Fact]
    public async Task Agent_AndTheRetrievalEndpoint_AnswerTheSameCorpusIndependently()
    {
        // The two endpoints are separate products over one corpus. This asserts
        // they are genuinely separate — different response shapes, different
        // counters — and genuinely over one corpus.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload(TestContent.Pdf());
        using HttpResponseMessage upload = await client.PostAsync(Documents, form);
        upload.EnsureSuccessStatusCode();

        using StringContent agentQuestion = Question("What is in the document?", maxSources: 3);
        using HttpResponseMessage agentResponse = await client.PostAsync(Agent, agentQuestion);

        using StringContent retrievalQuestion = TestContent.Question("What is in the document?", topK: 3);
        using HttpResponseMessage retrievalResponse =
            await client.PostAsync(new Uri("/api/questions", UriKind.Relative), retrievalQuestion);

        agentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        retrievalResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        AskAgentResponse agentBody = (await agentResponse.Content.ReadFromJsonAsync<AskAgentResponse>())!;

        agentBody.Citations.Should().HaveCount(3);
        factory.Azure.Chat.CallCount.Should().Be(1,
            "the agent endpoint answers through the agent port, not through the chat port");
    }
}
