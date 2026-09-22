namespace ILInspector.Metadata;

internal static class MetadataTypeIdentityValidator
{
    internal static string? ValidateInterfaceRequest(
        MetadataTypeIdentity? type)
    {
        if (type is MetadataTypeIdentity.Named
            {
                IsValueType: false,
                Definition: { } namedDefinition,
            })
        {
            return ValidateNamed(
                namedDefinition,
                expectedArguments: 0);
        }
        if (type is MetadataTypeIdentity.GenericInstance
            {
                IsValueType: false,
                Definition: { } genericDefinition,
            } generic)
        {
            if (generic.Arguments.IsDefaultOrEmpty)
            {
                return "The requested constructed interface identity has no arguments.";
            }
            string? definitionFailure =
                ValidateNamed(
                    genericDefinition,
                    generic.Arguments.Length);
            if (definitionFailure is not null)
                return definitionFailure;
            foreach (MetadataTypeIdentity? argument in generic.Arguments)
            {
                string? argumentFailure = ValidateType(argument);
                if (argumentFailure is not null)
                    return argumentFailure;
            }
            return null;
        }

        return "The requested interface identity must be a complete named reference type or constructed named reference type.";
    }

    static string? ValidateType(MetadataTypeIdentity? type) =>
        type switch
        {
            null =>
                "A requested interface type argument is missing.",
            MetadataTypeIdentity.Primitive primitive =>
                primitive.Name.Length == 0
                    ? "A primitive type identity is missing its name."
                    : null,
            MetadataTypeIdentity.Named
                {
                    Definition: { } definition,
                } =>
                ValidateNamed(definition, expectedArguments: 0),
            MetadataTypeIdentity.Named =>
                "A named type identity is missing its definition.",
            MetadataTypeIdentity.GenericInstance
                {
                    Definition: { } definition,
                } generic =>
                ValidateGeneric(definition, generic.Arguments),
            MetadataTypeIdentity.GenericInstance =>
                "A constructed type identity is missing its definition.",
            MetadataTypeIdentity.GenericParameter
                {
                    Index: < 0,
                } =>
                "A generic-parameter identity has a negative index.",
            MetadataTypeIdentity.GenericParameter =>
                null,
            MetadataTypeIdentity.SzArray array =>
                ValidateType(array.Element),
            MetadataTypeIdentity.Array array =>
                ValidateArray(array),
            MetadataTypeIdentity.Pointer pointer =>
                ValidateType(pointer.Element),
            MetadataTypeIdentity.ByReference byReference =>
                ValidateType(byReference.Element),
            MetadataTypeIdentity.FunctionPointer pointer =>
                ValidateSignature(pointer.Signature),
            MetadataTypeIdentity.Modified modified =>
                ValidateType(modified.Modifier)
                ?? ValidateType(modified.Type),
            MetadataTypeIdentity.Pinned pinned =>
                ValidateType(pinned.Type),
            _ =>
                "The requested interface identity contains an unknown type shape.",
        };

    static string? ValidateGeneric(
        MetadataNamedTypeIdentity definition,
        System.Collections.Immutable.ImmutableArray<
            MetadataTypeIdentity> arguments)
    {
        if (arguments.IsDefaultOrEmpty)
            return "A constructed type identity has no arguments.";
        string? definitionFailure =
            ValidateNamed(definition, arguments.Length);
        if (definitionFailure is not null)
            return definitionFailure;
        foreach (MetadataTypeIdentity? argument in arguments)
        {
            string? argumentFailure = ValidateType(argument);
            if (argumentFailure is not null)
                return argumentFailure;
        }
        return null;
    }

    static string? ValidateNamed(
        MetadataNamedTypeIdentity definition,
        int expectedArguments)
    {
        if (definition.Scope is null)
            return "A named type identity is missing its scope.";
        if (definition.Segments.IsDefaultOrEmpty
            || definition.Segments.Any(
                segment => segment.Length == 0))
        {
            return "A named type identity is missing a name segment.";
        }
        if (definition.IntroducedGenericParameterCounts.IsDefault
            || definition.IntroducedGenericParameterCounts.Length
                != definition.Segments.Length
            || definition.IntroducedGenericParameterCounts.Any(
                count => count < 0))
        {
            return "A named type identity has incomplete generic-arity evidence.";
        }

        long arity = 0;
        foreach (int count
            in definition.IntroducedGenericParameterCounts)
        {
            arity += count;
        }
        if (arity != expectedArguments)
        {
            return "A named type identity's authenticated arity does not match its supplied arguments.";
        }

        MetadataTypeScopeIdentity scope = definition.Scope;
        if (scope.Kind is MetadataTypeScopeKind.CurrentModule
                or MetadataTypeScopeKind.ModuleReference
            && scope.ModuleName is null)
        {
            return "A module-scoped type identity is missing its module name.";
        }
        if (scope.Kind
                == MetadataTypeScopeKind.AssemblyReference
            && scope.Assembly is null)
        {
            return "An assembly-scoped type identity is missing its assembly identity.";
        }
        if (scope.Assembly is { Name.Length: 0 })
        {
            return "An assembly identity is missing its name.";
        }
        return null;
    }

    static string? ValidateArray(MetadataTypeIdentity.Array array)
    {
        if (array.Rank <= 0
            || array.Sizes.IsDefault
            || array.LowerBounds.IsDefault)
        {
            return "An array type identity has incomplete shape evidence.";
        }
        return ValidateType(array.Element);
    }

    static string? ValidateSignature(
        MetadataMethodSignatureIdentity? signature)
    {
        if (signature is null
            || signature.GenericParameterCount < 0
            || signature.RequiredParameterCount < 0
            || signature.ParameterTypes.IsDefault)
        {
            return "A function-pointer identity has an incomplete signature.";
        }
        string? returnFailure = ValidateType(signature.ReturnType);
        if (returnFailure is not null)
            return returnFailure;
        foreach (MetadataTypeIdentity? parameter
            in signature.ParameterTypes)
        {
            string? parameterFailure = ValidateType(parameter);
            if (parameterFailure is not null)
                return parameterFailure;
        }
        return null;
    }
}
