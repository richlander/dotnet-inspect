using CSharpText;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.CSharp;

/// <summary>
/// Metadata-only admission for a member declaration that can retain its exact
/// identifiers in a product-owned C# artifact.
/// </summary>
public static class CSharpMemberArtifactEligibility
{
    public static bool IsRepresentable(ApiType type, ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);

        if (type.DefinitionName is not { } definitionName
            || !IsRepresentable(definitionName)
            || type.TypeParameters.Any(parameter => !IsIdentifier(parameter.Name))
            || member.SignatureDecodeStatus is not null
            || !IsMemberNameRepresentable(member)
            || member.SignatureModel is not { } signature
            || signature.TypeParameters.Any(parameter => !IsIdentifier(parameter.Name))
            || signature.Parameters.Any(parameter => !IsIdentifier(parameter.Name))
            || !ReferencesAreRepresentable(type.BaseTypeReference.AsEnumerable())
            || !ReferencesAreRepresentable(type.InterfaceReferences)
            || !ReferencesAreRepresentable(signature.ReturnTypeReferences)
            || !ReferencesAreRepresentable(
                signature.Parameters.SelectMany(parameter => parameter.TypeReferences)))
        {
            return false;
        }

        return new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(type, members: [member]))
            is CSharpTypePrintOutcome.Printed;
    }

    static bool IsMemberNameRepresentable(ApiMember member)
    {
        if (member.Name is ".ctor" or ".cctor"
            || member.IsFinalizer
            || member.Kind == "operator")
        {
            return true;
        }

        string name = member.SignatureModel?.MemberName is { Length: > 0 } modelName
            ? modelName
            : member.Name;
        if (name == "this[]")
            return true;

        if (member.Kind == "explicit-interface-implementation")
        {
            int memberSeparator = name.LastIndexOf('.');
            return memberSeparator > 0
                && IsQualifiedName(name[..memberSeparator])
                && (name[(memberSeparator + 1)..] == "this[]"
                    || IsIdentifier(name[(memberSeparator + 1)..]));
        }

        return IsIdentifier(name);
    }

    static bool ReferencesAreRepresentable(
        IEnumerable<ApiTypeReferenceIdentity> references)
        => references.All(reference =>
            reference.DefinitionName is { } definitionName
            && IsRepresentable(definitionName));

    static bool IsRepresentable(MetadataTypeDefinitionName name)
    {
        if (!IsQualifiedName(name.Namespace))
            return false;

        foreach (string segment in name.Segments)
        {
            string simpleName = MetadataNameArity.TryReadSuffix(
                segment,
                out _,
                out int simpleNameLength)
                    ? segment[..simpleNameLength]
                    : segment;
            if (!IsIdentifier(simpleName))
                return false;
        }

        return true;
    }

    static bool IsQualifiedName(string name)
        => name.Length == 0
            || name.Split('.').All(segment => segment.Length > 0 && IsIdentifier(segment));

    static bool IsIdentifier(string name)
        => CSharpIdentifier.AdmitTypeDeclaration(name)
            is CSharpTypeDeclarationIdentifierAdmission.Admitted;

    static IEnumerable<ApiTypeReferenceIdentity> AsEnumerable(
        this ApiTypeReferenceIdentity? reference)
    {
        if (reference is not null)
            yield return reference;
    }
}
