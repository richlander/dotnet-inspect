using ILInspector.Findings;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum ExtensionMemberKind
{
    Method,
    Property,
}

/// <summary>
/// An extension member bound to Metadata's stable member identity.
/// Rendered declaration text remains outside the Finding payload.
/// </summary>
public sealed record ExtensionMemberObservation(
    MemberAnchor Anchor,
    ExtensionMemberKind Kind,
    string ExtendedType,
    string ReturnType,
    string? Assembly = null)
{
    MemberAnchor _anchor = Anchor ?? throw new ArgumentNullException(nameof(Anchor));
    string _extendedType = ExtendedType ?? throw new ArgumentNullException(nameof(ExtendedType));
    string _returnType = ReturnType ?? throw new ArgumentNullException(nameof(ReturnType));

    public MemberAnchor Anchor
    {
        get => _anchor;
        init => _anchor = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ExtendedType
    {
        get => _extendedType;
        init => _extendedType = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ReturnType
    {
        get => _returnType;
        init => _returnType = value ?? throw new ArgumentNullException(nameof(value));
    }
}

public static partial class MetadataFindings
{
    public static readonly FindingDescriptor ExtensionMemberDescriptor =
        new("metadata.extension-member", "Extension member");

    public static FindingInspection<ExtensionMemberObservation> InspectExtensionMembers(
        IEnumerable<ExtensionMethodInfo> members,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectExtensionMembersCore(members, subject, nameof(members));
    }

    static FindingInspection<ExtensionMemberObservation> InspectExtensionMembersCore(
        IEnumerable<ExtensionMethodInfo> members,
        FindingSubject subject,
        string parameterName)
    {
        var materialized = members
            .Select(member => member is null
                ? throw new ArgumentException(
                    "Extension member inventory cannot contain null observations.",
                    parameterName)
                : member)
            .ToArray();

        if (materialized.Any(static member =>
                member.Anchor is null
                || member.CanonicalExtendedType is null
                || member.ReturnType is null))
        {
            return new FindingInspection<ExtensionMemberObservation>.Failed(
                new InspectionError(
                    subject,
                    ExtensionMemberDescriptor,
                    "One or more extension members do not have structured member shape."));
        }

        if (materialized.Any(static member => member.Kind is not ("method" or "property")))
        {
            return new FindingInspection<ExtensionMemberObservation>.Failed(
                new InspectionError(
                    subject,
                    ExtensionMemberDescriptor,
                    "One or more extension members have an unsupported member kind."));
        }

        return InspectInventory(
            materialized.Select(static member => new ExtensionMemberObservation(
                member.Anchor!,
                member.Kind == "method" ? ExtensionMemberKind.Method : ExtensionMemberKind.Property,
                member.CanonicalExtendedType!,
                member.ReturnType!,
                member.Assembly)),
            subject,
            ExtensionMemberDescriptor,
            static member => JoinSortKey(
                member.Anchor.CanonicalSignature,
                member.Kind.ToString()),
            static member => JoinSortKey(
                member.Anchor.CanonicalSignature,
                member.Kind.ToString(),
                member.ExtendedType,
                JoinSortKey(member.ReturnType, member.Assembly)),
            parameterName);
    }

    public static FindingComparison<ExtensionMemberObservation> CompareExtensionMembers(
        IEnumerable<ExtensionMethodInfo> oldMembers,
        IEnumerable<ExtensionMethodInfo> newMembers,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldMembers);
        ArgumentNullException.ThrowIfNull(newMembers);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectExtensionMembersCore(oldMembers, subject, nameof(oldMembers)),
            InspectExtensionMembersCore(newMembers, subject, nameof(newMembers)));
    }
}
