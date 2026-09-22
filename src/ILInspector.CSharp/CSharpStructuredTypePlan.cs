using System.Collections.Immutable;
using System.Text;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public enum CSharpStructuredBodyRole
{
    Method,
    Getter,
    Setter,
    Init,
    Adder,
    Remover,
}

public enum CSharpStructuredPartKind
{
    Fixed,
    Attributes,
    Implementation,
}

public enum CSharpStructuredImplementationKind
{
    Body,
    Initializer,
    ImplicitAccessors,
}

public sealed record CSharpStructuredBodyBinding(
    int MethodToken,
    CSharpStructuredBodyRole Role,
    bool HasBodyEvidence = true,
    bool HasManagedBody = true);

public sealed record CSharpStructuredBodyPlacement(
    CSharpStructuredBodyBinding Binding,
    CSharpSourceRange FullRange);

public sealed record CSharpStructuredPart(
    CSharpStructuredPartKind Kind,
    string FullText,
    string SkeletonText,
    CSharpStructuredImplementationKind? ImplementationKind = null,
    ImmutableArray<CSharpStructuredBodyPlacement> Bodies = default);

public sealed record CSharpStructuredDeclarationPlan(
    ApiMember Member,
    ImmutableArray<CSharpStructuredPart> Parts);

public sealed record CSharpStructuredTypePlan(
    ImmutableArray<CSharpStructuredPart> PrefixParts,
    string DeclarationSeparator,
    string Suffix,
    ImmutableArray<CSharpStructuredDeclarationPlan> Declarations,
    ImmutableArray<string> Namespaces);

public sealed record CSharpStructuredMemberRequest(
    ApiMember Member,
    CSharpBodyPolicy BodyPolicy,
    CSharpMemberBody? Body,
    ImmutableArray<CSharpStructuredBodyBinding> Bodies);

public sealed class CSharpStructuredTypeRequest
{
    public CSharpStructuredTypeRequest(
        ApiType type,
        IEnumerable<CSharpStructuredMemberRequest> members,
        IEnumerable<ApiType>? containingTypes = null,
        IEnumerable<string>? namespaces = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(members);
        Type = type;
        Members = [.. members];
        ContainingTypes = containingTypes is null ? [] : [.. containingTypes];
        Namespaces = namespaces is null
            ? []
            : [.. namespaces
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
    }

    public ApiType Type { get; }

    public ImmutableArray<CSharpStructuredMemberRequest> Members { get; }

    public ImmutableArray<ApiType> ContainingTypes { get; }

    public ImmutableArray<string> Namespaces { get; }
}

/// <summary>
/// Produces declaration-local full/skeleton alternatives while CSharp still
/// owns every spelling and source range boundary.
/// </summary>
public static class CSharpStructuredTypePlanProducer
{
    public static CSharpStructuredTypePlan Produce(
        CSharpStructuredTypeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        ApiType declaredType = PrepareTypeForDeclaration(
            request.Type,
            nested: !request.ContainingTypes.IsEmpty,
            [.. request.Members.Select(static member => member.Member)]);
        ImmutableArray<ApiType> containingTypes =
        [
            .. request.ContainingTypes.Select((type, index) =>
                PrepareTypeForDeclaration(
                    type,
                    nested: index > 0,
                    [])),
        ];
        CSharpFormatter formatter = CreateFormatter(
            declaredType,
            omitPropertyAccessors: false);
        CSharpFormatter propertyFormatter = CreateFormatter(
            declaredType,
            omitPropertyAccessors: true);

        int nestingDepth = containingTypes.Length;
        string typePad = "";
        string memberPad = new(' ', (nestingDepth + 1) * 4);
        var prefix = ImmutableArray.CreateBuilder<CSharpStructuredPart>();
        var frame = new StringBuilder();
        foreach (string @namespace in request.Namespaces)
            frame.Append("using ").Append(CSharpFormatter.EscapeNamespace(@namespace)).Append(";\n");
        if (request.Namespaces.Length > 0)
            frame.Append('\n');
        if (!string.IsNullOrWhiteSpace(declaredType.Namespace))
        {
            frame.Append("namespace ")
                .Append(CSharpFormatter.EscapeNamespace(declaredType.Namespace!))
                .Append(";\n\n");
        }

        foreach (ApiType containing in containingTypes)
        {
            prefix.Add(Fixed(frame.ToString()));
            frame.Clear();
            AppendAttributePart(prefix, containing.Attributes, typePad);
            frame.Append(typePad)
                .Append(CreateFormatter(
                    containing,
                    omitPropertyAccessors: false)
                    .FormatTypeDeclaration(containing))
                .Append("\n")
                .Append(typePad)
                .Append("{\n");
            typePad += "    ";
        }

        prefix.Add(Fixed(frame.ToString()));
        frame.Clear();
        AppendAttributePart(prefix, declaredType.Attributes, typePad);
        frame.Append(typePad);
        if (declaredType.Kind == "delegate")
        {
            ApiMember invoke = request.Members.Single().Member;
            frame.Append(formatter.FormatDelegate(declaredType, invoke))
                .Append(';');
            prefix.Add(Fixed(frame.ToString()));
            return new(
                prefix.ToImmutable(),
                "",
                CloseContainingTypes(containingTypes.Length),
                [],
                request.Namespaces);
        }

        frame.Append(formatter.FormatTypeDeclaration(declaredType))
            .Append("\n")
            .Append(typePad)
            .Append('{');
        prefix.Add(Fixed(frame.ToString()));

        ImmutableArray<CSharpStructuredDeclarationPlan> declarations =
            declaredType.Kind == "enum"
                ? [.. request.Members.Select((member, index) =>
                    RenderEnum(member, memberPad))]
                : [.. request.Members.Select(member =>
                    RenderMember(
                        declaredType,
                        member,
                        formatter,
                        propertyFormatter,
                        memberPad))];
        string separator = declaredType.Kind == "enum"
            ? ",\n"
            : "\n\n";
        string suffix = "\n"
            + typePad
            + "}"
            + CloseContainingTypes(containingTypes.Length);
        return new(
            prefix.ToImmutable(),
            separator,
            suffix,
            declarations,
            request.Namespaces);
    }

    static CSharpStructuredDeclarationPlan RenderEnum(
        CSharpStructuredMemberRequest request,
        string pad)
    {
        var parts = ImmutableArray.CreateBuilder<CSharpStructuredPart>();
        AppendAttributePart(parts, request.Member.Attributes, pad);
        string value = request.Body is CSharpFieldInitializer initializer
            ? $" = {initializer.Source}"
            : "";
        parts.Add(Fixed(
            $"{pad}{CSharpFormatter.EscapeIdentifier(request.Member.Name)}{value}"));
        return new(request.Member, parts.ToImmutable());
    }

    static CSharpFormatter CreateFormatter(
        ApiType type,
        bool omitPropertyAccessors)
    {
        CSharpDeclaredTypeSelfNameAdmission.Admitted? selfName = null;
        if (type.DefinitionName is not null
            && type.IntroducedTypeParameterCounts is { Count: > 0 })
        {
            selfName = CSharpDeclaredTypeSelfName.Admit(
                type.DefinitionName,
                type.IntroducedTypeParameterCounts,
                type.TypeParameters) switch
            {
                CSharpDeclaredTypeSelfNameAdmission.Admitted admitted =>
                    admitted,
                CSharpDeclaredTypeSelfNameAdmission.Unrepresentable =>
                    throw new NotSupportedException(
                        $"Type '{type.FullName}' has no exact C# declaration self-name."),
                _ => throw new InvalidOperationException(),
            };
        }

        return new(new CSharpFormatOptions
        {
            IncludeCustomAttributes = false,
            IncludeObsoleteAttribute = false,
            OmitInterfaceMemberModifiers = true,
            OmitPropertyAccessors = omitPropertyAccessors,
            TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
            DeclaredTypeSelfName = selfName,
        });
    }

    static ApiType PrepareTypeForDeclaration(
        ApiType type,
        bool nested,
        IReadOnlyList<ApiMember> members)
    {
        ApiType snapshot = CSharpTypePrinter.SnapshotTypeForRendering(
            type,
            members);
        if (!nested
            || snapshot.DefinitionName is null
            || snapshot.IntroducedTypeParameterCounts is not { Count: > 0 })
        {
            return snapshot;
        }

        int introducedCount =
            snapshot.IntroducedTypeParameterCounts[^1];
        snapshot.Name = snapshot.DefinitionName.Segments[^1];
        snapshot.TypeParameters =
        [
            .. snapshot.TypeParameters.TakeLast(introducedCount),
        ];
        return snapshot;
    }

    static CSharpStructuredDeclarationPlan RenderMember(
        ApiType type,
        CSharpStructuredMemberRequest request,
        CSharpFormatter formatter,
        CSharpFormatter propertyFormatter,
        string pad)
    {
        request = request with { Member = PrepareLogicalMember(type, request.Member) };
        var parts = ImmutableArray.CreateBuilder<CSharpStructuredPart>();
        AppendAttributePart(parts, request.Member.Attributes, pad);
        ApiMember member = request.Member;
        if (member.Kind == "field")
        {
            string declaration = formatter.FormatMember(type, member);
            if (declaration.EndsWith(';'))
                declaration = declaration[..^1];
            parts.Add(Fixed(PadDeclaration(declaration, pad)));
            if (request.Body is CSharpFieldInitializer fieldInitializer)
            {
                parts.Add(Implementation(
                    $" = {fieldInitializer.Source}",
                    "",
                    CSharpStructuredImplementationKind.Initializer,
                    []));
            }
            parts.Add(Fixed(";"));
            return new(member, parts.ToImmutable());
        }

        static ApiMember PrepareLogicalMember(ApiType type, ApiMember member)
        {
            var accessor = member.SignatureModel?.Accessors.FirstOrDefault(value =>
                value.IsExplicitInterfaceImplementation == true
                && !string.IsNullOrWhiteSpace(value.Name));
            if (member.Kind is not ("property" or "event") || accessor?.Name is not { } name)
                return member;

            int separator = name.LastIndexOf('.');
            if (separator < 0)
                throw new NotSupportedException("An explicit accessor has no interface-qualified name.");
            ApiMember snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, [member]).Members[0];
            string leaf = member.SignatureModel!.MemberName ?? member.Name;
            leaf = leaf[(leaf.LastIndexOf('.') + 1)..];
            snapshot.Kind = "explicit-interface-implementation";
            snapshot.Name = name[..separator] + "." + leaf;
            snapshot.SignatureModel!.MemberName = leaf == "this[]"
                ? leaf
                : snapshot.Name;
            return snapshot;
        }

        if (IsProperty(member))
        {
            RenderProperty(
                type,
                request,
                formatter,
                propertyFormatter,
                pad,
                parts);
            return new(member, parts.ToImmutable());
        }

        if (IsEvent(member))
        {
            RenderEvent(type, request, formatter, pad, parts);
            return new(member, parts.ToImmutable());
        }

        string signature = request.Body is null
            ? formatter.FormatMember(type, member)
            : formatter.FormatMemberWithBody(type, member, request.Body);
        if (signature.EndsWith(';'))
            signature = signature[..^1];
        if (request.BodyPolicy is CSharpBodyPolicy.Skeleton or CSharpBodyPolicy.Extern
            || request.Body is null)
        {
            parts.Add(Fixed(PadDeclaration(signature, pad) + ";"));
            AddInvisibleBodyParts(parts, request.Bodies);
            return new(member, parts.ToImmutable());
        }

        var body = (CSharpBlockBody)request.Body;
        string initializer = body.ConstructorInitializer is { } constructorInitializer
            ? " " + CSharpFormatter.FormatConstructorInitializer(constructorInitializer)
            : "";
        parts.Add(Fixed(PadDeclaration(signature + initializer, pad)));
        string full = "\n" + CSharpSourceLayout.RenderBlock(body.Source, pad);
        const string skeletonBody = "throw null;";
        string skeleton = "\n"
            + CSharpSourceLayout.RenderBlock(skeletonBody, pad);
        parts.Add(Implementation(
            full,
            skeleton,
            CSharpStructuredImplementationKind.Body,
            request.Bodies.Select(binding =>
                new CSharpStructuredBodyPlacement(
                    binding,
                    binding.HasBodyEvidence
                        ? new(1, full.Length - 1)
                        : new(0, 0))).ToImmutableArray()));
        return new(member, parts.ToImmutable());
    }

    static void RenderProperty(
        ApiType type,
        CSharpStructuredMemberRequest request,
        CSharpFormatter formatter,
        CSharpFormatter propertyFormatter,
        string pad,
        ImmutableArray<CSharpStructuredPart>.Builder parts)
    {
        ApiMember member = request.Member;
        string declaration = request.Body is null
            ? propertyFormatter.FormatMember(type, member)
            : propertyFormatter.FormatMemberWithBody(type, member, request.Body);
        parts.Add(Fixed(PadDeclaration(declaration, pad) + "\n" + pad + "{"));
        var body = request.Body as CSharpPropertyBody;
        foreach (CSharpStructuredBodyBinding binding in request.Bodies)
        {
            bool getter = binding.Role == CSharpStructuredBodyRole.Getter;
            string keyword = getter
                ? "get"
                : binding.Role == CSharpStructuredBodyRole.Init
                    ? "init"
                    : "set";
            CSharpAccessorBody? accessor = getter
                ? body?.Getter
                : body?.Setter;
            string accessorPad = pad + "    ";
            string head = formatter.FormatAccessorHead(type, member, keyword);
            string full;
            string skeleton;
            CSharpSourceRange range;
            if (accessor is null || accessor.Kind == CSharpAccessorBodyKind.Auto)
            {
                full = "\n" + accessorPad + head + ";";
                skeleton = full;
                range = new(full.Length, 0);
            }
            else
            {
                string source = accessor.Kind == CSharpAccessorBodyKind.Throw
                    ? "throw null;"
                    : accessor.Source!;
                string prefix = "\n" + accessorPad + head + " ";
                if (accessor.Kind == CSharpAccessorBodyKind.Expression)
                {
                    string clause = "=> " + source + ";";
                    full = prefix + clause;
                    range = new(prefix.Length, clause.Length);
                }
                else
                {
                    prefix = "\n" + accessorPad + head + "\n";
                    string block =
                        CSharpSourceLayout.RenderBlock(source, accessorPad);
                    full = prefix + block;
                    range = new(prefix.Length, block.Length);
                }
                bool canUseAuto =
                    member.SignatureModel?.MemberName != "this[]"
                    && member.SignatureModel?.Accessors.Any(
                        static value =>
                            value.IsExplicitInterfaceImplementation == true)
                        != true;
                skeleton = canUseAuto
                    ? "\n" + accessorPad + head + ";"
                    : "\n" + accessorPad + head + "\n"
                        + CSharpSourceLayout.RenderBlock("throw null;", accessorPad);
            }
            if (!binding.HasManagedBody)
            {
                parts.Add(Fixed(full));
                continue;
            }
            parts.Add(Implementation(
                full,
                binding.HasBodyEvidence ? skeleton : full,
                accessor?.Kind == CSharpAccessorBodyKind.Auto
                    ? CSharpStructuredImplementationKind.ImplicitAccessors
                    : CSharpStructuredImplementationKind.Body,
                [new(binding, binding.HasBodyEvidence ? range : new(0, 0))]));
        }
        parts.Add(Fixed("\n" + pad + "}"
            + (body?.Initializer is { } initializer
                ? $" = {initializer};"
                : "")));
    }

    static void RenderEvent(
        ApiType type,
        CSharpStructuredMemberRequest request,
        CSharpFormatter formatter,
        string pad,
        ImmutableArray<CSharpStructuredPart>.Builder parts)
    {
        ApiMember member = request.Member;
        if (request.Body is not CSharpEventBody body)
        {
            string declaration = formatter.FormatMember(type, member);
            parts.Add(Fixed(PadDeclaration(
                declaration.EndsWith(';') ? declaration : declaration + ";",
                pad)));
            AddInvisibleBodyParts(parts, request.Bodies);
            return;
        }

        parts.Add(Fixed(PadDeclaration(
            formatter.FormatMemberWithBody(type, member, body),
            pad) + "\n" + pad + "{"));
        foreach (CSharpStructuredBodyBinding binding in request.Bodies)
        {
            bool adder = binding.Role == CSharpStructuredBodyRole.Adder;
            string keyword = adder ? "add" : "remove";
            CSharpAccessorBody accessor = adder ? body.Adder : body.Remover;
            string accessorPad = pad + "    ";
            string head = formatter.FormatAccessorHead(type, member, keyword);
            string source = accessor.Kind == CSharpAccessorBodyKind.Throw
                ? "throw null;"
                : accessor.Source!;
            string prefix = "\n" + accessorPad + head + " ";
            string full;
            CSharpSourceRange range;
            if (accessor.Kind == CSharpAccessorBodyKind.Expression)
            {
                string clause = "=> " + source + ";";
                full = prefix + clause;
                range = new(prefix.Length, clause.Length);
            }
            else
            {
                prefix = "\n" + accessorPad + head + "\n";
                string block =
                    CSharpSourceLayout.RenderBlock(source, accessorPad);
                full = prefix + block;
                range = new(prefix.Length, block.Length);
            }
            string skeleton = "\n" + accessorPad + head + "\n"
                + CSharpSourceLayout.RenderBlock("throw null;", accessorPad);
            parts.Add(Implementation(
                full,
                binding.HasBodyEvidence ? skeleton : full,
                CSharpStructuredImplementationKind.Body,
                [new(binding, binding.HasBodyEvidence ? range : new(0, 0))]));
        }
        parts.Add(Fixed("\n" + pad + "}"));
    }

    static CSharpStructuredPart Fixed(string text)
        => new(
            CSharpStructuredPartKind.Fixed,
            text,
            text);

    static CSharpStructuredPart Implementation(
        string full,
        string skeleton,
        CSharpStructuredImplementationKind kind,
        ImmutableArray<CSharpStructuredBodyPlacement> bodies)
        => new(
            CSharpStructuredPartKind.Implementation,
            full,
            skeleton,
            kind,
            bodies.IsDefault ? [] : bodies);

    static void AppendAttributePart(
        ImmutableArray<CSharpStructuredPart>.Builder parts,
        IReadOnlyList<string> attributes,
        string pad)
    {
        if (attributes.Count == 0)
            return;
        string text = string.Join(
            "\n",
            attributes.Select(attribute => $"{pad}[{attribute}]"))
            + "\n";
        parts.Add(new(
            CSharpStructuredPartKind.Attributes,
            text,
            text));
    }

    static void AddInvisibleBodyParts(
        ImmutableArray<CSharpStructuredPart>.Builder parts,
        ImmutableArray<CSharpStructuredBodyBinding> bodies)
    {
        foreach (CSharpStructuredBodyBinding body in bodies)
        {
            if (!body.HasManagedBody)
                continue;
            parts.Add(Implementation(
                "",
                "",
                CSharpStructuredImplementationKind.ImplicitAccessors,
                [new(body, new(0, 0))]));
        }
    }

    static string PadDeclaration(string declaration, string pad)
        => declaration.Contains('\n', StringComparison.Ordinal)
            ? string.Join(
                '\n',
                declaration.Split('\n')
                    .Select(line => line.Length == 0 ? line : pad + line))
            : pad + declaration;

    static bool IsProperty(ApiMember member)
        => member.Kind == "property"
            || member.Kind == "explicit-interface-implementation"
                && member.SignatureModel?.Accessors is { Count: > 0 } accessors
                && accessors.All(static accessor =>
                    accessor.Kind is "get" or "set" or "init");

    static bool IsEvent(ApiMember member)
        => member.Kind == "event"
            || member.Kind == "explicit-interface-implementation"
                && member.SignatureModel?.Accessors is { Count: > 0 } accessors
                && accessors.All(static accessor =>
                    accessor.Kind is "add" or "remove");

    static string CloseContainingTypes(int count)
    {
        if (count == 0)
            return "";
        var builder = new StringBuilder();
        for (int depth = count - 1; depth >= 0; depth--)
            builder.Append('\n').Append(new string(' ', depth * 4)).Append('}');
        return builder.ToString();
    }

    static void ValidateRequest(CSharpStructuredTypeRequest request)
    {
        if (request.Members.Any(static member => member is null))
            throw new ArgumentException("Structured members cannot contain null.", nameof(request));
        if (request.ContainingTypes.Any(static type => type is null))
            throw new ArgumentException("Containing types cannot contain null.", nameof(request));
        foreach (var member in request.Members)
        {
            if (member.Member.Kind is "method" or "extension-method" or "field"
                && CSharpIdentifier.AdmitTypeDeclaration(member.Member.Name)
                    is not CSharpTypeDeclarationIdentifierAdmission.Admitted)
            {
                throw new NotSupportedException(
                    $"Declaration '{member.Member.Name}' cannot retain its exact C# identity.");
            }
        }
        if (request.Type.Kind == "delegate" && request.Members.Length != 1)
        {
            throw new ArgumentException(
                "A delegate structured plan requires its single Invoke member.",
                nameof(request));
        }
    }
}
