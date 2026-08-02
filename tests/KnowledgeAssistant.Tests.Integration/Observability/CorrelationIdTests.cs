using System.Net;
using KnowledgeAssistant.Tests.Integration.Harness;

namespace KnowledgeAssistant.Tests.Integration.Observability;

/// <summary>
/// Correlation identifiers on the request and the response.
/// </summary>
/// <remarks>
/// Only observable over HTTP: the middleware reads a request header, attaches to
/// the ambient activity, and writes response headers, none of which exists
/// outside a real pipeline.
/// </remarks>
public sealed class CorrelationIdTests
{
    private const string CorrelationHeader = "X-Correlation-Id";
    private const string TraceHeader = "X-Trace-Id";

    private static readonly Uri Live = new("/health/live", UriKind.Relative);
    private static readonly Uri Questions = new("/api/questions", UriKind.Relative);

    [Fact]
    public async Task EveryResponse_CarriesACorrelationIdAndATraceId()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Live);

        response.Headers.GetValues(CorrelationHeader).Should().ContainSingle()
            .Which.Should().NotBeNullOrWhiteSpace();
        response.Headers.GetValues(TraceHeader).Should().ContainSingle()
            .Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WhenTheCallerSuppliesACorrelationId_ItIsEchoedBack()
    {
        // The case that shows up in support: a client quotes an identifier it
        // chose, and it has to find something.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, Live);
        request.Headers.Add(CorrelationHeader, "order-4815162342");

        using HttpResponseMessage response = await client.SendAsync(request);

        response.Headers.GetValues(CorrelationHeader).Should().Equal("order-4815162342");
    }

    [Fact]
    public async Task WhenNoCorrelationIdIsSupplied_TheTraceIdStandsIn()
    {
        // W3C trace context is the real correlation mechanism; the header is the
        // bridge for callers that do not speak it. With no header, the two agree.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Live);

        string correlationId = response.Headers.GetValues(CorrelationHeader).Single();
        string traceId = response.Headers.GetValues(TraceHeader).Single();

        correlationId.Should().Be(traceId);
    }

    [Theory]
    [InlineData("has spaces and punctuation!")]
    [InlineData("../../etc/passwd")]
    [InlineData("<script>alert(1)</script>")]
    public async Task AnUnacceptableCorrelationId_IsReplacedRatherThanEchoed(string hostile)
    {
        // The value is copied into a response header and into every log line for
        // the request, so it is caller-controlled data on two paths that are
        // sensitive to it. Anything outside a conservative character set is
        // discarded in favour of the trace id.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, Live);
        request.Headers.TryAddWithoutValidation(CorrelationHeader, hostile);

        using HttpResponseMessage response = await client.SendAsync(request);

        string correlationId = response.Headers.GetValues(CorrelationHeader).Single();

        correlationId.Should().NotBe(hostile);
        correlationId.Should().Be(response.Headers.GetValues(TraceHeader).Single());
    }

    [Fact]
    public async Task AnOverlongCorrelationId_IsReplaced()
    {
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, Live);
        request.Headers.Add(CorrelationHeader, new string('a', 129));

        using HttpResponseMessage response = await client.SendAsync(request);

        response.Headers.GetValues(CorrelationHeader).Single()
            .Should().Be(response.Headers.GetValues(TraceHeader).Single());
    }

    [Fact]
    public async Task CorrelationHeaders_ArePresentOnFailureResponsesToo()
    {
        // The responses whose identifier anyone actually needs.
        using var factory = new KnowledgeAssistantApiFactory();
        using HttpClient client = factory.CreateClient();

        using StringContent blank = TestContent.Question(string.Empty);
        using HttpResponseMessage response = await client.PostAsync(Questions, blank);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Contains(CorrelationHeader).Should().BeTrue();
        response.Headers.Contains(TraceHeader).Should().BeTrue();
    }
}
