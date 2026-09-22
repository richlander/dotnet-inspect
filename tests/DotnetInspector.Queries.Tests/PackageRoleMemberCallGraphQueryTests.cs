using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageRoleMemberCallGraphQueryTests
{
    static string CallerPath =>
        FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();

    static string TargetPath =>
        FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();

    static string TargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    [Fact]
    public async Task
        ExecuteUsesExactRootImplementationAndReturnsDetachedExternalGraph()
    {
        PackageRootBinding caller = PackageBinding(
            "callgraph.caller",
            CallerPath);
        PackageRootBinding target = PackageBinding(
            "callgraph.target",
            TargetPath);
        (InspectionGraphDocument document,
            ImmutableArray<PackageRoleMemberCallGraphNodePackage>
                nodePackages) =
            await ExecuteAsync(caller, target);
        InspectionGraphEdge edge = Assert.Single(document.Edges);
        Assert.Equal(
            ("RunAcrossBoundary", "Forward"),
            (
                Member(document.Nodes[edge.FromNodeId]).Name,
                Member(document.Nodes[edge.ToNodeId]).Name));
        Assert.Equal(
            [
                (edge.FromNodeId, "callgraph.caller"),
                (edge.ToNodeId, "callgraph.target"),
            ],
            nodePackages
                .OrderBy(item => item.NodeId)
                .Select(item => (item.NodeId, item.Package.PackageId)));
    }

    [Fact]
    public void AmbiguousAssemblyNameOwnershipReturnsNoPackage()
    {
        PackageRootBinding firstTarget = PackageBinding(
            "callgraph.target.one",
            TargetPath);
        PackageRootBinding secondTarget = PackageBinding(
            "callgraph.target.two",
            TargetV2Path);
        var firstParticipant = new PackageAssemblyRoleParticipant(
            firstTarget.Root.Identity,
            Assert.Single(firstTarget.Root.AssetSelection.Assets),
            new AssemblyContextParticipant(
                ResolvedAssemblyReference.CreateFromPath(
                    TargetPath,
                    AssemblyResolutionProvenance.Local("query test")),
                NoResolverAssemblyBindingPolicy.Instance));
        var secondParticipant = new PackageAssemblyRoleParticipant(
            secondTarget.Root.Identity,
            Assert.Single(secondTarget.Root.AssetSelection.Assets),
            new AssemblyContextParticipant(
                ResolvedAssemblyReference.CreateFromPath(
                    TargetV2Path,
                    AssemblyResolutionProvenance.Local("query test")),
                NoResolverAssemblyBindingPolicy.Instance));

        (PackageRootIdentity? package, bool ambiguous) =
            PackageRoleMemberCallGraphQuery.MatchPackage(
                [firstParticipant, secondParticipant],
                identity: null,
                firstParticipant.Participant.Assembly.Identity.Name);

        Assert.Null(package);
        Assert.True(ambiguous);
    }

    private static async Task<(
        InspectionGraphDocument Document,
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> NodePackages)>
        ExecuteAsync(
            PackageRootBinding caller,
            params PackageRootBinding[] packages)
    {
        ImmutableArray<PackageRootBinding> bindings =
            [caller, .. packages];
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    bindings,
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        Assert.Equal(bindings.Length, scope.Packages.Length);

        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(bindings);
        PackageAssemblyContextCompletion completion =
            await operation.ExecuteAsync(operation.Identity);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection(bindings);
        try
        {
            PackageRoleMemberCallGraphOutcome outcome =
                PackageRoleMemberCallGraphQuery.Execute(
                    projection,
                    new PackageRoleMemberCallGraphFocus(
                        caller.Root.Identity,
                        ModuleVersionId(CallerPath),
                        MethodToken(
                            CallerPath,
                            "Entry",
                            "RunAcrossBoundary")),
                    new(
                        maxDepth: 2,
                        maxNodes: 10));
            PackageRoleMemberCallGraphOutcome.Unavailable? unavailable =
                outcome as PackageRoleMemberCallGraphOutcome.Unavailable;
            Assert.True(
                outcome is PackageRoleMemberCallGraphOutcome.Available,
                unavailable?.Failure.ToString());
            var available =
                (PackageRoleMemberCallGraphOutcome.Available)outcome;
            return (available.Document, available.NodePackages);
        }
        finally
        {
            await projection.ReturnAsync();
            PackageRoleCleanupReport cleanup =
                await completion.CloseAsync();
            Assert.DoesNotContain(
                cleanup.Groups,
                static group =>
                    group is PackageRoleGroupCleanupRecord.Failed);
        }
    }

    private static PackageRootBinding PackageBinding(
        string packageId,
        string assemblyPath)
    {
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>1.0.0</version>
                <authors>dotnet-inspect</authors>
                <description>Package-role call-graph fixture.</description>
              </metadata>
            </package>
            """);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream nuspec = archive.CreateEntry(
                $"{packageId}.nuspec").Open())
            {
                nuspec.Write(manifest);
            }
            using Stream assembly = archive.CreateEntry(
                $"lib/net11.0/{Path.GetFileName(assemblyPath)}").Open();
            assembly.Write(File.ReadAllBytes(assemblyPath));
        }

        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, "1.0.0"),
            new InMemoryPackageContent(
                stream.ToArray(),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(
            payload,
            "net11.0");
    }

    private static Guid ModuleVersionId(string assemblyPath)
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath);
        return session.ModuleVersionId();
    }

    private static int MethodToken(
        string assemblyPath,
        string typeName,
        string methodName)
    {
        Analysis.LibraryBodyIndex index =
            Analysis.LibraryBodyIndex.Open(assemblyPath);
        return index.Methods.Single(
            method => method.DeclaringType.Name == typeName
                && method.Name == methodName).MetadataToken;
    }

    private static Analysis.MemberRef Member(
        InspectionGraphNode node) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                node.Subject)
                .Identity)
            .Member;
}
