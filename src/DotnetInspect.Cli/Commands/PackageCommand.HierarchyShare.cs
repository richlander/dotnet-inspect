using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspect.Cli.Commands;

public partial class PackageCommand
{
    private static int WriteDirectHierarchyShare(
        int exitCode,
        InspectionOptions options,
        PackageReferenceTarget target,
        PackageExtractionResult resolution)
    {
        if (options.ShareFormat is not { } format
            || options.WorkspacePacket is not null)
        {
            return exitCode;
        }

        int shareExitCode = WorkspaceShareOutput.Write(
            ProjectDirectHierarchyShare(options, target, resolution),
            format);
        return Math.Max(exitCode, shareExitCode);
    }

    private static InspectionShare ProjectDirectHierarchyShare(
        InspectionOptions options,
        PackageReferenceTarget target,
        PackageExtractionResult resolution)
    {
        static InspectionShare Refuse(string reason) =>
            new InspectionShare.NonProjectable(
                "package/dependency-hierarchy", reason);

        if (target.IsLocalFile || resolution.ToolWrapperChain.Count != 0)
        {
            return Refuse(
                "The Dependency Hierarchy link requires a NuGet package "
                    + "that the Browser can reacquire.");
        }

        if (options.Rows is not null
            || options.DependencyQueryPlan is { MaximumDepth: not null }
            || options.DependencyQueryPlan?.HierarchyRows.Operations.Count > 0)
        {
            return Refuse(
                "The Browser cannot yet preserve the requested hierarchy "
                    + "depth or row selection.");
        }

        string? packageId = resolution.PackageName;
        string? version = resolution.Version;
        if (packageId is null
            || !PackageCoordinateResolver.IsCanonicalPackageId(packageId)
            || version is null)
        {
            return Refuse(
                "The resolved Package has no exact portable package, "
                    + "version, and target framework coordinate.");
        }
        if (!PackageExtractor.TryNormalizePackageVersion(
                version, out string normalizedVersion)
            || !DependsShareProjection.TryNormalizeFramework(
                options.Tfm
                    ?? TraversalTargetFrameworkPolicy.ProductDefaultTargetFramework,
                out string? framework))
        {
            return Refuse(
                "The resolved Package has no exact portable package, "
                    + "version, and target framework coordinate.");
        }

        PackageSourceAuthorization authorization =
            new SourcePolicyPackageSourceAuthorization(options.SourceOptions)
                .AuthorizeSourcesFor(packageId);
        if (authorization.DenialReason is { } denial)
            return Refuse($"The package source policy refused sharing: {denial}");
        if (authorization.Sources.Count != 1
            || !authorization.Sources[0].IsNuGetOrg)
        {
            return Refuse(
                "The Browser cannot preserve this package source selection; "
                    + "sharing currently requires NuGet.org.");
        }

        const int schema = InspectionDefinitionSchema.Version1;
        var coordinate = new DefinitionMemberCoordinate.PackageCoordinate(
            packageId, normalizedVersion, framework);
        var workspace = new WorkspaceDefinition(
            schema,
            WorkspaceSharePacketTransposer.WorkspaceId,
            [new WorkspaceContextDefinition(
                "g0", framework, members: [coordinate])]);
        var navigation = new NavigationDefinition(
            schema,
            WorkspaceSharePacketTransposer.NavigationId,
            [new NavigationTabDefinition("t0", coordinate: coordinate)],
            "t0");
        var view = new ViewDefinition(
            schema,
            WorkspaceSharePacketTransposer.ViewId,
            lens: "hierarchy");
        var scenario = new ScenarioDefinition(
            schema,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: "g0",
            view: view.Id,
            navigation: navigation.Id);
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                new WorkspaceSharePacketDefinitionSet(
                    workspace, navigation, view, scenario));
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure!;
            return new InspectionShare.NonProjectable(
                failure.Path, failure.Message);
        }

        string packet = WorkspaceSharePacketCodec.Encode(projection.Packet!);
        return new InspectionShare.Available(
            WorkspaceShareUrl.Create(packet), packet);
    }
}
