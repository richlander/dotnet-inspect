using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Services;
using DotnetInspector.Networking;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;
using static DotnetInspect.Cli.Commands.WorkspaceTypeInspectionTarget;

namespace DotnetInspect.Cli.Commands;

public static partial class MemberCommand
{
    internal static Task<int> ExecuteAsync(
        MemberOptions options,
        WorkspaceContextLoadOptions capabilities) =>
        PackageReferenceAdmission.IsCandidate(options.PackagePath)
            ? ExecutePackageReferenceAsync(options,
                ResolvedMemberInspectionPlan.FromCompatibilityOptions(options), capabilities)
            : ExecuteWorkspaceAsync(options,
                ResolvedMemberInspectionPlan.FromCompatibilityOptions(options), capabilities);

    static async Task<int> ExecuteWithShareAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        ApiSourceResult? source = null,
        ApiServices.LoadedApiSurface? loaded = null,
        Func<ApiType, ShareProjectionPlan, InspectionShare>? project = null)
    {
        if (options.ShareFormat is null)
            return await ExecuteCoreAsync(options, plan, source, loaded).ConfigureAwait(false);

        InspectionShare share = new InspectionShare.NonProjectable(
            "member.selection",
            "Share requires one exact member overload selected with "
                + "Name~<digest>, Name:N, or --index N.");
        string? refusal = MemberShareProjection.ValidateOptions(options);
        MemberOptions contentOptions = options with { ShareFormat = null };
        int exitCode = await ExecuteCoreAsync(
            contentOptions,
            plan,
            source,
            loaded,
            options.ShareFormat is null ? null : (resolvedSource, surface, type, member) =>
            {
                share = refusal is not null
                    ? new InspectionShare.NonProjectable("member.options", refusal)
                    : project is not null
                        ? project(type, member)
                        : MemberShareProjection.Project(
                            resolvedSource, surface, type, member);
            }).ConfigureAwait(false);
        if (options.ShareFormat is not { } format)
            return exitCode;
        // Failure cannot produce a successful-looking navigation scalar.
        if (exitCode != 0)
            return exitCode;
        return WorkspaceShareOutput.Write(share, format);
    }

    static async Task<int> ExecuteWorkspaceAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        WorkspaceContextLoadOptions capabilities)
    {
        if (string.IsNullOrWhiteSpace(options.TypeName)
            || options.TypeName.IndexOfAny(['*', '?']) >= 0)
        {
            CommandError.Write("--workspace requires one explicit exact Type selector.");
            return 1;
        }
        WorkspacePacketRestorationResult result =
            await WorkspacePacketRestoration.RestoreAsync(
                options.WorkspacePacket!, capabilities, CancellationToken.None)
                .ConfigureAwait(false);
        if (result is WorkspacePacketRestorationResult.Failed failed)
        {
            CommandError.Write(failed.Summary, failed.Details);
            return 1;
        }
        WorkspacePacketRestoration restoration =
            ((WorkspacePacketRestorationResult.Restored)result).Value;
        return await restoration.ExecuteAsync(async active =>
        {
            SelectedContextExactTypeLiveTarget? target = null;
            var envelope = SelectedContextExactTypeInspectionOperation
                .ExecuteWithLiveTarget(
                    active.Workspace,
                    active.Activation,
                    new SelectedContextExactTypeInspectionRequest(options.TypeName),
                    selected => target = selected,
                    scope: options.IncludeAll
                        ? ApiSurfaceScope.IncludeAll
                        : ApiSurfaceScope.PublicWithNonPublicTypes);
            if (!envelope.Content.Inspection.IsAvailable)
            {
                TypeCommand.WriteExactTypeNonSuccess(
                    envelope.Content.Inspection,
                    envelope.Diagnostics,
                    envelope.Content.DefiningSources.Select(
                        TypeCommand.FormatDefiningSource));
                return 1;
            }
            SelectedContextExactTypeSource defining =
                envelope.Content.DefiningSources.Single();
            SelectedContextExactTypeLiveTarget selectedTarget = target
                ?? throw new InvalidOperationException(
                    "An available Workspace Type requires a live target.");
            var selectedContext = active.Activation.SelectedContext!;
            WorkspaceDeclarationMember member = selectedContext.Receipt.Members
                .Single(candidate => Equals(candidate.Occurrence, defining.Occurrence));
            TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            MemberOptions effective = options with { WorkspacePacket = null };
            int outputExitCode = await WorkspaceTypeInspectionTarget.ExecuteAsync(
                effective,
                ExactTypeRenderSource.From(defining),
                selectedTarget,
                (source, loaded) => ExecuteWithShareAsync(
                    effective, plan, source, loaded,
                    (type, selected) =>
                    {
                        if (!envelope.Content.Inspection.IsComplete)
                            return new InspectionShare.NonProjectable(
                                "member.context", "Selected-context Type evidence is incomplete.");
                        if (type.DefinitionName is null)
                            return new InspectionShare.NonProjectable(
                                "member.type", "The selected Type has no structured identity.");
                        WorkspaceSharePacketProjectionResult projection =
                            WorkspaceTypeScenarioProjection.ProjectMember(
                                active.Activation,
                                selectedContext,
                                member,
                                type.DefinitionName,
                                selected.Basis.Target.Member.Fingerprint,
                                selected.Facet);
                        if (!projection.Succeeded)
                            return new InspectionShare.NonProjectable(
                                projection.Failure!.Path, projection.Failure.Message);
                        string encoded = WorkspaceSharePacketCodec.Encode(projection.Packet!);
                        return new InspectionShare.Available(
                            WorkspaceShareUrl.Create(encoded), encoded);
                    })).ConfigureAwait(false);
            return outputExitCode != 0 || !envelope.Content.Inspection.IsComplete ? 1 : 0;
        }).ConfigureAwait(false);
    }

    static WorkspaceContextLoadOptions CreateWorkspaceLoadOptions(MemberOptions options) =>
        new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization = new SourcePolicyPackageSourceAuthorization(options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            UseVersionCache = true,
            Log = options.Verbose ? CommandError.WriteLine : null,
        };
}
