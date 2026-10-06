using System.Collections.Immutable;
using System.Text.Json.Serialization;

using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record TypeOverviewDocument
{
    public TypeOverviewDocument(
        TypeSubject subject,
        TypeMemberGroupPopulationResult members,
        int assemblyBytes)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Members = members
            ?? throw new ArgumentNullException(nameof(members));
        ArgumentOutOfRangeException.ThrowIfNegative(assemblyBytes);
        if (!Matches(subject, members.Binding))
        {
            throw new ArgumentException(
                "The Type subject and Member-group population binding must identify the same exact Type.",
                nameof(members));
        }
        if (members.Rows
                is not TypeMemberGroupRowsOutcome.Read rows)
        {
            throw new ArgumentException(
                "A Type overview requires compact Member-group Rows.",
                nameof(members));
        }
        if (rows.Ordering != members.Binding.Ordering
            || rows.Items.Any(row =>
                row.Binding.Population != members.Binding
                || !row.ExactMemberCount.HasValue)
            || rows.Continuation is { } continuation
                && continuation.Binding != members.Binding)
        {
            throw new ArgumentException(
                "Type overview Rows and exact-Member Counts must share the document population binding and ordering.",
                nameof(members));
        }

        AssemblyBytes = assemblyBytes;
    }

    public TypeSubject Subject { get; }
    public TypeMemberGroupPopulationResult Members { get; }
    public int AssemblyBytes { get; }

    internal static bool Matches(
        TypeSubject subject,
        TypeMemberGroupPopulationBinding binding) =>
        subject.Assembly == binding.Assembly
        && subject.ModuleVersionId == binding.ModuleVersionId
        && subject.Type == binding.Type
        && subject.TypeDefinitionToken
            == binding.TypeDefinitionToken;
}

public sealed record TypeDocument
{
    public TypeDocument(
        TypeSubject subject,
        TypeMemberGroupPopulationBinding memberGroups,
        ImmutableArray<MemberDeclaration> members,
        int assemblyBytes)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        MemberGroups = memberGroups
            ?? throw new ArgumentNullException(nameof(memberGroups));
        if (members.IsDefault)
        {
            throw new ArgumentException(
                "A complete Type document requires an explicit Member declaration population.",
                nameof(members));
        }
        Members = members;
        ArgumentOutOfRangeException.ThrowIfNegative(assemblyBytes);
        if (!TypeOverviewDocument.Matches(subject, memberGroups))
        {
            throw new ArgumentException(
                "The Type subject and Member-group population binding must identify the same exact Type.",
                nameof(memberGroups));
        }
        if (Members.Any(member =>
                !Matches(subject, memberGroups, member)))
        {
            throw new ArgumentException(
                "Every Member declaration must identify the Type document's exact Type.",
                nameof(members));
        }

        AssemblyBytes = assemblyBytes;
    }

    public TypeSubject Subject { get; }
    public TypeMemberGroupPopulationBinding MemberGroups { get; }
    public ImmutableArray<MemberDeclaration> Members { get; }
    public int AssemblyBytes { get; }

    private static bool Matches(
        TypeSubject subject,
        TypeMemberGroupPopulationBinding memberGroups,
        MemberDeclaration member) =>
        member.Subject.Group.DeclaringType == subject.Type
        && member.Subject.Population.Assembly == subject.Assembly
        && member.Subject.Population.ModuleVersionId
            == subject.ModuleVersionId
        && member.Subject.Population.DeclaringType == subject.Type
        && member.Subject.Population.TypeDefinitionToken
            == subject.TypeDefinitionToken
        && MatchesIntent(
            member.Subject.Population,
            member.Subject.Group,
            memberGroups);

    private static bool MatchesIntent(
        MemberOverloadPopulationBinding member,
        MemberGroupSubject group,
        TypeMemberGroupPopulationBinding memberGroups) =>
        group.Spelling == member.Spelling
        && member.Spelling == memberGroups.Spelling
        && member.IncludeHidden == memberGroups.IncludeHidden
        && member.Ordering == MemberOverloadOrdering.Metadata
        && memberGroups.Ordering
            == TypeMemberGroupOrdering.Metadata
        && Matches(
            member.Accessibility,
            memberGroups.Accessibility)
        && Matches(
            member.Receiver,
            memberGroups.Receiver);

    private static bool Matches(
        MemberOverloadAccessibilityFilter member,
        TypeMemberGroupAccessibilityFilter type) =>
        (member, type) switch
        {
            (MemberOverloadAccessibilityFilter.Public,
                TypeMemberGroupAccessibilityFilter.Public) => true,
            (MemberOverloadAccessibilityFilter.Protected,
                TypeMemberGroupAccessibilityFilter.Protected) => true,
            (MemberOverloadAccessibilityFilter.Internal,
                TypeMemberGroupAccessibilityFilter.Internal) => true,
            (MemberOverloadAccessibilityFilter.Private,
                TypeMemberGroupAccessibilityFilter.Private) => true,
            (MemberOverloadAccessibilityFilter.All,
                TypeMemberGroupAccessibilityFilter.All) => true,
            _ => false,
        };

    private static bool Matches(
        MemberOverloadReceiverFilter member,
        TypeMemberGroupReceiverFilter type) =>
        (member, type) switch
        {
            (MemberOverloadReceiverFilter.All,
                TypeMemberGroupReceiverFilter.All) => true,
            (MemberOverloadReceiverFilter.This,
                TypeMemberGroupReceiverFilter.This) => true,
            (MemberOverloadReceiverFilter.Static,
                TypeMemberGroupReceiverFilter.Static) => true,
            (MemberOverloadReceiverFilter.Extension,
                TypeMemberGroupReceiverFilter.Extension) => true,
            (MemberOverloadReceiverFilter.NonExtension,
                TypeMemberGroupReceiverFilter.NonExtension) => true,
            _ => false,
        };
}

public sealed record MemberOverviewDocument
{
    public MemberOverviewDocument(
        MemberGroupSubject subject,
        MemberOverloadPopulationBinding population,
        ImmutableArray<MemberDeclaration> members)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
        if (members.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A Member overview requires a non-empty exact-Member declaration population.",
                nameof(members));
        }
        Members = members;
        if (!Matches(subject, population))
        {
            throw new ArgumentException(
                "The Member group and exact-Member population binding must identify the same group.",
                nameof(population));
        }
        if (Members.Any(member =>
                member.Subject.Group != subject
                || member.Subject.Population != population))
        {
            throw new ArgumentException(
                "Every Member declaration must belong to the document's exact Member-group population.",
                nameof(members));
        }
    }

    public MemberGroupSubject Subject { get; }
    public MemberOverloadPopulationBinding Population { get; }
    public ImmutableArray<MemberDeclaration> Members { get; }

    private static bool Matches(
        MemberGroupSubject subject,
        MemberOverloadPopulationBinding population) =>
        subject.DeclaringType == population.DeclaringType
        && string.Equals(
            subject.Name,
            population.Name,
            StringComparison.Ordinal)
        && subject.Category == population.Category
        && subject.Role == population.Role
        && subject.Spelling == population.Spelling;
}

public sealed record MemberDocument : MemberDeclaration
{
    public MemberDocument(
        MemberSubject subject,
        InertString displaySignature,
        InertString canonicalSignature,
        InertString accessibility,
        MemberReceiver receiver)
        : base(
            subject,
            displaySignature,
            canonicalSignature,
            accessibility,
            receiver)
    {
    }
}

[JsonSourceGenerationOptions(
    Converters = [typeof(InertStringJsonConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TypeOverviewDocument))]
[JsonSerializable(typeof(TypeDocument))]
[JsonSerializable(typeof(MemberOverviewDocument))]
[JsonSerializable(typeof(MemberDocument))]
public partial class TypeMemberInspectionDocumentJsonContext
    : JsonSerializerContext;
