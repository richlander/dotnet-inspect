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
            || !IsMemberAccessibilityRepresentable(member)
            || !IsMethodSemanticsRepresentable(member)
            || member.SignatureModel is not { } signature
            || !IsMethodDeclarationHeaderRepresentable(member, signature)
            || !IsMethodDeclarationRepresentableOnType(type, member)
            || !IsFinalizerDeclarationRepresentable(member, signature)
            || !IsConstructorDeclarationRepresentable(member, signature)
            || !IsOperatorDeclarationRepresentable(type, member, signature)
            || !MethodCustomModifiersAreRepresentable(member, signature)
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
        {
            return TryGetOperatorDeclarationName(
                member.Name,
                out string? qualifier,
                out string operatorName)
                && (qualifier is null
                    || CSharpIdentifier.IsQualifiedTypeName(qualifier))
                && (qualifier is null
                    ? OperatorNames.GetStandaloneDeclarationParameterCount(
                        operatorName)
                    : OperatorNames.GetExplicitInterfaceDeclarationParameterCount(
                        operatorName)) is not null;
        }

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
                && CSharpIdentifier.IsQualifiedTypeName(
                    name[..memberSeparator])
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

    static bool IsMethodDeclarationHeaderRepresentable(
        ApiMember member,
        ApiSignature signature) =>
        !IsMethodLike(member)
        || signature.MethodDeclarationHeaderIsRepresentable == true;

    static bool IsMethodDeclarationRepresentableOnType(
        ApiType type,
        ApiMember member)
    {
        if (!IsMethodLike(member))
        {
            return true;
        }

        if (member.MethodModifiersAreRepresentable != true
            || member.MethodImplementationIsRepresentable != true
            || member.ReadOnlyMarkerIsRepresentable == false
            || member.IsReadOnly
                && (member.ReadOnlyMarkerIsRepresentable != true
                    || type.Kind != "struct"
                    || type.IsStatic
                    || member.Kind is not (
                        "method" or "explicit-interface-implementation")
                    || member.IsStatic))
        {
            return false;
        }

        if (type.Kind is not ("class" or "struct"))
            return true;

        bool hasProtectedAccessibility = member.Accessibility is
            "protected" or "protected internal" or "private protected";
        if ((type.IsStatic
                && (type.Kind != "class"
                    || !member.IsStatic
                    || hasProtectedAccessibility))
            || (type.Kind == "struct" && hasProtectedAccessibility))
        {
            return false;
        }

        if (member.Kind == "constructor")
            return !type.IsStatic || member.IsStatic;

        if (member.Kind == "finalizer")
            return type.Kind == "class" && !type.IsStatic;

        if (member.Kind == "operator")
            return true;

        if (member.Kind == "explicit-interface-implementation")
        {
            return !type.IsStatic
                && !member.IsAbstract
                && !member.IsOverride
                && !member.IsSealed;
        }

        if (member.IsStatic)
        {
            return !member.IsVirtual
                && !member.IsAbstract
                && !member.IsOverride
                && !member.IsSealed;
        }

        if (member.Accessibility == "private"
            && (member.IsVirtual
                || member.IsAbstract
                || member.IsOverride))
        {
            return false;
        }

        if (member.IsAbstract)
        {
            return type.Kind == "class"
                && type.IsAbstract
                && !type.IsSealed
                && member.IsVirtual
                && !member.IsSealed
                && member.HasMethodBody != true;
        }

        if (member.IsOverride)
        {
            return member.IsVirtual
                && (type.Kind == "class" || !member.IsSealed);
        }

        if (member.IsSealed)
            return false;

        return !member.IsVirtual
            || (type.Kind == "class" && !type.IsSealed);
    }

    static bool IsFinalizerDeclarationRepresentable(
        ApiMember member,
        ApiSignature signature)
    {
        bool hasFinalizerDeclarationShape =
            member.Name == "Finalize"
            && member.Accessibility == "protected"
            && !member.IsStatic
            && member.IsVirtual
            && member.IsOverride
            && !member.IsAbstract
            && !member.IsSealed
            && member.GenericArity == 0
            && signature.TypeParameters.Count == 0
            && signature.Parameters.Count == 0
            && IsVoid(signature.ReturnTypeShape);

        if (!hasFinalizerDeclarationShape)
            return member.Kind != "finalizer" && !member.IsFinalizer;

        return member.Kind == "finalizer" && member.IsFinalizer;
    }

    static bool IsMemberAccessibilityRepresentable(ApiMember member) =>
        !IsMethodLike(member)
        || member.AccessibilityIsRepresentable == true;

    static bool IsMethodSemanticsRepresentable(ApiMember member) =>
        !IsMethodLike(member)
        || member.MethodSemantics == ApiMethodSemanticsKind.None;

    static bool MethodCustomModifiersAreRepresentable(
        ApiMember member,
        ApiSignature signature)
        => !IsMethodLike(member)
            || signature.ReturnTypeCustomModifiersAreRepresentable == true
                && signature.Parameters.All(
                    parameter =>
                        parameter.CustomModifiersAreRepresentable == true);

    static bool IsMethodLike(ApiMember member) =>
        member.Kind is
            "method"
                or "extension-method"
                or "constructor"
                or "operator"
                or "finalizer"
                or "explicit-interface-implementation";

    static bool IsOperatorDeclarationRepresentable(
        ApiType type,
        ApiMember member,
        ApiSignature signature)
    {
        if (member.Kind != "operator")
            return true;

        if (!TryGetOperatorDeclarationName(
                member.Name,
                out string? qualifier,
                out string operatorName)
            || type.DefinitionName is not { } declaringType
            || type.Kind is not ("class" or "struct")
            || type.IsStatic
            || member.Accessibility is not null
            || !member.IsStatic
            || member.IsAbstract
            || member.IsVirtual
            || member.IsOverride
            || member.IsSealed
            || member.GenericArity != 0
            || signature.TypeParameters.Count != 0
            || signature.ReturnTypeShape is null
            || IsVoid(signature.ReturnTypeShape)
            || (qualifier is null
                ? OperatorNames.GetStandaloneDeclarationParameterCount(
                    operatorName)
                : OperatorNames.GetExplicitInterfaceDeclarationParameterCount(
                    operatorName)) is not int parameterCount
            || signature.Parameters.Count != parameterCount
            || signature.Parameters.Any(
                parameter => !string.IsNullOrEmpty(parameter.Modifier)))
        {
            return false;
        }

        if (qualifier is not null)
            return true;

        bool hasDeclaringOperand = signature.Parameters.Any(
            parameter => parameter.MatchesDeclaringType == true);
        if (!hasDeclaringOperand)
            return false;

        if (operatorName is "op_Increment" or "op_Decrement")
        {
            return signature.ReturnTypeMatchesDeclaringType == true;
        }

        if (operatorName is
            "op_LeftShift"
                or "op_RightShift"
                or "op_UnsignedRightShift")
        {
            return signature.Parameters[1].Type == "int";
        }

        return true;
    }

    static bool TryGetOperatorDeclarationName(
        string memberName,
        out string? qualifier,
        out string operatorName)
    {
        int separator = memberName.LastIndexOf('.');
        qualifier = separator > 0 ? memberName[..separator] : null;
        operatorName = memberName[(separator + 1)..];
        return operatorName.StartsWith("op_", StringComparison.Ordinal);
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
