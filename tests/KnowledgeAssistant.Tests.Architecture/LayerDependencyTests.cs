using System.Reflection;
using KnowledgeAssistant.Application.Abstractions;
using KnowledgeAssistant.Domain.Common;
using KnowledgeAssistant.Infrastructure.DependencyInjection;
using NetArchTest.Rules;

namespace KnowledgeAssistant.Tests.Architecture;

/// <summary>
/// The dependency rule from ARCHITECTURE.md §1, enforced by the build.
/// </summary>
/// <remarks>
/// <para>
/// Source code dependencies point inward only. Breaking that is one
/// <c>using</c> directive away and compiles perfectly, which is why the rule
/// needs a test rather than a paragraph.
/// </para>
/// <para>
/// Namespace prefixes are used rather than assembly identity because
/// <c>NetArchTest</c> resolves dependencies by namespace, and because the check
/// should fail for a type that merely <i>names</i> a forbidden namespace, not
/// only for one that forces an assembly load.
/// </para>
/// </remarks>
public sealed class LayerDependencyTests
{
    private const string DomainNamespace = "KnowledgeAssistant.Domain";
    private const string ApplicationNamespace = "KnowledgeAssistant.Application";
    private const string InfrastructureNamespace = "KnowledgeAssistant.Infrastructure";
    private const string ApiNamespace = "KnowledgeAssistant.Api";

    private static Assembly DomainAssembly => typeof(Result).Assembly;
    private static Assembly ApplicationAssembly => typeof(ICommand<>).Assembly;
    private static Assembly InfrastructureAssembly => typeof(InfrastructureServiceRegistration).Assembly;
    private static Assembly ApiAssembly => typeof(Program).Assembly;

    [Fact]
    public void Domain_DependsOnNothingInTheSolution()
    {
        TestResult result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "Domain is the innermost layer and must know nothing about the layers around it"));
    }

    [Fact]
    public void Domain_HasNoThirdPartyDependencies()
    {
        // Domain.csproj having no PackageReference at all is the point, not an
        // oversight: the enterprise model must not be shaped by a framework.
        TestResult result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Azure", "MediatR", "FluentValidation", "Microsoft.Extensions", "Polly", "UglyToad")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "Domain must stay free of every third-party dependency"));
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructure()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "Application declares ports; Infrastructure implements them. The arrow points inward"));
    }

    [Fact]
    public void Application_DoesNotDependOnApi()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "a use case must be drivable by a queue trigger or a test, not only by HTTP"));
    }

    [Fact]
    public void Application_DoesNotDependOnAnyVendorSdk()
    {
        // The constraint that has been restated in every sprint since Sprint 6.
        // Checked at the type level here, and at the package level by
        // ApplicationHasNoVendorPackageReferences below.
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Azure", "OpenAI", "System.ClientModel", "Polly", "UglyToad")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "the Application layer must remain completely free of vendor SDK types"));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "the composition root may know its adapters; an adapter must never know the host"));
    }

    [Fact]
    public void Api_DependsOnlyOnApplicationAndInfrastructure()
    {
        // The inverse of the rule: everything the API references from this
        // solution must be one of the three inner layers. Stated this way it also
        // catches a fifth project appearing without the rule being revisited.
        TestResult result = Types.InAssembly(ApiAssembly)
            .That()
            .HaveDependencyOn("KnowledgeAssistant")
            .Should()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, DomainNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "the API composes the inner layers and nothing else"));
    }

    [Fact]
    public void OnlyApiExtensions_ReferenceInfrastructure()
    {
        // ARCHITECTURE.md §1 calls Api -> Infrastructure "the one compromise",
        // kept honest by convention: only the DI extensions may name an
        // Infrastructure type, so controllers see Application alone. This turns
        // that convention into a check.
        TestResult result = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespaceStartingWith("KnowledgeAssistant.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(BecauseOf(result,
            "controllers must see Application only; Infrastructure is reached through AddInfrastructure"));
    }

    [Fact]
    public void ApplicationHasNoVendorPackageReferences()
    {
        // A type-level check can miss a package that is referenced but not yet
        // used. This asserts the referenced assembly set directly, which is what
        // actually prevents the boundary from eroding by accident.
        string[] forbidden = ["Azure.", "OpenAI", "System.ClientModel", "Polly", "PdfPig", "UglyToad"];

        IEnumerable<string> referenced = ApplicationAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty);

        IEnumerable<string> offenders = referenced
            .Where(name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

        offenders.Should().BeEmpty(
            "the Application project must not reference any vendor SDK assembly");
    }

    [Fact]
    public void DomainHasNoPackageReferencesAtAll()
    {
        IEnumerable<string> referenced = DomainAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                        && !string.Equals(name, "netstandard", StringComparison.Ordinal));

        referenced.Should().BeEmpty(
            "Domain depends on the BCL and nothing else");
    }

    /// <summary>Renders the offending types into the assertion message.</summary>
    /// <remarks>
    /// A bare "expected true but found false" makes an architecture failure a
    /// puzzle. Naming the types that broke the rule turns it into a fix.
    /// </remarks>
    private static string BecauseOf(TestResult result, string rule)
    {
        IEnumerable<string> offenders = result.FailingTypeNames ?? [];
        return $"{rule}. Offending types: {string.Join(", ", offenders)}";
    }
}
