using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public class AssemblyQueryTests
{
    [Fact]
    public async Task AssemblyQueries_RunAgainstPathlessBorrowedContent()
    {
        var image = await File.ReadAllBytesAsync(
            typeof(AssemblyQueryTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        var reference = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                "query-fixture",
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("query-test"));
        QueryResultSet<AssemblyQueryContext> results;

        using (var metadata = PdbContext.Open(reference))
        {
            Assert.Null(metadata.AssemblyPathOrNull);
            var plan = AssemblyQueryCatalog.Default.Plan(
                AssemblyInfoQuery.Definition,
                AssemblyPresenceQuery.Definition,
                AssemblyReferencesQuery.Definition);

            results = await plan.ExecuteAsync(
                new AssemblyQueryContext(
                    metadata,
                    new FindingSubject("memory:query-fixture", "query-fixture")),
                QueryExecutionPolicy.NetworkFree,
                TestContext.Current.CancellationToken);

            Assert.NotNull(metadata.ExtractAssemblyInfo().AssemblyName);
        }

        var info = results.RequireValue(AssemblyInfoQuery.Definition);
        var presence = results.RequireValue(AssemblyPresenceQuery.Definition);
        var references = results.RequireValue(AssemblyReferencesQuery.Definition);
        Assert.Equal(
            "DotnetInspector.Queries.Tests",
            info.Assembly.AssemblyName);
        Assert.NotNull(presence.Presence);
        Assert.NotEmpty(references.References);
        Assert.NotNull(references.Inspection);
    }
}
