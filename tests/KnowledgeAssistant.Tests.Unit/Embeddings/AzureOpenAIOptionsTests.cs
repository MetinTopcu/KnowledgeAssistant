using System.ComponentModel.DataAnnotations;
using KnowledgeAssistant.Infrastructure.Azure.OpenAI;

namespace KnowledgeAssistant.Tests.Unit.Embeddings;

/// <summary>
/// Which embedding and chat configurations the host may start with.
/// </summary>
/// <remarks>
/// The embedding dimension is both an assertion on every returned vector and
/// the dimension the retrieval index is created with, so a value the model
/// cannot produce must fail at startup — not on the first upload, after a blob
/// has been written and possibly an index created with the wrong shape.
/// </remarks>
public sealed class AzureOpenAIOptionsTests
{
    private static AzureOpenAIOptions Options(
        string modelName = "text-embedding-3-large",
        int dimensions = 3072,
        double? temperature = null) => new()
        {
            Endpoint = "https://fake.services.ai.azure.com/",
            EmbeddingDeploymentName = "embeddings",
            ChatDeploymentName = "chat",
            EmbeddingModelName = modelName,
            EmbeddingDimensions = dimensions,
            ChatTemperature = temperature,
        };

    private static List<ValidationResult> Validate(AzureOpenAIOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_MatchTheVerifiedEmbeddingModel()
    {
        var options = new AzureOpenAIOptions();

        options.EmbeddingModelName.Should().Be("text-embedding-3-large");
        options.EmbeddingDimensions.Should().Be(3072);
    }

    [Fact]
    public void Defaults_SendNoChatTemperature()
    {
        new AzureOpenAIOptions().ChatTemperature.Should().BeNull(
            "reasoning models reject any temperature but their default");
    }

    [Theory]
    [InlineData("text-embedding-3-large", 3072)]
    [InlineData("text-embedding-3-small", 1536)]
    [InlineData("text-embedding-ada-002", 1536)]
    [InlineData("TEXT-EMBEDDING-3-LARGE", 3072)]
    public void AKnownModelWithItsNativeDimension_IsValid(string model, int dimensions)
    {
        Validate(Options(model, dimensions)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("text-embedding-3-large", 1536, 3072)]
    [InlineData("text-embedding-3-small", 3072, 1536)]
    [InlineData("text-embedding-ada-002", 3072, 1536)]
    public void AKnownModelWithAnyOtherDimension_FailsNamingTheRightValue(string model, int dimensions, int expected)
    {
        List<ValidationResult> results = Validate(Options(model, dimensions));

        results.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain($"produces {expected}-dimensional vectors");
    }

    [Fact]
    public void AnUnknownModel_IsAcceptedWithTheConfiguredDimension()
    {
        Validate(Options("my-custom-embedder", 768)).Should().BeEmpty();
    }

    [Fact]
    public void AZeroDimension_IsRejected()
    {
        // The retrieval index is created from this value; zero cannot describe one.
        Validate(Options("my-custom-embedder", 0)).Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("EmbeddingDimensions must be between 1 and 16384");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void AnExplicitTemperatureInRange_IsValid(double temperature)
    {
        Validate(Options(temperature: temperature)).Should().BeEmpty();
    }

    [Fact]
    public void ATemperatureOutOfRange_IsRejected()
    {
        Validate(Options(temperature: 2.5)).Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("ChatTemperature");
    }
}
