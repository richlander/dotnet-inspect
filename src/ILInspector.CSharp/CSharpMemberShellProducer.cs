using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpShellMemberKind
{
    PropertyGet,
    PropertySet,
    EventAdd,
    EventRemove,
    Constructor,
    Method,
    Field,
}

public enum CSharpShellBodyKind
{
    None,
    Throw,
    ThrowInit,
    ThrowGetSet,
    ThrowGetInit,
    TargetBody,
    TargetGetterWithSetter,
    TargetGetterWithInitSetter,
    TargetSetterWithGetter,
    TargetInitSetterWithGetter,
    TargetInitBody,
    TargetEventAccessorWithSibling,
    AutoProperty,
    AutoPropertyGetSet,
    AutoPropertyGetInit,
    InitOnlyProperty,
    FieldInitializer,
}

public enum CSharpShellAccessibility
{
    Public,
    Protected,
}

public sealed record CSharpShellParameter(
    string Name,
    string Type,
    string? Modifier = null,
    IReadOnlyList<string>? Attributes = null,
    bool HasDefault = false,
    string? DefaultValueText = null);

public sealed record CSharpShellTypeParameter(
    string Name,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<TypeParameterConstraint>? StructuredConstraints = null,
    TypeParameterTypeKind TypeKind = TypeParameterTypeKind.Undetermined);

public sealed record CSharpMemberShellSpec(
    string Name,
    CSharpShellMemberKind Kind,
    bool IsStatic,
    IReadOnlyList<CSharpShellParameter> Parameters,
    string? ReturnType,
    IReadOnlyList<CSharpShellTypeParameter> TypeParameters,
    CSharpShellBodyKind BodyKind,
    string? Body,
    IReadOnlyList<string>? Attributes = null,
    IReadOnlyList<string>? ReturnAttributes = null,
    bool IsAbstract = false,
    bool IsVirtual = false,
    bool IsOverride = false,
    bool IsSealed = false,
    bool IsAsync = false,
    bool IsExtension = false,
    CSharpShellAccessibility Accessibility = CSharpShellAccessibility.Public,
    string? ConstructorInitializer = null,
    string? ExplicitInterfaceMemberName = null,
    string? DeclarationSignature = null,
    bool RequiresUnsafeModifier = false,
    string? SiblingBody = null,
    int? MetadataToken = null,
    int? GetterToken = null,
    int? SetterToken = null,
    int? AdderToken = null,
    int? RemoverToken = null);

/// <summary>
/// Composes product-owned C# member models and body policies from a neutral shell
/// specification. Consumers select members and bodies; this seam owns their C#
/// declaration and accessor shape.
/// </summary>
public static class CSharpMemberShellProducer
{
    public static CSharpMemberPolicy BuildPolicy(
        CSharpMemberShellSpec spec,
        int primaryConstructorParameterCount = 0)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (primaryConstructorParameterCount < 0)
            throw new ArgumentOutOfRangeException(nameof(primaryConstructorParameterCount));

        ValidateBodyKind(spec);
        ValidateExplicitInterfaceMemberName(spec);
        ValidateConstructorInitializer(spec);
        var member = BuildMember(spec);
        return spec.BodyKind switch
        {
            CSharpShellBodyKind.None
                or CSharpShellBodyKind.AutoProperty
                or CSharpShellBodyKind.AutoPropertyGetSet
                or CSharpShellBodyKind.AutoPropertyGetInit
                or CSharpShellBodyKind.InitOnlyProperty
                => new(member, CSharpBodyPolicy.Skeleton),
            CSharpShellBodyKind.Throw or CSharpShellBodyKind.ThrowInit
                when spec.Kind is CSharpShellMemberKind.PropertyGet or CSharpShellMemberKind.PropertySet
                => new(member, CSharpBodyPolicy.Stub, PropertyBody(spec, CSharpAccessorBody.Throw)),
            CSharpShellBodyKind.Throw
                when spec.Kind is CSharpShellMemberKind.EventAdd or CSharpShellMemberKind.EventRemove
                => new(
                    member,
                    CSharpBodyPolicy.Stub,
                    new CSharpEventBody(CSharpAccessorBody.Throw, CSharpAccessorBody.Throw)),
            CSharpShellBodyKind.Throw
                when spec.Kind == CSharpShellMemberKind.Constructor
                    && primaryConstructorParameterCount > 0
                => new(
                    member,
                    CSharpBodyPolicy.Stub,
                    new CSharpBlockBody(
                        "throw null;",
                        new CSharpConstructorInitializer(
                            CSharpConstructorInitializerKind.This,
                            Enumerable.Repeat("default", primaryConstructorParameterCount).ToArray()))),
            CSharpShellBodyKind.Throw
                when spec.Kind == CSharpShellMemberKind.Constructor
                    && CSharpFormatter.ParseConstructorInitializer(spec.ConstructorInitializer) is { } initializer
                => new(
                    member,
                    CSharpBodyPolicy.Stub,
                    new CSharpBlockBody("throw null;", initializer)),
            CSharpShellBodyKind.Throw
                => new(member, CSharpBodyPolicy.Stub),
            CSharpShellBodyKind.ThrowGetSet
                or CSharpShellBodyKind.ThrowGetInit
                => new(
                    member,
                    CSharpBodyPolicy.Stub,
                    new CSharpPropertyBody(CSharpAccessorBody.Throw, CSharpAccessorBody.Throw)),
            CSharpShellBodyKind.TargetBody
                when spec.Kind == CSharpShellMemberKind.Field
                => new(member, CSharpBodyPolicy.Full, new CSharpFieldInitializer(RequiredBody(spec))),
            CSharpShellBodyKind.TargetBody
                when spec.Kind is CSharpShellMemberKind.PropertyGet or CSharpShellMemberKind.PropertySet
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    PropertyBody(spec, TargetAccessorBody(RequiredBody(spec)))),
            CSharpShellBodyKind.TargetInitBody
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    PropertyBody(spec, TargetAccessorBody(RequiredBody(spec)))),
            CSharpShellBodyKind.TargetBody
                when spec.Kind is CSharpShellMemberKind.EventAdd or CSharpShellMemberKind.EventRemove
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    EventBody(spec, TargetAccessorBody(RequiredBody(spec)))),
            CSharpShellBodyKind.TargetEventAccessorWithSibling
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    EventBody(
                        spec,
                        TargetAccessorBody(RequiredBody(spec)),
                        CSharpAccessorBody.Block(RequiredSiblingBody(spec)))),
            CSharpShellBodyKind.TargetBody
                when spec.Kind == CSharpShellMemberKind.Constructor
                    && primaryConstructorParameterCount > 0
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    TargetBlockBody(
                        RequiredBody(spec),
                        new CSharpConstructorInitializer(
                            CSharpConstructorInitializerKind.This,
                            Enumerable.Repeat("default", primaryConstructorParameterCount).ToArray()))),
            CSharpShellBodyKind.TargetBody
                when spec.Kind == CSharpShellMemberKind.Constructor
                    && CSharpFormatter.ParseConstructorInitializer(spec.ConstructorInitializer) is { } initializer
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    TargetBlockBody(RequiredBody(spec), initializer)),
            CSharpShellBodyKind.TargetBody
                => new(member, CSharpBodyPolicy.Full, TargetBlockBody(RequiredBody(spec))),
            CSharpShellBodyKind.TargetGetterWithSetter
                or CSharpShellBodyKind.TargetGetterWithInitSetter
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        TargetAccessorBody(RequiredBody(spec)),
                        CSharpAccessorBody.Throw)),
            CSharpShellBodyKind.TargetSetterWithGetter
                or CSharpShellBodyKind.TargetInitSetterWithGetter
                => new(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        CSharpAccessorBody.Throw,
                        TargetAccessorBody(RequiredBody(spec)))),
            CSharpShellBodyKind.FieldInitializer
                => new(member, CSharpBodyPolicy.Full, new CSharpFieldInitializer(RequiredBody(spec))),
            _ => throw new NotSupportedException(
                $"Unsupported C# shell member body shape '{spec.BodyKind}'."),
        };
    }

    public static ApiParameter BuildParameter(CSharpShellParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        string type = parameter.Type;
        string? modifier = parameter.Modifier;
        if (type.StartsWith("ref ", StringComparison.Ordinal))
        {
            type = type["ref ".Length..];
            modifier ??= "ref";
        }

        return new ApiParameter
        {
            Attributes = parameter.Attributes?.ToList() ?? [],
            Name = parameter.Name,
            Type = type,
            Modifier = modifier,
            HasDefault = parameter.HasDefault,
            DefaultValueText = parameter.DefaultValueText,
        };
    }

    static ApiMember BuildMember(CSharpMemberShellSpec spec)
    {
        bool isProperty = spec.Kind is CSharpShellMemberKind.PropertyGet or CSharpShellMemberKind.PropertySet;
        bool isEvent = spec.Kind is CSharpShellMemberKind.EventAdd or CSharpShellMemberKind.EventRemove;
        bool isExplicitInterface = spec.ExplicitInterfaceMemberName is not null;
        var member = new ApiMember
        {
            Name = spec.ExplicitInterfaceMemberName ?? spec.Name,
            Kind = isExplicitInterface && (isProperty || isEvent || spec.Kind == CSharpShellMemberKind.Method)
                ? "explicit-interface-implementation"
                : spec.Kind switch
                {
                    CSharpShellMemberKind.PropertyGet or CSharpShellMemberKind.PropertySet => "property",
                    CSharpShellMemberKind.EventAdd or CSharpShellMemberKind.EventRemove => "event",
                    CSharpShellMemberKind.Constructor => "constructor",
                    CSharpShellMemberKind.Method => "method",
                    CSharpShellMemberKind.Field => "field",
                    _ => throw new NotSupportedException(
                        $"Unsupported C# shell member kind '{spec.Kind}'."),
                },
            ReturnType = spec.ReturnType,
            Signature = DeclarationSignature(spec),
            IsStatic = spec.IsStatic,
            IsAbstract = spec.IsAbstract,
            IsVirtual = spec.IsVirtual,
            IsOverride = spec.IsOverride,
            IsSealed = spec.IsSealed,
            Accessibility = spec.Accessibility switch
            {
                CSharpShellAccessibility.Public => "public",
                CSharpShellAccessibility.Protected => "protected",
                _ => throw new NotSupportedException(
                    $"Unsupported C# shell accessibility '{spec.Accessibility}'."),
            },
            Attributes = spec.Attributes?.ToList() ?? [],
            IsUnsafe = spec.RequiresUnsafeModifier || RequiresUnsafe(spec),
            IsAsync = spec.IsAsync,
            IsExtension = spec.IsExtension,
            IsConst = spec.Kind == CSharpShellMemberKind.Field
                && spec.BodyKind == CSharpShellBodyKind.TargetBody,
            MetadataToken = spec.MetadataToken,
            GetterToken = spec.GetterToken,
            SetterToken = spec.SetterToken,
            AdderToken = spec.AdderToken,
            RemoverToken = spec.RemoverToken,
        };

        if (spec.Kind != CSharpShellMemberKind.Field)
        {
            member.SignatureModel = new ApiSignature
            {
                ReturnType = spec.ReturnType,
                ReturnAttributes = spec.Kind == CSharpShellMemberKind.Method
                    ? spec.ReturnAttributes?.ToList() ?? []
                    : [],
                MemberName = spec.TypeParameters.Count == 0
                    ? spec.Name
                    : $"{spec.Name}<{string.Join(", ", spec.TypeParameters.Select(parameter => parameter.Name))}>",
                TypeParameters = spec.TypeParameters
                    .Select(parameter => new TypeParameter
                    {
                        Name = parameter.Name,
                        Constraints = parameter.Constraints.ToList(),
                        StructuredConstraints = parameter.StructuredConstraints,
                        TypeKind = parameter.TypeKind,
                    })
                    .ToList(),
                Parameters = spec.Parameters.Select(BuildParameter).ToList(),
            };
            if (isProperty)
            {
                member.SignatureModel.MemberName = spec.Parameters.Count > 0
                    ? "this[]"
                    : member.Name;
                member.SignatureModel.Accessors = PropertyAccessors(spec);
            }
            else if (isEvent)
            {
                member.SignatureModel.MemberName = member.Name;
                member.SignatureModel.Accessors =
                [
                    new ApiAccessor { Kind = "add" },
                    new ApiAccessor { Kind = "remove" },
                ];
            }
        }

        return member;
    }

    static string? DeclarationSignature(CSharpMemberShellSpec spec)
    {
        if (spec.Kind != CSharpShellMemberKind.Method
            || spec.ExplicitInterfaceMemberName is null)
        {
            return spec.DeclarationSignature;
        }

        if (string.IsNullOrWhiteSpace(spec.ReturnType))
        {
            throw new ArgumentException(
                "Explicit-interface method shells require a return type.",
                nameof(spec));
        }

        string typeParameters = spec.TypeParameters.Count == 0
            ? ""
            : $"<{string.Join(", ", spec.TypeParameters.Select(parameter => parameter.Name))}>";
        string parameters = string.Join(
            ", ",
            spec.Parameters.Select(parameter =>
                CSharpDeclarationWriter.FormatParameter(BuildParameter(parameter))));
        return $"{spec.ReturnType} {spec.ExplicitInterfaceMemberName}{typeParameters}({parameters})";
    }

    static void ValidateExplicitInterfaceMemberName(CSharpMemberShellSpec spec)
    {
        if (spec.ExplicitInterfaceMemberName is not { } name)
            return;

        bool supportsExplicitInterfaceName = spec.Kind is CSharpShellMemberKind.Method
            or CSharpShellMemberKind.PropertyGet
            or CSharpShellMemberKind.PropertySet
            or CSharpShellMemberKind.EventAdd
            or CSharpShellMemberKind.EventRemove;
        if (!supportsExplicitInterfaceName)
        {
            throw new ArgumentException(
                $"Member kind '{spec.Kind}' does not support an explicit-interface name.",
                nameof(spec));
        }

        if (HasInvalidExplicitInterfaceNameShape(name))
        {
            throw new ArgumentException(
                "Explicit-interface member names must be qualified as 'Interface.Member'.",
                nameof(spec));
        }
    }

    static bool HasInvalidExplicitInterfaceNameShape(string name)
    {
        int angleDepth = 0;
        int parenthesisDepth = 0;
        int bracketDepth = 0;
        bool segmentHasContent = false;
        bool hasTopLevelSeparator = false;
        char? previous = null;
        for (int index = 0; index < name.Length; index++)
        {
            char ch = name[index];
            if (CSharpIdentifier.RequiresLiteralEscape(ch)
                || (char.IsWhiteSpace(ch) && (angleDepth == 0 || ch is not (' ' or '\t'))))
            {
                return true;
            }

            if (char.IsWhiteSpace(ch))
            {
                char? next = NextNonWhitespace(name, index + 1);
                if (parenthesisDepth == 0
                    && previous is { } previousChar
                    && next is { } nextChar
                    && !IsTypeNameSeparator(previousChar)
                    && !IsTypeNameSeparator(nextChar))
                {
                    return true;
                }
                continue;
            }

            if (ch == '<')
            {
                if (previous is null or '<' or ',' or '.')
                    return true;
                angleDepth++;
            }
            else if (ch == '>')
            {
                if (angleDepth == 0 || previous is '<' or ',')
                    return true;
                angleDepth--;
            }
            else if (ch == '(')
            {
                parenthesisDepth++;
            }
            else if (ch == ')')
            {
                if (parenthesisDepth == 0)
                    return true;
                parenthesisDepth--;
            }
            else if (ch == '[')
            {
                bracketDepth++;
            }
            else if (ch == ']')
            {
                if (bracketDepth == 0)
                    return true;
                bracketDepth--;
            }
            else if (ch == ',')
            {
                char? next = NextNonWhitespace(name, index + 1);
                if (bracketDepth == 0
                    && (angleDepth == 0
                    || previous is null or '<' or ',' or '.'
                    || next is null or '>' or ',' or '.'))
                {
                    return true;
                }
            }
            else if (ch == '.')
            {
                char? next = NextNonWhitespace(name, index + 1);
                if (previous is null or '<' or ',' or '.'
                    || next is null or '>' or ',' or '.')
                {
                    return true;
                }
                if (angleDepth == 0)
                {
                    if (!segmentHasContent)
                        return true;
                    segmentHasContent = false;
                    hasTopLevelSeparator = true;
                    previous = ch;
                    continue;
                }
            }
            segmentHasContent = true;
            previous = ch;
        }
        return angleDepth != 0
            || parenthesisDepth != 0
            || bracketDepth != 0
            || !segmentHasContent
            || !hasTopLevelSeparator;
    }

    static char? NextNonWhitespace(string value, int start)
    {
        for (int index = start; index < value.Length; index++)
        {
            if (!char.IsWhiteSpace(value[index]))
                return value[index];
        }
        return null;
    }

    static bool IsTypeNameSeparator(char ch)
        => ch is '<' or '>' or ',' or '.' or '(' or ')' or '[' or ']' or '?' or '*' or '&' or ':';

    static void ValidateConstructorInitializer(CSharpMemberShellSpec spec)
    {
        if (spec.ConstructorInitializer is null)
            return;

        if (spec.Kind != CSharpShellMemberKind.Constructor
            || CSharpFormatter.ParseConstructorInitializer(spec.ConstructorInitializer) is null)
        {
            throw new ArgumentException(
                "Constructor initializers require a constructor shell and a valid 'this(...)' or 'base(...)' chain.",
                nameof(spec));
        }
    }

    static void ValidateBodyKind(CSharpMemberShellSpec spec)
    {
        bool isProperty = spec.Kind is CSharpShellMemberKind.PropertyGet
            or CSharpShellMemberKind.PropertySet;
        bool isEvent = spec.Kind is CSharpShellMemberKind.EventAdd
            or CSharpShellMemberKind.EventRemove;
        bool isValid = spec.BodyKind switch
        {
            CSharpShellBodyKind.None
                => spec.ExplicitInterfaceMemberName is null
                    || spec.Kind is not (CSharpShellMemberKind.Method or CSharpShellMemberKind.PropertySet),
            CSharpShellBodyKind.Throw => spec.Kind != CSharpShellMemberKind.Field,
            CSharpShellBodyKind.ThrowInit
                => spec.Kind == CSharpShellMemberKind.PropertySet,
            CSharpShellBodyKind.InitOnlyProperty
                => spec.Kind == CSharpShellMemberKind.PropertySet
                    && spec.ExplicitInterfaceMemberName is null,
            CSharpShellBodyKind.ThrowGetSet
                or CSharpShellBodyKind.ThrowGetInit
                or CSharpShellBodyKind.AutoPropertyGetSet
                or CSharpShellBodyKind.AutoPropertyGetInit
                => isProperty,
            CSharpShellBodyKind.AutoProperty
                => spec.Kind == CSharpShellMemberKind.PropertyGet,
            CSharpShellBodyKind.TargetBody => true,
            CSharpShellBodyKind.TargetGetterWithSetter
                or CSharpShellBodyKind.TargetGetterWithInitSetter
                => spec.Kind == CSharpShellMemberKind.PropertyGet,
            CSharpShellBodyKind.TargetSetterWithGetter
                or CSharpShellBodyKind.TargetInitSetterWithGetter
                or CSharpShellBodyKind.TargetInitBody
                => spec.Kind == CSharpShellMemberKind.PropertySet,
            CSharpShellBodyKind.TargetEventAccessorWithSibling => isEvent,
            CSharpShellBodyKind.FieldInitializer => spec.Kind == CSharpShellMemberKind.Field,
                _ => false,
        };
        if (!isValid)
        {
            throw new ArgumentException(
                $"C# shell body shape '{spec.BodyKind}' is not valid for member kind '{spec.Kind}'.",
                nameof(spec));
        }
    }

    static List<ApiAccessor> PropertyAccessors(CSharpMemberShellSpec spec)
    {
        bool isAutoGetInit = spec.BodyKind == CSharpShellBodyKind.AutoPropertyGetInit;
        bool setterIsInit = spec.BodyKind is CSharpShellBodyKind.TargetGetterWithInitSetter
            or CSharpShellBodyKind.TargetInitSetterWithGetter
            or CSharpShellBodyKind.TargetInitBody
            or CSharpShellBodyKind.ThrowGetInit
            or CSharpShellBodyKind.ThrowInit
            or CSharpShellBodyKind.InitOnlyProperty;
        bool hasGetter = isAutoGetInit
            || spec.Kind == CSharpShellMemberKind.PropertyGet
            || spec.BodyKind is CSharpShellBodyKind.AutoPropertyGetSet
                or CSharpShellBodyKind.ThrowGetSet
                or CSharpShellBodyKind.ThrowGetInit
                or CSharpShellBodyKind.TargetSetterWithGetter
                or CSharpShellBodyKind.TargetInitSetterWithGetter;
        bool hasSetter = !isAutoGetInit
            && (spec.Kind == CSharpShellMemberKind.PropertySet
                || spec.BodyKind is CSharpShellBodyKind.AutoPropertyGetSet
                    or CSharpShellBodyKind.ThrowGetSet
                    or CSharpShellBodyKind.ThrowGetInit
                    or CSharpShellBodyKind.TargetGetterWithSetter
                    or CSharpShellBodyKind.TargetGetterWithInitSetter);
        var accessors = new List<ApiAccessor>();
        if (hasGetter)
        {
            accessors.Add(new ApiAccessor
            {
                Kind = "get",
                ReturnAttributes = spec.ReturnAttributes?.ToList() ?? [],
            });
        }
        if (hasSetter)
            accessors.Add(new ApiAccessor { Kind = setterIsInit ? "init" : "set" });
        if (isAutoGetInit)
            accessors.Add(new ApiAccessor { Kind = "init" });
        return accessors;
    }

    static CSharpPropertyBody PropertyBody(
        CSharpMemberShellSpec spec,
        CSharpAccessorBody body)
        => spec.Kind == CSharpShellMemberKind.PropertyGet
            ? new CSharpPropertyBody(body, null)
            : new CSharpPropertyBody(null, body);

    static CSharpEventBody EventBody(
        CSharpMemberShellSpec spec,
        CSharpAccessorBody body,
        CSharpAccessorBody? siblingBody = null)
        => spec.Kind == CSharpShellMemberKind.EventAdd
            ? new CSharpEventBody(body, siblingBody ?? CSharpAccessorBody.Throw)
            : new CSharpEventBody(siblingBody ?? CSharpAccessorBody.Throw, body);

    static CSharpBlockBody TargetBlockBody(
        string source,
        CSharpConstructorInitializer? constructorInitializer = null)
        => new(source, constructorInitializer) { IsReplacementTarget = true };

    static CSharpAccessorBody TargetAccessorBody(string source)
        => CSharpAccessorBody.Block(source) with { IsReplacementTarget = true };

    static bool RequiresUnsafe(CSharpMemberShellSpec spec)
        => (spec.ReturnType is { } returnType
                && CSharpFormatter.TypeRequiresUnsafeModifier(returnType))
            || spec.Parameters.Any(parameter =>
                CSharpFormatter.TypeRequiresUnsafeModifier(parameter.Type))
            || (!spec.IsAsync
                && spec.Body is { } body
                && CSharpFormatter.RequiresUnsafeModifier(body))
            || (!spec.IsAsync
                && spec.SiblingBody is { } siblingBody
                && CSharpFormatter.RequiresUnsafeModifier(siblingBody))
            || (spec.ConstructorInitializer is { } initializer
                && CSharpFormatter.RequiresUnsafeModifier(initializer))
            || spec.DeclarationSignature?.StartsWith("fixed ", StringComparison.Ordinal) == true;

    static string RequiredBody(CSharpMemberShellSpec spec)
        => spec.Body ?? throw new ArgumentException(
            $"C# shell body shape '{spec.BodyKind}' requires a body.",
            nameof(spec));

    static string RequiredSiblingBody(CSharpMemberShellSpec spec)
        => spec.SiblingBody ?? throw new ArgumentException(
            $"C# shell body shape '{spec.BodyKind}' requires a sibling body.",
            nameof(spec));
}
