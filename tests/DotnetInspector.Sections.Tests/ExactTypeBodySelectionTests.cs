using System.ComponentModel;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class ExactTypeBodySelectionTests
{
    [Theory]
    [InlineData("Visible", true)]
    [InlineData("Hidden", false)]
    [InlineData("Deprecated", false)]
    [InlineData("Internal", false)]
    [InlineData("Protected", false)]
    [InlineData("InternalOuter+PublicNested", false)]
    public async Task CatalogInventoryAndIssuedBodyAgree(string leaf, bool admittedPublic)
    {
        string path = typeof(BodySelectionCases).Assembly.Location;
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await using var library = await LibraryInspectionTestLibrary.CreateAsync(
            bytes, LibraryInspectionTestLibrary.Identity(bytes));
        using AssemblyInspectionSession session = AssemblyInspectionSession.Open(path);
        foreach (bool includeAll in new[] { false, true })
        {
            ApiSurfaceScope scope = includeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.Public;
            string query = typeof(BodySelectionCases).FullName + "+" + leaf;
            var inventory = ExactTypeInspectionOperation.SelectDeclarations(session.TypeDeclarations(), query, scope).Content;
            var catalog = ExactTypeInspectionOperation.SelectCatalog(session.ApiSurface(includeAll), query).Content;
            var compact = ExactTypeInspectionOperation.ExecuteWithLibraryTarget(
                library.Reference, library.IssueOperation(), query, scope,
                new ApiSurfaceExtractionBounds(20_000, 0, 100, 20_000, 2_000_000, 8_000_000),
                (selected, _) => selected.Content,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(includeAll || admittedPublic ? ExactTypeInspectionOutcome.Available : ExactTypeInspectionOutcome.NotFound,
                inventory.Outcome);
            Assert.Equal(catalog.Outcome, inventory.Outcome);
            Assert.Equal(compact.Outcome, inventory.Outcome);
            Assert.Equal(catalog.Type?.DefinitionName, inventory.Definition);
            Assert.Equal(compact.Definition, inventory.Definition);
            if (inventory.DefinitionToken is not { } token)
                continue;
            Assert.Equal(catalog.Type!.MetadataToken, token.Value);
            var body = session.MethodBodies.ResolveApiMethodOverload(token.Value, "M", 0, includeAll);
            Assert.NotNull(body);
            Assert.Equal(Assert.Single(catalog.Type!.Members, item => item.Name == "M").MetadataToken,
                body.MetadataToken);
            var issued = ExactTypeInspectionOperation.SelectDeclarations(session.TypeDeclarations(),
                inventory.Definition!.ToEscapedFullName(), scope, ExactTypeSelectionKind.DefinitionIdentity).Content;
            Assert.Equal(inventory.Definition, issued.Definition);
            Assert.Equal(inventory.DefinitionToken, issued.DefinitionToken);
        }
    }

    [Fact]
    public void IncompleteInventoryDoesNotLendABodyCoordinate()
    {
        using var session = AssemblyInspectionSession.Open(typeof(BodySelectionCases).Assembly.Location);
        var selection = ExactTypeInspectionOperation.SelectDeclarations(
            session.TypeDeclarations(maximumRetainedDeclarations: 0),
            "Visible", ApiSurfaceScope.IncludeAll).Content;
        Assert.Equal(ExactTypeInspectionOutcome.Unavailable, selection.Outcome);
        Assert.Null(selection.DefinitionToken);
    }

    public class BodySelectionCases
    {
        public class Visible { public void M() { } }
        [EditorBrowsable(EditorBrowsableState.Never)]
        public class Hidden { public void M() { } }
        [Obsolete("A declaration fact, filtered only by the transitional admission.")]
        public class Deprecated { public void M() { } }
        internal class Internal { public void M() { } }
        protected class Protected { public void M() { } }
        internal class InternalOuter { public class PublicNested { public void M() { } } }
    }
}
