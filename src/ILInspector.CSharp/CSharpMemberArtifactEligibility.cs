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
            || !IsConstructorDeclarationRepresentable(member, signature)
            || !IsOperatorDeclarationRepresentable(type, member, signature)
            || signature.TypeParameters.Any(parameter => !IsIdentifier(parameter.Name))
            || signature.Parameters.Any(parameter => !IsIdentifier(parameter.Name))
            || !ConstraintsAreRepresentable(type.TypeParameters)
            || !ConstraintsAreRepresentable(signature.TypeParameters)
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
            || member.IsFinalizer)
        {
            return true;
        }

        if (member.Kind == "operator")
            return OperatorNames.GetStandaloneDeclarationParameterCount(
                member.Name) is not null;

        string name = member.Kind is "method" or "extension-method"
            ? member.Name
            : member.SignatureModel?.MemberName is { Length: > 0 } modelName
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

    static bool IsConstructorDeclarationRepresentable(
        ApiMember member,
        ApiSignature signature)
    {
        if (member.Name == ".cctor")
        {
            return member.Accessibility == "private"
                && member.IsStatic
                && member.GenericArity == 0
                && signature.TypeParameters.Count == 0
                && signature.Parameters.Count == 0
                && IsVoid(signature.ReturnTypeShape);
        }

        if (member.Name != ".ctor")
            return true;

        return member.Kind == "constructor"
            && !member.IsStatic
            && member.GenericArity == 0
            && signature.TypeParameters.Count == 0
            && IsVoid(signature.ReturnTypeShape);
    }

    static bool IsOperatorDeclarationRepresentable(
        ApiType type,
        ApiMember member,
        ApiSignature signature)
    {
        if (member.Kind != "operator")
            return true;

        if (type.DefinitionName is not { } declaringType
            || type.Kind is not ("class" or "struct")
            || type.IsStatic
            || member.Accessibility is not null
            || !member.IsStatic
            || member.GenericArity != 0
            || signature.TypeParameters.Count != 0
            || signature.ReturnTypeShape is null
            || IsVoid(signature.ReturnTypeShape)
            || OperatorNames.GetStandaloneDeclarationParameterCount(
                member.Name) is not int parameterCount
            || signature.Parameters.Count != parameterCount
            || signature.Parameters.Any(
                parameter => !string.IsNullOrEmpty(parameter.Modifier)))
        {
            return false;
        }

        bool hasDeclaringOperand = signature.Parameters.Any(
            parameter => IsOpenConstructedDeclaringType(
                parameter.TypeShape,
                type,
                declaringType));
        if (!hasDeclaringOperand)
            return false;

        if (member.Name is "op_Increment" or "op_Decrement")
        {
            return IsOpenConstructedDeclaringType(
                signature.ReturnTypeShape,
                type,
                declaringType);
        }

        if (member.Name is
            "op_LeftShift"
                or "op_RightShift"
                or "op_UnsignedRightShift")
        {
            return signature.Parameters[1].Type == "int";
        }

        return true;
    }

    static bool IsOpenConstructedDeclaringType(
        ApiTypeShape? shape,
        ApiType type,
        MetadataTypeDefinitionName expected)
    {
        if (shape?.Definition?.DefinitionName is not { } actual
            || actual.Namespace != expected.Namespace
            || !actual.Segments.SequenceEqual(expected.Segments)
            || shape.IsValueType != (type.Kind == "struct"))
        {
            return false;
        }

        if (type.TypeParameters.Count == 0)
            return shape.Kind == ApiTypeShapeKind.Named;

        if (shape.Kind != ApiTypeShapeKind.GenericInstance
            || shape.DefinitionArityMatchesTypeArguments != true
            || shape.TypeArguments.Length != type.TypeParameters.Count)
        {
            return false;
        }

        for (int index = 0; index < shape.TypeArguments.Length; index++)
        {
            if (shape.TypeArguments[index] is not
                {
                    Kind: ApiTypeShapeKind.GenericParameter,
                    GenericParameterIndex: var parameterIndex,
                    IsMethodGenericParameter: false,
                }
                || parameterIndex != index)
            {
                return false;
            }
        }

        return true;
    }

    static bool IsVoid(ApiTypeShape? shape) =>
        shape is
        {
            Kind: ApiTypeShapeKind.Primitive,
            Primitive: ApiPrimitiveType.Void,
        };

    static bool ConstraintsAreRepresentable(
        IEnumerable<TypeParameter> parameters)
    {
        foreach (TypeParameter parameter in parameters)
        {
            if (parameter.StructuredConstraints is not { } constraints)
            {
                if (parameter.Constraints.Count > 0)
                    return false;
                continue;
            }

            if (constraints.Any(constraint => constraint.IsTypeName)
                && (parameter.ConstraintTypeDefinitionNames is null
                    || parameter.ConstraintTypeDefinitionNames.Any(
                        name => !IsRepresentable(name))))
            {
                return false;
            }
        }

        return true;
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
