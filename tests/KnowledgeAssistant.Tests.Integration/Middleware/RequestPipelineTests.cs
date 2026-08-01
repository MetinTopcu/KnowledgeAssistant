using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using KnowledgeAssistant.Tests.Integration.Harness;

namespace KnowledgeAssistant.Tests.Integration.Middleware;

/// <summary>
/// The filters and conventions every response passes through.
/// </summary>
/// <remarks>
/// <para>
/// The unifying claim under test is that this API speaks <i>one</i> error
/// dialect. A failure raised by a validator, by model binding, and by a resource
/// filter rejecting an oversized body all take different routes through ASP.NET
/// Core, and by default they arrive in different shapes — which forces a client
/// to write two parsers for one endpoint. These tests pin all three to the same
/// contract.
/// </para>
/// <para>
/// None of it is reachable from a unit test: every one of these responses is
/// produced by the framework before or instead of a handler running.
/// </para>
/// </remarks>
public sealed class RequestPipelineTests
{
    private static readonly Uri Documents = new("/api/documents", UriKind.Relative);
    private static readonly Uri Questions = new("/api/questions", UriKind.Relative);

    [Fact]
    public async Task Upload_WhenTheDeclaredLengthExceedsTheLimit_Returns413BeforeReadingTheBody()
    {
        // The reason ContentLengthLimitAttribute exists. Left to [RequestSizeLimit]
        // alone, MVC catches Kestrel's BadHttpRequestException while reading the
        // form and files it as a model error, so the client is told 400 — that its
        // request was malformed, when in fact it was merely too big.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using MultipartFormDataContent form = TestContent.Upload("small"u8.ToArray());
        form.Headers.ContentLength = 22L * 1024 * 1024;

        using HttpResponseMessage response = await client.PostAsync(Documents, form);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        problem.RootElement.GetProperty("type").GetString()
            .Should().EndWith("#section-15.5.14", "413 must cite the RFC section for Content Too Large");
        problem.RootElement.GetProperty("errors").TryGetProperty("Request", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_WithAJsonBody_ReturnsUnsupportedMediaType()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent json = TestContent.Json("""{"file":"nope"}""");
        using HttpResponseMessage response = await client.PostAsync(Documents, json);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType,
            "the endpoint declares [Consumes(\"multipart/form-data\")]");
    }

    [Fact]
    public async Task Ask_WithMalformedJson_ReturnsTheSameProblemShapeAsAValidationFailure()
    {
        // Produced by the InvalidModelStateResponseFactory, not by the Result
        // pipeline. Both must render identically or the endpoint has two dialects.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent malformed = TestContent.Json("""{"question": """);
        using HttpResponseMessage response = await client.PostAsync(Questions, malformed);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        problem.RootElement.TryGetProperty("title", out _).Should().BeTrue();
        problem.RootElement.TryGetProperty("status", out _).Should().BeTrue();
        problem.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
        problem.RootElement.GetProperty("type").GetString().Should().EndWith("#section-15.5.1");
    }

    [Fact]
    public async Task EveryProblemResponse_CarriesATraceIdentifier()
    {
        // The value a support ticket quotes to find the request in the logs. It is
        // stamped by ApiProblemDetails, so it must survive both error routes.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent blank = TestContent.Question(string.Empty);
        using HttpResponseMessage validationFailure = await client.PostAsync(Questions, blank);

        using StringContent malformed = TestContent.Json("""{"question": """);
        using HttpResponseMessage bindingFailure = await client.PostAsync(Questions, malformed);

        foreach (HttpResponseMessage response in new[] { validationFailure, bindingFailure })
        {
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            problem.RootElement.TryGetProperty("traceId", out JsonElement traceId).Should().BeTrue();
            traceId.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ask_WithAGetRequest_ReturnsMethodNotAllowed()
    {
        // The endpoint is POST-only by design: a question is free text that has no
        // business being logged in a URL by every proxy on the path.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Questions);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task SuccessfulResponses_AreJson()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent question = TestContent.Question("Anything?");
        using HttpResponseMessage response = await client.PostAsync(Questions, question);

        // The other half of the problem+json contract: success keeps the ordinary
        // JSON media type, so removing [Produces] to let failures use
        // application/problem+json must not have changed the success shape.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Ask_AcceptsAJsonBodyWithoutACharset()
    {
        // Some clients omit it. The endpoint must not answer 415 for that.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using var body = new StringContent("""{"question":"Anything?"}""");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using HttpResponseMessage response = await client.PostAsync(Questions, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
