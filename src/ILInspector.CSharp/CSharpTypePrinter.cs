using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

public sealed class CSharpTypePrinter
{
    public CSharpTypePrintResult Print(
        CSharpTypePrintRequest request,
        CSharpTypePrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PrintBatch([request], options);
    }

    public CSharpTypePrintResult PrintBatch(
        IEnumerable<CSharpTypePrintRequest> requests,
        CSharpTypePrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        options ??= new CSharpTypePrintOptions();
        if (!Enum.IsDefined(options.NamespaceStyle))
            throw new ArgumentOutOfRangeException(nameof(options), options.NamespaceStyle, "Namespace style must be defined.");

        var requestList = requests.ToArray();
        if (requestList.Any(request => request is null))
            throw new ArgumentException("Type print requests cannot contain null entries.", nameof(requests));

        var preparedTypes = new List<PreparedType>();
        var canonicalIdentities = new HashSet<TypeOutputIdentity>();
        var outputIdentities = new HashSet<TypeOutputIdentity>();
        var diagnostics = ImmutableArray.CreateBuilder<CSharpTypePrintDiagnostic>();
        foreach (var request in requestList)
        {
            preparedTypes.Add(PrepareType(
                request,
                containingNamespace: null,
                canonicalParent: null,
                outputParent: null,
                canonicalIdentities,
                outputIdentities,
                nameof(requests)));
        }

        var units = ImmutableArray.CreateBuilder<CSharpTypeSourceUnit>();
        foreach (var group in preparedTypes.GroupBy(type => type.Namespace, StringComparer.Ordinal))
        {
            var containingNamespace = group.Key.Length == 0 ? null : group.Key;
            var source = string.Join(
                "\n\n",
                group.Select(type => RenderType(type, indent: 0, options, diagnostics)));
            if (containingNamespace is not null)
            {
                string renderedNamespace = CSharpDeclarationWriter.EscapeNamespace(containingNamespace);
                source = options.NamespaceStyle switch
                {
                    CSharpNamespaceStyle.FileScoped => $"namespace {renderedNamespace};\n\n{source}",
                    CSharpNamespaceStyle.BlockScoped => $"namespace {renderedNamespace}\n{{\n{Indent(source, 1)}\n}}",
                    _ => throw new InvalidOperationException(
                        $"Unsupported namespace style '{options.NamespaceStyle}'."),
                };
            }

            units.Add(new CSharpTypeSourceUnit(containingNamespace, source));
        }

        return new CSharpTypePrintResult(units.ToImmutable(), diagnostics.ToImmutable());
    }

    static string NormalizeNamespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value;

    static PreparedType PrepareType(
        CSharpTypePrintRequest request,
        string? containingNamespace,
        string? canonicalParent,
        string? outputParent,
        HashSet<TypeOutputIdentity> canonicalIdentities,
        HashSet<TypeOutputIdentity> outputIdentities,
        string parameterName)
    {
        var memberArray = request.Members.ToArray();
        if (memberArray.Any(member => member is null))
        {
            throw new ArgumentException(
                $"Type '{request.Type.FullName}' has a null member entry.",
                parameterName);
        }

        var type = SnapshotTypeForRendering(request.Type, memberArray);
        if (string.IsNullOrWhiteSpace(type.Name))
            throw new ArgumentException("Type print requests require a non-empty type name.");
        var metadataName = string.IsNullOrWhiteSpace(type.MetadataName)
            ? type.Name
            : type.MetadataName;
        bool hasGeneratedMetadataName = IsGeneratedMetadataName(type.Name);
        type.Name = SourceTypeName(type.Name);
        ValidateRequiredShape(type, hasGeneratedMetadataName);
        bool isNested = canonicalParent is not null;
        ValidateTypeKindAndContainment(type, isNested);

        var typeNamespace = NormalizeNamespace(type.Namespace);
        if (containingNamespace is not null && typeNamespace.Length == 0)
            typeNamespace = containingNamespace;
        if (containingNamespace is not null
            && !string.Equals(typeNamespace, containingNamespace, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Nested type '{type.FullName}' must use containing namespace '{containingNamespace}'.",
                parameterName);
        }

        var outputName = DisplayTypeName(type);
        var canonicalPath = canonicalParent is null ? metadataName : $"{canonicalParent}+{metadataName}";
        var outputPath = outputParent is null ? outputName : $"{outputParent}.{outputName}";
        if (!canonicalIdentities.Add(new TypeOutputIdentity(typeNamespace, canonicalPath))
            || !outputIdentities.Add(new TypeOutputIdentity(typeNamespace, outputPath)))
        {
            throw new ArgumentException(
                $"Type print requests contain duplicate C# type '{type.FullName}'.",
                parameterName);
        }

        var overrides = ValidateAndIndexPolicies(request, memberArray, parameterName);
        var members = ImmutableArray.CreateBuilder<PreparedMember>(memberArray.Length);
        for (int i = 0; i < memberArray.Length; i++)
        {
            var original = memberArray[i];
            var snapshot = type.Members[i];
            var policy = overrides.TryGetValue(original, out var memberPolicy)
                ? memberPolicy
                : new CSharpMemberPolicy(original, request.BodyPolicy);
            ValidateResolvedBodyPolicy(type, snapshot, policy, parameterName);
            members.Add(new PreparedMember(snapshot, policy.BodyPolicy, policy.Body));
        }

        var primaryConstructorParameters = request.PrimaryConstructorParameters
            .Select(SnapshotParameter)
            .ToImmutableArray();
        var nestedTypes = request.NestedTypes
            .Select(nested => PrepareType(
                nested,
                typeNamespace,
                canonicalPath,
                outputPath,
                canonicalIdentities,
                outputIdentities,
                parameterName))
            .ToImmutableArray();
        return new PreparedType(
            typeNamespace,
            type,
            members.ToImmutable(),
            primaryConstructorParameters,
            nestedTypes);
    }

    static Dictionary<ApiMember, CSharpMemberPolicy> ValidateAndIndexPolicies(
        CSharpTypePrintRequest request,
        IReadOnlyList<ApiMember> members,
        string parameterName)
    {
        var selectedMembers = new HashSet<ApiMember>(members, ReferenceEqualityComparer.Instance);
        var overrides = new Dictionary<ApiMember, CSharpMemberPolicy>(ReferenceEqualityComparer.Instance);
        foreach (var policy in request.MemberPolicyOverrides)
        {
            if (!selectedMembers.Contains(policy.Member))
            {
                throw new ArgumentException(
                    $"Member policy override '{policy.Member.Name}' is not in the selected member set.",
                    parameterName);
            }
            if (!overrides.TryAdd(policy.Member, policy))
            {
                throw new ArgumentException(
                    $"Member '{policy.Member.Name}' has multiple policy overrides.",
                    parameterName);
            }
        }

        return overrides;
    }

    static string RenderType(
        PreparedType prepared,
        int indent,
        CSharpTypePrintOptions options,
        ImmutableArray<CSharpTypePrintDiagnostic>.Builder diagnostics)
    {
        if (prepared.Type.Kind == "delegate")
            return RenderDelegate(prepared, indent);

        var declarationOptions = DeclarationOptions(prepared.Namespace, options);
        var diagnosticPass = CSharpDeclarationWriter.RenderTypeUnit(
            prepared.Type,
            prepared.Type.Members,
            declarationOptions with { TerminateMemberDeclaration = true });
        diagnostics.AddRange(diagnosticPass.Diagnostics.Select(
            diagnostic => new CSharpTypePrintDiagnostic(prepared.Type.FullName, diagnostic)));

        string pad = new(' ', indent * 4);
        string declaration = CSharpDeclarationWriter.RenderTypeDeclaration(prepared.Type, declarationOptions);
        if (prepared.PrimaryConstructorParameters.Length > 0)
            declaration = AddPrimaryConstructorParameters(declaration, prepared.PrimaryConstructorParameters);

        var lines = new List<string>
        {
            $"{pad}{declaration}",
            $"{pad}{{"
        };
        if (prepared.Type.Kind == "enum")
        {
            lines.AddRange(prepared.Members.Select((member, index) =>
                RenderEnumMember(member, indent + 1, index < prepared.Members.Length - 1)));
        }
        else
        {
            foreach (var member in prepared.Members)
                lines.AddRange(RenderMember(prepared, member, indent + 1, declarationOptions));
            foreach (var nested in prepared.NestedTypes)
                lines.Add(RenderType(nested, indent + 1, options, diagnostics));
        }
        lines.Add($"{pad}}}");
        return string.Join('\n', lines);
    }

    static IEnumerable<string> RenderMember(
        PreparedType type,
        PreparedMember member,
        int indent,
        CSharpDeclarationOptions declarationOptions)
    {
        string pad = new(' ', indent * 4);
        if (member.Member.Kind == "field")
        {
            string declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
                type.Type,
                member.Member,
                declarationOptions);
            if (member.Body is CSharpFieldInitializer fieldInitializer)
                return [$"{pad}{declaration} = {fieldInitializer.Source};"];
            return [$"{pad}{EnsureTerminated(declaration)}"];
        }

        if (member.Member.Kind == "property")
            return RenderProperty(type, member, indent, declarationOptions);

        string memberDeclaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type.Type,
            member.Member,
            declarationOptions);
        if (type.Type.Kind == "interface"
            || member.Member.IsAbstract
            || member.Policy == CSharpBodyPolicy.Skeleton)
        {
            return [$"{pad}{EnsureTerminated(memberDeclaration)}"];
        }
        if (member.Member.Kind == "event")
            return [$"{pad}{EnsureTerminated(memberDeclaration)}"];

        string initializer = member.Member.Kind == "constructor"
            && member.Policy == CSharpBodyPolicy.Stub
            && type.PrimaryConstructorParameters.Length > 0
                ? $" : this({string.Join(", ", Enumerable.Repeat("default", type.PrimaryConstructorParameters.Length))})"
                : "";
        if (member.Body is null && member.Policy == CSharpBodyPolicy.Stub)
            return [$"{pad}{memberDeclaration}{initializer} {{ throw null; }}"];

        var body = member.Body switch
        {
            CSharpBlockBody block => block.Source,
            _ => throw new InvalidOperationException(
                $"Member '{member.Member.Name}' has no renderable block body."),
        };
        return RenderBlock(memberDeclaration + initializer, body, indent);
    }

    static IEnumerable<string> RenderProperty(
        PreparedType type,
        PreparedMember member,
        int indent,
        CSharpDeclarationOptions declarationOptions)
    {
        string pad = new(' ', indent * 4);
        if (member.Policy == CSharpBodyPolicy.Skeleton)
        {
            string skeleton = CSharpDeclarationWriter.RenderMemberDeclaration(
                type.Type,
                member.Member,
                declarationOptions);
            return [$"{pad}{EnsureTerminated(skeleton)}"];
        }

        var body = member.Body as CSharpPropertyBody;
        if (body is null && member.Policy == CSharpBodyPolicy.Stub)
        {
            var accessors = member.Member.SignatureModel?.Accessors
                ?? throw new InvalidOperationException(
                    $"Stub property '{member.Member.Name}' requires structured accessors.");
            body = new CSharpPropertyBody(
                accessors.Any(accessor => accessor.Kind == "get") ? CSharpAccessorBody.Throw : null,
                accessors.Any(accessor => accessor.Kind == "set") ? CSharpAccessorBody.Throw : null);
        }
        if (body is null)
        {
            throw new InvalidOperationException(
                $"Property '{member.Member.Name}' requires an accessor body shape.");
        }
        string declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type.Type,
            member.Member,
            declarationOptions with { OmitPropertyAccessors = true });
        if (AllAuto(body))
        {
            var accessors = new List<string>();
            if (body.Getter is not null)
                accessors.Add(AccessorHead(member.Member, "get") + ";");
            if (body.Setter is not null)
                accessors.Add(AccessorHead(member.Member, "set") + ";");
            return [$"{pad}{declaration} {{ {string.Join(" ", accessors)} }}"];
        }

        var lines = new List<string>
        {
            $"{pad}{declaration}",
            $"{pad}{{"
        };
        AddAccessor(lines, member.Member, "get", body.Getter, indent + 1);
        AddAccessor(lines, member.Member, "set", body.Setter, indent + 1);
        lines.Add($"{pad}}}");
        return lines;
    }

    static void AddAccessor(
        List<string> lines,
        ApiMember member,
        string kind,
        CSharpAccessorBody? body,
        int indent)
    {
        if (body is null)
            return;
        string pad = new(' ', indent * 4);
        string head = AccessorHead(member, kind);
        if (body.Kind == CSharpAccessorBodyKind.Auto)
        {
            lines.Add($"{pad}{head};");
            return;
        }

        lines.Add($"{pad}{head}");
        lines.Add($"{pad}{{");
        string source = body.Kind == CSharpAccessorBodyKind.Throw
            ? "throw null;"
            : body.Source!;
        foreach (var line in SourceLines(source))
            lines.Add($"{pad}    {line}");
        lines.Add($"{pad}}}");
    }

    static string AccessorHead(ApiMember member, string kind)
    {
        var accessor = member.SignatureModel?.Accessors
            .FirstOrDefault(candidate => candidate.Kind == kind);
        var parts = new List<string>();
        if (accessor?.ReturnAttributes is { Count: > 0 } returnAttributes)
            parts.Add($"[return: {string.Join(", ", returnAttributes)}]");
        if (!string.IsNullOrWhiteSpace(accessor?.Accessibility))
            parts.Add(accessor.Accessibility!);
        parts.Add(kind);
        return string.Join(" ", parts);
    }

    static string RenderDelegate(PreparedType prepared, int indent)
    {
        string pad = new(' ', indent * 4);
        var invoke = prepared.Members.Single(member => member.Member.Name == "Invoke").Member;
        var signature = invoke.SignatureModel
            ?? throw new InvalidOperationException(
                $"Delegate '{prepared.Type.FullName}' requires a structured Invoke signature.");
        string attributes = prepared.Type.Attributes.Count == 0
            ? ""
            : $"[{string.Join(", ", prepared.Type.Attributes)}] ";
        string unsafeText = invoke.IsUnsafe ? " unsafe" : "";
        string typeName = DisplayTypeName(prepared.Type);
        string parameters = string.Join(", ", signature.Parameters.Select(parameter => parameter.Declaration));
        string declaration = $"{attributes}public{unsafeText} delegate {signature.ReturnType ?? "void"} {typeName}({parameters})";
        foreach (var typeParameter in prepared.Type.TypeParameters)
        {
            if (typeParameter.ConstraintsSummary is { } constraints)
                declaration += $" where {CSharpDeclarationWriter.EscapeIdentifier(typeParameter.Name)} : {constraints}";
        }
        return $"{pad}{declaration};";
    }

    static string RenderEnumMember(PreparedMember member, int indent, bool trailingComma)
    {
        string pad = new(' ', indent * 4);
        string initializer = member.Body is CSharpFieldInitializer value
            ? $" = {value.Source}"
            : "";
        return $"{pad}{CSharpDeclarationWriter.EscapeIdentifier(member.Member.Name)}{initializer}{(trailingComma ? "," : "")}";
    }

    static CSharpDeclarationOptions DeclarationOptions(
        string containingNamespace,
        CSharpTypePrintOptions options)
        => new()
        {
            TypeNameMode = CSharpTypeNameMode.ContextualShort,
            ContainingNamespace = containingNamespace.Length == 0 ? null : containingNamespace,
            NamespaceMode = CSharpNamespaceMode.Omit,
            IncludeCustomAttributes = options.IncludeCustomAttributes
        };

    static IEnumerable<string> RenderBlock(string declaration, string source, int indent)
    {
        string pad = new(' ', indent * 4);
        yield return $"{pad}{declaration}";
        yield return $"{pad}{{";
        foreach (var line in SourceLines(source))
            yield return $"{pad}    {line}";
        yield return $"{pad}}}";
    }

    static IEnumerable<string> SourceLines(string source)
        => source.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0);

    static bool AllAuto(CSharpPropertyBody body)
        => (body.Getter is null || body.Getter.Kind == CSharpAccessorBodyKind.Auto)
            && (body.Setter is null || body.Setter.Kind == CSharpAccessorBodyKind.Auto);

    static string EnsureTerminated(string declaration)
        => declaration.EndsWith(';') || declaration.EndsWith('}')
            ? declaration
            : declaration + ";";

    static string AddPrimaryConstructorParameters(
        string declaration,
        IReadOnlyList<ApiParameter> parameters)
    {
        string parameterList = string.Join(", ", parameters.Select(parameter => parameter.Declaration));
        int constraints = declaration.IndexOf(" where ", StringComparison.Ordinal);
        string head = constraints >= 0 ? declaration[..constraints] : declaration;
        string tail = constraints >= 0 ? declaration[constraints..] : "";
        int inheritance = head.IndexOf(" : ", StringComparison.Ordinal);
        return inheritance >= 0
            ? head[..inheritance] + $"({parameterList})" + head[inheritance..] + tail
            : $"{head}({parameterList}){tail}";
    }

    static string DisplayTypeName(ApiType type)
    {
        int tick = type.Name.IndexOf('`');
        string name = tick >= 0 ? type.Name[..tick] : type.Name;
        name = CSharpDeclarationWriter.EscapeIdentifier(name);
        return type.TypeParameters.Count == 0
            ? name
            : $"{name}<{string.Join(", ", type.TypeParameters.Select(parameter => CSharpDeclarationWriter.EscapeIdentifier(parameter.Name)))}>";
    }

    static string SourceTypeName(string metadataName)
    {
        if (!metadataName.StartsWith('<') || !metadataName.Contains('>', StringComparison.Ordinal))
            return metadataName;

        int arity = metadataName.IndexOf('`');
        string sourceName = arity < 0 ? metadataName : metadataName[..arity];
        var builder = new System.Text.StringBuilder(sourceName.Length + 1);
        if (sourceName.Length == 0 || !(char.IsLetter(sourceName[0]) || sourceName[0] == '_'))
            builder.Append('_');
        foreach (char character in sourceName)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        return builder.ToString();
    }

    static bool IsGeneratedMetadataName(string name)
        => name.StartsWith('<') && name.Contains('>', StringComparison.Ordinal);

    static string Indent(string source, int depth)
    {
        string pad = new(' ', depth * 4);
        return string.Join('\n', source.Split('\n').Select(line => line.Length == 0 ? line : pad + line));
    }

    internal static ApiType SnapshotTypeForRendering(
        ApiType type,
        IReadOnlyList<ApiMember> members)
    {
        var attributes = type.Attributes;
        var interfaces = type.Interfaces;
        var typeParameters = type.TypeParameters;
        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            MetadataName = type.MetadataName,
            Kind = type.Kind,
            Attributes = attributes?.ToList()!,
            EnumUnderlyingType = type.EnumUnderlyingType,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            IsByRefLike = type.IsByRefLike,
            IsReadOnly = type.IsReadOnly,
            BaseType = type.BaseType,
            Interfaces = interfaces?.ToList()!,
            TypeParameters = typeParameters?.Select(SnapshotTypeParameter).ToList()!,
            Members = members.Select(SnapshotMember).ToList()
        };
    }

    static ApiMember SnapshotMember(ApiMember member)
    {
        var attributes = member.Attributes;
        var signatureModel = member.SignatureModel;
        return new ApiMember
        {
            Name = member.Name,
            Kind = member.Kind,
            Attributes = attributes?.ToList()!,
            ReturnType = member.ReturnType,
            Signature = member.Signature,
            SignatureModel = signatureModel is null ? null : SnapshotSignature(signatureModel),
            IsStatic = member.IsStatic,
            IsVirtual = member.IsVirtual,
            IsAbstract = member.IsAbstract,
            IsOverride = member.IsOverride,
            IsSealed = member.IsSealed,
            IsReadOnly = member.IsReadOnly,
            IsConst = member.IsConst,
            IsUnsafe = member.IsUnsafe,
            IsAsync = member.IsAsync,
            Accessibility = member.Accessibility,
            IsExtension = member.IsExtension,
            IsObsolete = member.IsObsolete,
            ObsoleteMessage = member.ObsoleteMessage
        };
    }

    static ApiSignature SnapshotSignature(ApiSignature signature)
    {
        var returnAttributes = signature.ReturnAttributes;
        var typeParameters = signature.TypeParameters;
        var parameters = signature.Parameters;
        var accessors = signature.Accessors;
        return new ApiSignature
        {
            ReturnType = signature.ReturnType,
            ReturnAttributes = returnAttributes?.ToList()!,
            MemberName = signature.MemberName,
            IsRequired = signature.IsRequired,
            TypeParameters = typeParameters?.Select(SnapshotTypeParameter).ToList()!,
            Parameters = parameters?.Select(SnapshotParameter).ToList()!,
            Accessors = accessors?.Select(SnapshotAccessor).ToList()!
        };
    }

    static TypeParameter SnapshotTypeParameter(TypeParameter parameter)
    {
        var constraints = parameter.Constraints;
        return new TypeParameter
        {
            Name = parameter.Name,
            Variance = parameter.Variance,
            Constraints = constraints?.ToList()!
        };
    }

    static ApiParameter SnapshotParameter(ApiParameter parameter)
    {
        var attributes = parameter.Attributes;
        return new ApiParameter
        {
            Attributes = attributes?.ToList()!,
            Name = parameter.Name,
            Type = parameter.Type,
            Modifier = parameter.Modifier,
            HasDefault = parameter.HasDefault,
            DefaultValueText = parameter.DefaultValueText
        };
    }

    static ApiAccessor SnapshotAccessor(ApiAccessor accessor)
    {
        var returnAttributes = accessor.ReturnAttributes;
        return new ApiAccessor
        {
            Kind = accessor.Kind,
            Accessibility = accessor.Accessibility,
            ReturnAttributes = returnAttributes?.ToList()!
        };
    }

    static void ValidateRequiredShape(ApiType type, bool allowMissingMetadataArity)
    {
        if (string.IsNullOrWhiteSpace(type.Name))
            throw new ArgumentException("Type print requests require a non-empty type name.");
        if (type.TypeParameters is null)
            throw new ArgumentException($"Type '{type.FullName}' has a null type-parameter collection.");
        if (type.Name.Contains('<', StringComparison.Ordinal)
            || type.Name.Contains('>', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' must use a metadata name rather than C# type-argument spelling.");
        }

        var tick = type.Name.LastIndexOf('`');
        if (tick < 0)
        {
            if (type.TypeParameters.Count > 0 && !allowMissingMetadataArity)
            {
                throw new ArgumentException(
                    $"Generic type '{type.FullName}' requires metadata arity in its name.");
            }

            return;
        }

        if (!int.TryParse(type.Name.AsSpan(tick + 1), out var arity)
            || arity <= 0
            || arity != type.TypeParameters.Count)
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' has inconsistent metadata arity and type parameters.");
        }
    }

    static void ValidateTypeKindAndContainment(ApiType type, bool isNested)
    {
        if ((!isNested && type.MetadataName?.Contains('+', StringComparison.Ordinal) == true)
            || type.Name.Contains('.', StringComparison.Ordinal)
            || type.Name.Contains('+', StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"C# skeleton printing for nested type '{type.FullName}' requires its declaring type.");
        }

        if (type.Kind is not ("class" or "struct" or "interface" or "record" or "enum" or "delegate"))
        {
            throw new NotSupportedException(
                $"C# type printing does not support type kind '{type.Kind}' for '{type.FullName}'.");
        }
    }

    static void ValidateResolvedBodyPolicy(
        ApiType type,
        ApiMember member,
        CSharpMemberPolicy policy,
        string parameterName)
    {
        if (policy.BodyPolicy == CSharpBodyPolicy.Skeleton && policy.Body is not null)
        {
            throw new ArgumentException(
                $"Skeleton member '{member.Name}' cannot carry an implementation body.",
                parameterName);
        }
        if (policy.BodyPolicy == CSharpBodyPolicy.Full && policy.Body is null)
        {
            throw new NotSupportedException(
                $"C# member body policy '{policy.BodyPolicy}' for '{member.Name}' requires a body provider.");
        }

        bool validBody = (member.Kind, policy.Body) switch
        {
            (_, null) => policy.BodyPolicy != CSharpBodyPolicy.Full,
            ("field", CSharpFieldInitializer) => true,
            ("property", CSharpPropertyBody) => true,
            ("method" or "extension-method" or "explicit-interface-implementation" or "constructor", CSharpBlockBody) => true,
            _ => false,
        };
        if (!validBody)
        {
            throw new ArgumentException(
                $"Member body shape '{policy.Body?.GetType().Name}' is not valid for {member.Kind} '{member.Name}'.",
                parameterName);
        }
        if (type.Kind == "interface"
            && policy.BodyPolicy != CSharpBodyPolicy.Skeleton)
        {
            throw new ArgumentException(
                $"Interface member '{member.Name}' must use skeleton body policy.",
                parameterName);
        }
    }

    sealed record PreparedType(
        string Namespace,
        ApiType Type,
        ImmutableArray<PreparedMember> Members,
        ImmutableArray<ApiParameter> PrimaryConstructorParameters,
        ImmutableArray<PreparedType> NestedTypes);

    readonly record struct PreparedMember(
        ApiMember Member,
        CSharpBodyPolicy Policy,
        CSharpMemberBody? Body);

    readonly record struct TypeOutputIdentity(string Namespace, string Name);
}
