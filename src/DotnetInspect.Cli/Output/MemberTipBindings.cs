using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Output;

internal sealed record MemberTipOverloadGroup(
    string Name,
    int Count);

internal sealed record MemberTipContext(
    string TypeName,
    ImmutableArray<MemberTipOverloadGroup> OverloadGroups,
    ImmutableArray<CliCommandToken> SourceTokens,
    string? PackageName,
    string? PackageVersion);

internal static class MemberTipBindings
{
    private static readonly Lazy<
        CliRelatedOperationBindingRegistry<MemberTipContext>> Registry =
        new(CreateRegistry);

    internal static Tip[] Resolve(
        ApiType type,
        string? platformAssembly,
        string? packagePath,
        string? assemblyPath,
        string? packageName,
        string? packageVersion)
    {
        ArgumentNullException.ThrowIfNull(type);

        MemberTipContext context = CreateContext(
            type,
            platformAssembly,
            packagePath,
            assemblyPath,
            packageName,
            packageVersion);
        return Registry.Value.Resolve(
            MemberRelatedOperationAffordances.All,
            context);
    }

    private static MemberTipContext CreateContext(
        ApiType type,
        string? platformAssembly,
        string? packagePath,
        string? assemblyPath,
        string? packageName,
        string? packageVersion)
    {
        ImmutableArray<MemberTipOverloadGroup> overloadGroups =
        [
            .. type.Members
                .Where(ApiMemberSectionDescriptors.IsMethodLike)
                .GroupBy(static member => member.Name)
                .Select(static group =>
                    new MemberTipOverloadGroup(
                        group.Key,
                        group.Count()))
                .OrderByDescending(static group => group.Count),
        ];

        return new(
            TypeMatcher.GetSimpleName(type.FullName),
            overloadGroups,
            CreateSourceTokens(
                platformAssembly,
                packagePath,
                assemblyPath,
                packageName),
            packageName,
            packageVersion);
    }

    private static ImmutableArray<CliCommandToken> CreateSourceTokens(
        string? platformAssembly,
        string? packagePath,
        string? assemblyPath,
        string? packageName)
    {
        if (!string.IsNullOrEmpty(platformAssembly))
        {
            return
            [
                CliCommandToken.Syntax("--platform"),
                CliCommandToken.ValueToken(platformAssembly),
            ];
        }

        if (!string.IsNullOrEmpty(packagePath))
        {
            return
            [
                CliCommandToken.Syntax("--package"),
                CliCommandToken.ValueToken(packageName ?? packagePath),
            ];
        }

        if (!string.IsNullOrEmpty(assemblyPath))
        {
            return
            [
                CliCommandToken.Syntax("--library"),
                CliCommandToken.ValueToken(assemblyPath),
            ];
        }

        return [];
    }

    private static CliRelatedOperationBindingRegistry<MemberTipContext>
        CreateRegistry() =>
        new(
            MemberRelatedOperationAffordances.All,
            [
                Binding(
                    "member.detail",
                    MemberRelatedOperationAffordances.InspectMember.Id,
                    100,
                    static context => !context.OverloadGroups.IsEmpty,
                    CreateMemberDetail),
                Binding(
                    "member.index",
                    MemberRelatedOperationAffordances.InspectMemberIndex.Id,
                    200,
                    static context =>
                        context.OverloadGroups.Any(
                            static group => group.Count > 1),
                    CreateMemberIndex),
                Binding(
                    "member.type-hierarchy",
                    MemberRelatedOperationAffordances
                        .InspectTypeHierarchy.Id,
                    300,
                    static _ => true,
                    CreateTypeHierarchy),
                Binding(
                    "member.dotted-syntax",
                    MemberRelatedOperationAffordances.InspectMember.Id,
                    400,
                    static _ => true,
                    CreateDottedMember),
                Binding(
                    "member.version-diff",
                    MemberRelatedOperationAffordances.CompareTypeVersions.Id,
                    500,
                    static context =>
                        !string.IsNullOrEmpty(context.PackageName)
                        && !string.IsNullOrEmpty(context.PackageVersion),
                    CreateVersionDiff),
            ]);

    private static CliRelatedOperationBinding<MemberTipContext> Binding(
        string id,
        RelatedOperationAffordanceId affordanceId,
        int order,
        Func<MemberTipContext, bool> isApplicable,
        Func<MemberTipContext, CliRelatedOperationGesture> createGesture) =>
        new(
            new CliRelatedOperationBindingId(id),
            affordanceId,
            order,
            isApplicable,
            createGesture);

    private static CliRelatedOperationGesture CreateMemberDetail(
        MemberTipContext context) =>
        new(
            [
                CliCommandToken.Syntax(MemberCommand.Name),
                CliCommandToken.ValueToken(context.TypeName),
                .. context.SourceTokens,
                CliCommandToken.ValueToken(
                    $"{context.OverloadGroups[0].Name}:1"),
            ],
            "view member detail (source, IL)");

    private static CliRelatedOperationGesture CreateMemberIndex(
        MemberTipContext context) =>
        new(
            [
                CliCommandToken.Syntax(MemberCommand.Name),
                CliCommandToken.ValueToken(context.TypeName),
                .. context.SourceTokens,
                CliCommandToken.Syntax("-S"),
                CliCommandToken.ValueToken("Member Index"),
            ],
            "full selector/identity table");

    private static CliRelatedOperationGesture CreateTypeHierarchy(
        MemberTipContext context) =>
        new(
            [
                CliCommandToken.Syntax(TypeCommand.Name),
                CliCommandToken.ValueToken(context.TypeName),
                .. context.SourceTokens,
                CliCommandToken.Syntax("--tree"),
            ],
            "view type tree");

    private static CliRelatedOperationGesture CreateDottedMember(
        MemberTipContext context) =>
        new(
            [
                CliCommandToken.Syntax(MemberCommand.Name),
                CliCommandToken.Syntax("-m"),
                CliCommandToken.ValueToken(
                    $"{context.TypeName}."
                    + (context.OverloadGroups.IsEmpty
                        ? "Method"
                        : context.OverloadGroups[0].Name)),
                .. context.SourceTokens,
            ],
            "dotted member syntax");

    private static CliRelatedOperationGesture CreateVersionDiff(
        MemberTipContext context) =>
        new(
            [
                CliCommandToken.Syntax(DiffCommand.Name),
                CliCommandToken.Syntax("--package"),
                CliCommandToken.ValueToken(
                    $"{context.PackageName}@<prev>..{context.PackageVersion}"),
                CliCommandToken.Syntax("-t"),
                CliCommandToken.ValueToken(context.TypeName),
            ],
            "compare API changes");
}
