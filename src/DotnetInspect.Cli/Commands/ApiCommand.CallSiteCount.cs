using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public partial class ApiCommand
{
    internal static int? TryWriteCallSiteCount(
        ApiSourceResult source,
        string? typeName,
        MemberOptions options,
        TextWriter output)
    {
        if (!IsCallSiteCountRequest(options)
            || options.RouterDeferredTypeOrMember
            || options.HasCallerScope
            || options.EffectiveDiscovery
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || options.KindFilter.Count != 0
            || options.UnsafeOnly
            || options.MemberDigest is not null
            || options.MemberGenericArity is not null
            || string.IsNullOrWhiteSpace(typeName)
            || !TryGetSingleMemberFilter(
                options.MemberFilter,
                out string memberName))
        {
            return null;
        }

        string? assemblyPath =
            ApiServices.FindApiDll(
                source.SearchPath,
                source.Context.Logger);
        if (assemblyPath is null)
            return null;

        int methodToken;
        using (AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assemblyPath))
        {
            int? selected = options.IncludeAll
                ? ResolveMetadataOnlyMethod(session, typeName, memberName, options)
                : null;
            selected ??=
                session.MethodBodies.ResolveApiMethodOverload(
                    typeName,
                    memberName,
                    options.OverloadIndex is { } overloadIndex
                        ? overloadIndex - 1
                        : null,
                    options.IncludeAll)
                ?.MetadataToken;
            selected ??= ResolveDeclaredTypeMethod(
                session,
                typeName,
                memberName,
                options);
            if (selected is not { } token)
                return null;

            methodToken = token;
        }

        return WriteCallSiteCount(
            assemblyPath,
            methodToken,
            options,
            output);
    }

    // The existing metadata-only selection, admitted only under --all because
    // it applies no public-scope hidden or obsolete admission.
    static int? ResolveMetadataOnlyMethod(
        AssemblyInspectionSession session,
        string typeName,
        string memberName,
        MemberOptions options)
    {
        MethodBodySelection? method =
            options.OverloadIndex is null or 1
                ? session.MethodBodies.ResolveUniqueMethod(
                    typeName,
                    memberName,
                    publicOnly: false)
                : null;
        if (method is null
            && options.OverloadIndex is { } accessorIndex)
        {
            method = session.MethodBodies.ResolveAccessorMethod(
                typeName,
                memberName,
                accessorIndex - 1,
                publicOnly: false);
        }
        return method?.MetadataToken;
    }

    // Resolves the selector exactly as the complete member route does, through
    // MemberTargetResolver, but over the selected Type's own declarations
    // instead of the whole assembly's API surface. A same-image extension
    // method with this name would be projected onto receiver Types by the
    // complete surface, and an unselected overload set is resolved by the
    // complete route's auto-selection, so both keep the complete route.
    static int? ResolveDeclaredTypeMethod(
        AssemblyInspectionSession session,
        string typeName,
        string memberName,
        MemberOptions options)
    {
        if (memberName.AsSpan().IndexOfAny('*', '?') >= 0
            || session.MethodBodies.DeclaresExtensionMethod(memberName)
            || session.MethodBodies.ExtractDeclaredType(
                typeName,
                options.IncludeAll) is not { } declared)
        {
            return null;
        }

        MemberTargetResolution resolution = MemberTargetResolver.Resolve(
            declared,
            new MemberTargetSelector(
                memberName,
                memberName,
                options.OverloadIndex));
        if (resolution.Target is not { } target
            || (options.OverloadIndex is null
                && resolution.Candidates.Count != 1))
        {
            return null;
        }

        return target.Body?.MetadataToken;
    }

    static bool TryGetSingleMemberFilter(
        HashSet<string> filters,
        out string memberName)
    {
        memberName = "";
        if (filters.Count != 1)
            return false;

        using HashSet<string>.Enumerator enumerator =
            filters.GetEnumerator();
        if (!enumerator.MoveNext())
            return false;

        memberName = enumerator.Current;
        return true;
    }

    static int? TryWriteCallSiteCount(
        ApiType type,
        ApiOptions options,
        TextWriter output)
    {
        if (options is not MemberOptions memberOptions
            || !IsCallSiteCountRequest(memberOptions)
            || memberOptions.OverloadIndex is null
            || memberOptions.DllPath is not { } assemblyPath)
        {
            return null;
        }

        if (memberOptions.SelectedBodyMethodToken is { } selectedMethodToken)
        {
            return WriteCallSiteCount(
                assemblyPath,
                selectedMethodToken,
                memberOptions,
                output);
        }

        IReadOnlySet<string> requestedSections =
            GetRequestedMemberSections(type, memberOptions);
        List<ApiMember> methods =
            ApiOutputFormatter.ResolveBodyMethods(
                type,
                requestedSections);
        ApiMember? method = ApiOutputFormatter.SelectBodyMethod(
            type,
            methods,
            memberOptions.OverloadIndex!.Value - 1);
        if (method?.MetadataToken is not { } methodToken)
        {
            CommandError.Write(
                $"section '{SectionNames.Calls}' produced no payload.");
            return 1;
        }

        return WriteCallSiteCount(
            assemblyPath,
            methodToken,
            memberOptions,
            output);
    }

    static bool IsCallSiteCountRequest(
        MemberOptions options) =>
        options is
        {
            Count: true,
            IncludeSections: { Count: 1 } sections,
        }
        && sections.Contains(SectionNames.Calls);

    static int WriteCallSiteCount(
        string assemblyPath,
        int methodToken,
        MemberOptions memberOptions,
        TextWriter output)
    {
        MemberCallSiteCountResult result =
            MemberCallSiteCountQuery.Execute(
                assemblyPath,
                methodToken);
        if (result is MemberCallSiteCountResult.Failed failed)
        {
            CommandError.Write(
                $"Calls Count failed: {failed.Error.Message}");
            return 1;
        }
        if (result is MemberCallSiteCountResult.Incomplete incomplete)
        {
            string detail = incomplete.Analysis.UnavailableBodies
                    .Select(static body => body.Diagnostic?.Message)
                    .FirstOrDefault(static message =>
                        !string.IsNullOrWhiteSpace(message))
                ?? incomplete.Analysis.Diagnostics
                    .Select(static diagnostic => diagnostic.Message)
                    .FirstOrDefault()
                ?? "one or more physical bodies did not complete.";
            CommandError.Write(
                $"Calls Count is incomplete: {detail}");
            return 1;
        }

        var available =
            (MemberCallSiteCountResult.Available)result;
        int count = available.Count;
        if (!CliSemanticRowSelection.TrySelectCount(
                memberOptions.CallRowSelection,
                count,
                static (stage, required, available) =>
                    $"Member Calls row selection stage {stage} "
                    + $"requires call row {required}, but only "
                    + $"{available} call rows are available.",
                out count))
        {
            return 1;
        }
        if (memberOptions.Rows is { IsUnlimited: false } window)
        {
            (int start, int end) = window.Resolve(count);
            count = end - start;
        }

        var projection = new CountProjection();
        projection.RecordRows(SectionNames.Calls, count);
        IReadOnlyList<string>? ordered =
            OutputFormatter.ResolveCountMapSections(
                ApiMemberSectionPipelines.Create(memberOptions),
                memberOptions.IncludeSections,
                fixedOverview: false);
        string rendered = CountOutput.Render(
            projection,
            ordered,
            memberOptions.Format,
            memberOptions.NoHeader);
        CountOutput.WriteCountResult(rendered, output);
        return 0;
    }
}
