using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

public partial class ApiCommand
{
    static int? TryWriteDirectCallCount(
        ApiType type,
        ApiOptions options,
        TextWriter output)
    {
        if (options is not MemberOptions
            {
                Count: true,
                DllPath: { } assemblyPath,
                OverloadIndex: { } overloadIndex,
                IncludeSections: { Count: 1 } sections,
            } memberOptions
            || !sections.Contains(SectionNames.Calls))
        {
            return null;
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
            overloadIndex - 1);
        if (method?.MetadataToken is not { } methodToken)
            return null;

        MemberCallSiteCountResult result =
            MemberCallSiteCountQuery.Execute(
                assemblyPath,
                methodToken);
        if (result is MemberCallSiteCountResult.Failed failed)
        {
            CommandError.Write(
                $"Direct call Count failed: {failed.Error.Message}");
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
                $"Direct call Count is incomplete: {detail}");
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
