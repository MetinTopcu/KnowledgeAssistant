using Azure.Search.Documents.Indexes.Models;
using KnowledgeAssistant.Infrastructure.Search;

namespace KnowledgeAssistant.Tests.Unit.Search;

/// <summary>
/// The document index definition, derived from <c>SearchDocument</c>.
/// </summary>
/// <remarks>
/// <para>
/// This schema is built by <c>FieldBuilder</c> from the model's attributes rather
/// than written out by hand, so there is little room for the field list to be
/// wrong. What that arrangement does not guarantee is the key: <c>FieldBuilder</c>
/// is perfectly willing to emit a schema with no key field at all, or with one of
/// a type Azure AI Search will not accept, and either produces a failure at index
/// creation time rather than at build time.
/// </para>
/// <para>
/// So the assertions here are deliberately narrow. Restating every attribute
/// would test <c>FieldBuilder</c>, not this code.
/// </para>
/// </remarks>
public sealed class DocumentIndexSchemaTests
{
    [Fact]
    public void Build_ProducesExactlyOneStringKeyField()
    {
        SearchIndex index = DocumentIndexSchema.Build("knowledge-index");

        SearchField key = index.Fields.Should().ContainSingle(field => field.IsKey == true).Subject;

        key.Name.Should().Be(nameof(SearchDocument.DocumentId));
        key.Type.Should().Be(SearchFieldDataType.String, "Azure AI Search keys must be Edm.String");
    }

    [Fact]
    public void Build_NamesTheIndexAsRequested()
    {
        DocumentIndexSchema.Build("knowledge-index").Name.Should().Be("knowledge-index");
    }

    [Fact]
    public void Build_DerivesAFieldForEveryModelProperty()
    {
        // The claim the class documents: adding a property to the model is the
        // only way to add a field, and it always does.
        IEnumerable<string> expected = typeof(SearchDocument)
            .GetProperties()
            .Select(property => property.Name);

        DocumentIndexSchema.Build("knowledge-index").Fields
            .Select(field => field.Name)
            .Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Build_MakesUploadedAtFilterableAndSortable()
    {
        // The corpus is browsed and pruned by ingestion date; both capabilities
        // are needed for that and neither is on by default.
        SearchField uploadedAt = DocumentIndexSchema.Build("knowledge-index").Fields
            .Single(field => field.Name == nameof(SearchDocument.UploadedAt));

        uploadedAt.Type.Should().Be(SearchFieldDataType.DateTimeOffset);
        uploadedAt.IsFilterable.Should().BeTrue();
        uploadedAt.IsSortable.Should().BeTrue();
    }
}
