using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public static class ExternalCallGraphCommand
{
    public const string Name = "calls";

    public static async Task<int> ExecuteAsync(
        ExternalCallGraphOptions options,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            options,
            CreateLoadOptions(options),
            cancellationToken);

    internal static async Task<int> ExecuteAsync(
        ExternalCallGraphOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadOptions);

        string[] packageInputs =
        [
            options.RootPackage,
            .. options.Packages,
        ];
        if (!InspectionGraphCommand.TryCreateMembers(
                packageInputs,
                out WorkspaceMemberCoordinate[] members))
        {
            return 1;
        }

        var rootMember =
            (WorkspaceMemberCoordinate.PackageMember)members[0];
        var contextInput = new WorkspaceContextInput
        {
            Framework = options.Tfm,
            Members = members,
        };

        await using var workspace = new InspectionWorkspace();
        WorkspaceContextLoadOutcome outcome =
            await WorkspaceContextLoader.LoadAsync(
                workspace,
                contextInput,
                loadOptions,
                cancellationToken);

        if (outcome is WorkspaceContextLoadOutcome.Failed failed)
        {
            CommandError.Write(
                "The external call-graph workspace could not be loaded.",
                [
                    .. failed.Failures.Select(static failure =>
                        $"{failure.Kind}: {failure.Message}"),
                ]);
            return 1;
        }

        WorkspaceContextLoadOutcome.Loaded loaded =
            (WorkspaceContextLoadOutcome.Loaded)outcome;
        using AssemblyContextGroup group = loaded.Group;

        if (!TryResolveFocus(
                group,
                loaded.Members,
                rootMember,
                options,
                out ResolvedFocus focus))
        {
            return 1;
        }

        InspectionGraphDocument document;
        try
        {
            using var session = new MemberCallGraphSession(
                group,
                focus.Participant.Assembly,
                focus.MethodToken);
            if (!session.HasCrossLibraryScope)
            {
                CommandError.Write(
                    "The external call graph requires at least one additional assembly participant.",
                    [
                        "Add an external --package in the same target framework.",
                    ]);
                return 1;
            }
            document = session.CrossLibraryCalleeNeighborhood(
                new MemberCallGraphCalleeNeighborhoodRequest(
                    options.Depth,
                    options.MaxNodes));
        }
        catch (MemberCallGraphAcquisitionException ex)
        {
            CommandError.Write(
                ex.Message,
                [.. ex.Failures.Select(FormatAcquisitionFailure)]);
            return 1;
        }
        catch (InspectionQueryException ex)
        {
            CommandError.Write(ex.Message);
            return 1;
        }

        try
        {
            return ExternalCallGraphOutputAdapter.Write(
                document,
                options);
        }
        catch (InspectionQueryException ex)
        {
            CommandError.Write(ex.Message);
            return 1;
        }
    }

    static WorkspaceContextLoadOptions CreateLoadOptions(
        ExternalCallGraphOptions options) =>
        new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization =
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            UseVersionCache = true,
            Log = options.Verbose
                ? CommandError.WriteLine
                : null,
        };

    static bool TryResolveFocus(
        AssemblyContextGroup group,
        IReadOnlyList<WorkspaceContextMember> loadedMembers,
        WorkspaceMemberCoordinate.PackageMember rootMember,
        ExternalCallGraphOptions options,
        out ResolvedFocus focus)
    {
        var matches = new List<FocusMatch>();
        var suggestions =
            new SortedSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        ApiSurfaceScope scope = options.IncludeAll
            ? ApiSurfaceScope.IncludeAll
            : ApiSurfaceScope.Public;

        foreach (WorkspaceContextMember member in loadedMembers)
        {
            if (!Equals(member.Declared, rootMember))
                continue;

            AssemblyContextEntry<AssemblyApiSurface> result =
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    member.Participant,
                    scope);
            switch (result)
            {
                case AssemblyContextEntry<AssemblyApiSurface>.Available
                    available:
                {
                    ApiTypeLookupResult lookup =
                        ApiTypeLookupService.LookupType(
                            available.Value.Surface,
                            options.TypeName);
                    foreach (string suggestion in lookup.Suggestions)
                        suggestions.Add(suggestion);
                    if (lookup.Type is not null)
                    {
                        matches.Add(
                            new FocusMatch(
                                member.Participant,
                                lookup.Type));
                    }
                    break;
                }
                case AssemblyContextEntry<AssemblyApiSurface>.Rejected
                    rejected:
                    failures.Add(
                        $"{rejected.Subject.Identity.Name}: {rejected.Failure.Kind}: {rejected.Failure.Detail}");
                    break;
                case AssemblyContextEntry<AssemblyApiSurface>.Failed failed:
                    failures.Add(
                        $"{failed.Subject.Identity.Name}: {failed.Error.Message}");
                    break;
            }
        }

        if (matches.Count == 0)
        {
            List<string> details = [.. failures];
            details.AddRange(
                ApiTypeLookupResult.SuggestionDetails([.. suggestions]));
            if (!options.IncludeAll)
            {
                details.Add(string.Empty);
                details.Add(
                    "Pass --all when the focus type is non-public.");
            }
            CommandError.Write(
                $"Type '{options.TypeName}' was not found in root package '{options.RootPackage}'.",
                [.. details]);
            focus = default!;
            return false;
        }

        if (matches.Count > 1)
        {
            CommandError.Write(
                $"Type '{options.TypeName}' matched multiple root-package assemblies.",
                [
                    .. matches.Select(match =>
                        $"  {match.Participant.Assembly.Identity.Name}: {match.Type.FullName}"),
                ]);
            focus = default!;
            return false;
        }

        FocusMatch match = matches[0];
        MemberTargetResolution resolution =
            MemberTargetResolver.Resolve(
                match.Type,
                MemberTargetSelector.Parse(options.Member));
        if (resolution.Diagnostic is { } diagnostic)
        {
            CommandError.Write(
                diagnostic.Message,
                [.. diagnostic.CandidateDetails()]);
            focus = default!;
            return false;
        }

        ResolvedMemberTarget target = resolution.Target!;
        if (target.Body?.MetadataToken is not { } methodToken)
        {
            CommandError.Write(
                $"Member selector '{target.NormalizedSelector}' has no exact MethodDef body.",
                [
                    "Select a method, constructor, or exact property/event accessor.",
                ]);
            focus = default!;
            return false;
        }

        focus = new ResolvedFocus(
            match.Participant,
            methodToken);
        return true;
    }

    static string FormatAcquisitionFailure(
        MemberCallGraphAcquisitionFailure failure) =>
        failure switch
        {
            MemberCallGraphAcquisitionFailure.Rejected rejected =>
                $"{rejected.Assembly.Identity.Name}: {rejected.Failure.Kind}: {rejected.Failure.Detail}",
            MemberCallGraphAcquisitionFailure.InvalidImage invalid =>
                $"{invalid.Assembly.Identity.Name}: {invalid.Error.Message}",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

    sealed record FocusMatch(
        AssemblyContextParticipant Participant,
        ApiType Type);

    sealed record ResolvedFocus(
        AssemblyContextParticipant Participant,
        int MethodToken);
}
