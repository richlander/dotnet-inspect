using System.Reflection;

namespace ILInspector.Metadata;

/// <summary>
/// Projects property and event rows into the physical accessor methods that own their bodies.
/// </summary>
public static class ApiMemberAccessors
{
    public static IEnumerable<ApiMember> Create(
        ApiMember member,
        ApiType type)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(type);

        string declaringType = string.IsNullOrEmpty(member.DeclaringType)
            ? type.FullName
            : member.DeclaringType;
        switch (member.Kind)
        {
            case "property":
                if (member.GetterToken is { } getter)
                {
                    yield return Accessor(
                        member,
                        declaringType,
                        $"get_{member.Name}",
                        getter,
                        member.GetterHasMethodBody,
                        "get",
                        valueReturning: true);
                }
                if (member.SetterToken is { } setter)
                {
                    yield return Accessor(
                        member,
                        declaringType,
                        $"set_{member.Name}",
                        setter,
                        member.SetterHasMethodBody,
                        "set",
                        valueReturning: false);
                }
                break;
            case "event":
                if (member.AdderToken is { } adder)
                {
                    yield return Accessor(
                        member,
                        declaringType,
                        $"add_{member.Name}",
                        adder,
                        member.AdderHasMethodBody,
                        "add",
                        valueReturning: false);
                }
                if (member.RemoverToken is { } remover)
                {
                    yield return Accessor(
                        member,
                        declaringType,
                        $"remove_{member.Name}",
                        remover,
                        member.RemoverHasMethodBody,
                        "remove",
                        valueReturning: false);
                }
                break;
        }
    }

    static ApiMember Accessor(
        ApiMember owner,
        string declaringType,
        string fallbackName,
        int token,
        bool? hasMethodBody,
        string accessorKind,
        bool valueReturning)
    {
        ApiSignature? ownerModel = owner.SignatureModel;
        ApiAccessor? accessorEntry =
            ownerModel?.Accessors.FirstOrDefault(
                accessor => accessor.Kind == accessorKind);
        string name = string.IsNullOrEmpty(accessorEntry?.Name)
            ? fallbackName
            : accessorEntry.Name;
        string valueType =
            ownerModel?.ReturnType ?? owner.ReturnType ?? "object";
        List<ApiParameter> parameters =
            ownerModel?.Parameters is { Count: > 0 } indexParameters
                ? indexParameters.Select(CloneParameter).ToList()
                : [];
        string returnType;
        if (valueReturning)
        {
            returnType = valueType;
        }
        else
        {
            returnType = "void";
            parameters.Add(
                new ApiParameter
                {
                    Name = "value",
                    Type = valueType,
                });
        }

        string? physicalAccessibility = accessorKind switch
        {
            "get" => owner.GetterAccessibility,
            "set" or "init" => owner.SetterAccessibility,
            "add" => owner.AdderAccessibility,
            "remove" => owner.RemoverAccessibility,
            _ => null,
        };
        MethodAttributes? physicalMethodAccess = accessorKind switch
        {
            "get" => owner.GetterPhysicalMethodAccess,
            "set" or "init" => owner.SetterPhysicalMethodAccess,
            "add" => owner.AdderPhysicalMethodAccess,
            "remove" => owner.RemoverPhysicalMethodAccess,
            _ => null,
        };
        string? accessibility =
            physicalAccessibility
            ?? (string.IsNullOrEmpty(accessorEntry?.Accessibility)
                ? owner.Accessibility
                : accessorEntry.Accessibility);
        bool isHidden = accessorKind switch
        {
            "get" => owner.GetterIsHidden,
            "set" or "init" => owner.SetterIsHidden,
            "add" => owner.AdderIsHidden,
            "remove" => owner.RemoverIsHidden,
            _ => false,
        };
        string renderedParameters = string.Join(
            ", ",
            parameters.Select(
                parameter =>
                    $"{parameter.TypeWithModifier} {parameter.Name}"));
        bool isExplicitImplementation =
            accessorEntry?.IsExplicitInterfaceImplementation
            ?? name.Contains('.', StringComparison.Ordinal);
        ApiMethodImplementationFacts? implementation =
            owner.AccessorImplementations?.FirstOrDefault(
                facts => facts.MethodToken == token);
        bool? accessorHasBody =
            implementation?.HasBodyRva ?? hasMethodBody;
        ApiMethodModifiers modifiers = implementation is not null
            ? ApiMethodModifiers.FromAttributes(
                implementation.Attributes,
                isExplicitImplementation)
            : new(
                owner.IsStatic,
                owner.IsVirtual,
                owner.IsAbstract && accessorHasBody != true,
                owner.IsOverride,
                owner.IsSealed);
        return new ApiMember
        {
            Name = name,
            Kind = isExplicitImplementation
                ? "explicit-interface-implementation"
                : "method",
            IsHidden = isHidden,
            MethodSemantics = accessorKind switch
            {
                "get" => ApiMethodSemanticsKind.PropertyGetter,
                "set" or "init" => ApiMethodSemanticsKind.PropertySetter,
                "add" => ApiMethodSemanticsKind.EventAdder,
                "remove" => ApiMethodSemanticsKind.EventRemover,
                _ => null,
            },
            MetadataToken = token,
            DeclaringType = declaringType,
            ReturnType = returnType,
            Signature =
                $"{returnType} {name}({renderedParameters})",
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = returnType,
                Parameters = parameters,
            },
            IsStatic = modifiers.IsStatic,
            IsVirtual = modifiers.IsVirtual,
            IsAbstract = modifiers.IsAbstract,
            IsOverride = modifiers.IsOverride,
            IsSealed = modifiers.IsSealed,
            IsUnsafe = owner.IsUnsafe,
            IsReadOnly = accessorEntry?.IsReadOnly == true
                || owner.IsReadOnly,
            MemorySafety = owner.AccessorMemorySafety?.FirstOrDefault(
                facts => facts.CallerContract.Evidence.MemberToken == token),
            MethodImplementation = implementation,
            HasMethodBody = accessorHasBody,
            Accessibility = accessibility,
            PhysicalMethodAccess = physicalMethodAccess,
            Documentation = owner.Documentation,
        };
    }

    static ApiParameter CloneParameter(ApiParameter parameter) => new()
    {
        Name = parameter.Name,
        Type = parameter.Type,
        CanonicalType = parameter.CanonicalType,
        Modifier = parameter.Modifier,
        HasDefault = parameter.HasDefault,
        DefaultValueText = parameter.DefaultValueText,
        Attributes = [.. parameter.Attributes],
    };
}
