using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one package assembly-semantic Find request into the shared
/// host-neutral inspection envelope.
/// </summary>
public static class PackageAssemblySemanticFindInspection
{
    public static async ValueTask<
        InspectionEnvelope<PackageAssemblySemanticFindDocument>>
        ExecuteAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackageAssemblySemanticFindExecution execution,
            CancellationToken cancellationToken = default) =>
        await ExecuteAsync(
            request,
            sourceOperation,
            execution,
            nonterminalSink: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<PackageAssemblySemanticFindDocument>>
        ExecuteAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackageAssemblySemanticFindExecution execution,
            IPackageAssemblySemanticFindNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
    {
        PackageAssemblySemanticFindDocument content =
            await PackageAssemblySemanticFindQuery.ExecuteToDocumentAsync(
                request,
                sourceOperation,
                execution,
                nonterminalSink,
                cancellationToken).ConfigureAwait(false);

        return new(
            content,
            new InspectionShare.NonProjectable(
                "package-assembly-semantic-find/share",
                "Package assembly-semantic Find requests do not yet have a canonical Workspace Share projection."));
    }
}
