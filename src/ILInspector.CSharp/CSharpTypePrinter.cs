using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.Text;

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
        if (!Enum.IsDefined(options.TypeNamePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), options.TypeNamePolicy, "C# type-name policy must be defined.");
        var configuredUsings = options.Usings?.ToArray()
            ?? throw new ArgumentException("C# type printer usings cannot be null.", nameof(options));

        var requestList = requests.ToArray();
        if (requestList.Any(request => request is null))
            throw new ArgumentException("Type print requests cannot contain null entries.", nameof(requests));

        // A single request produces at most one namespace, so a file-scoped
        // namespace declaration is always legal and reads cleaner. Multiple
        // requests may span namespaces, which only block-scoped namespaces can
        // represent in one file.
        bool useFileScopedNamespace = requestList.Length == 1;

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

        var safeUsings = ComputeSafeUsings(preparedTypes, options);
        var derivedUsings = options.TypeNamePolicy == CSharpTypeNamePolicy.ShortWithUsings
            ? safeUsings
            : [];
        var contextualUsings = TypeNameContext(options, configuredUsings, safeUsings);
        var emittedUsings = options.IncludeUsings
            ? configuredUsings
                .Concat(derivedUsings)
                .ToImmutableHashSet(StringComparer.Ordinal)
            : ImmutableHashSet.Create<string>(StringComparer.Ordinal);

        var units = ImmutableArray.CreateBuilder<CSharpTypeSourceUnit>();
        foreach (var group in preparedTypes.GroupBy(type => type.Namespace, StringComparer.Ordinal))
        {
            var containingNamespace = group.Key.Length == 0 ? null : group.Key;
            var source = string.Join(
                "\n\n",
                group.Select(type => RenderType(
                    type,
                    indent: 0,
                    options,
                    contextualUsings,
                    inheritedShadowingNames: ImmutableHashSet<string>.Empty,
                    diagnostics)));
            if (containingNamespace is not null)
            {
                string renderedNamespace = CSharpFormatter.EscapeNamespace(containingNamespace);
                source = useFileScopedNamespace
                    ? $"namespace {renderedNamespace};\n\n{source}"
                    : $"namespace {renderedNamespace}\n{{\n{Indent(source, 1)}\n}}";
            }

            units.Add(new CSharpTypeSourceUnit(containingNamespace, source));
        }

        var unitList = units.ToImmutable();
        return new CSharpTypePrintResult(
            unitList,
            diagnostics.ToImmutable(),
            emittedUsings,
            () => ComposeSource(unitList, emittedUsings, options));
    }

    /// <summary>
    /// Derives the collision-safe namespaces to shorten against, excluding the
    /// unit's own declaring namespaces (their references are already shortened by
    /// the same-namespace rule, so importing them would be redundant).
    /// </summary>
    static IReadOnlyList<string> ComputeSafeUsings(
        IReadOnlyList<PreparedType> preparedTypes,
        CSharpTypePrintOptions options)
    {
        // Shortening is only sound when the enabling `using` directives are
        // actually emitted. When usings are suppressed, keep references qualified
        // so the composed source stays compilable.
        if (options.TypeNamePolicy == CSharpTypeNamePolicy.Qualified
            || !options.IncludeUsings)
            return [];

        var scopes = new List<(
            ApiType Type,
            IEnumerable<ApiMember> Members,
            IEnumerable<ApiParameter> AdditionalParameters)>();
        var declaringNamespaces = new HashSet<string>(StringComparer.Ordinal);
        void Flatten(PreparedType prepared)
        {
            scopes.Add((
                prepared.Type,
                prepared.Members.Select(member => member.Member),
                prepared.PrimaryConstructorParameters));
            declaringNamespaces.Add(prepared.Namespace);
            foreach (var nested in prepared.NestedTypes)
                Flatten(nested);
        }
        foreach (var prepared in preparedTypes)
            Flatten(prepared);

        return CSharpDeclarationWriter.DeriveContextualUsings(scopes)
            .Where(ns => !declaringNamespaces.Contains(ns))
            .ToArray();
    }

    static IReadOnlyList<string> TypeNameContext(
        CSharpTypePrintOptions options,
        IReadOnlyList<string> configuredUsings,
        IReadOnlyList<string> safeUsings)
        => options.TypeNamePolicy switch
        {
            CSharpTypeNamePolicy.Qualified => [],
            CSharpTypeNamePolicy.ShortWithUsings =>
                options.IncludeUsings
                    ? safeUsings
                    : [],
            CSharpTypeNamePolicy.ContextualShort =>
                options.IncludeUsings
                    ? configuredUsings
                        .Where(safeUsings.Contains)
                        .ToArray()
                    : [],
            _ => throw new InvalidOperationException()
        };

    static string ComposeSource(
        ImmutableArray<CSharpTypeSourceUnit> units,
        ImmutableHashSet<string> usings,
        CSharpTypePrintOptions options)
    {
        var sb = new System.Text.StringBuilder();
        if (options.EmitPragmaWarningDisable)
            sb.AppendLf("#pragma warning disable");
        foreach (var attribute in options.AssemblyAttributes)
            sb.AppendLf($"[assembly: {attribute}]");
        foreach (var attribute in options.ModuleAttributes)
            sb.AppendLf($"[module: {attribute}]");
        foreach (var ns in usings.Select(CSharpFormatter.EscapeNamespace).Order(StringComparer.Ordinal))
            sb.AppendLf($"using {ns};");
        foreach (var unit in units)
            sb.AppendLf(unit.Source);

        return sb.ToString();
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
        bool hasGeneratedMetadataName = CSharpFormatter.IsGeneratedMetadataName(type.Name);
        type.Name = CSharpFormatter.NormalizeGeneratedMetadataTypeName(type.Name);
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

        var outputName = CSharpFormatter.FormatTypeName(type);
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
            ValidateResolvedBodyPolicy(
                type,
                snapshot,
                policy,
                request.PrimaryConstructorParameters.Count,
                parameterName);
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
        IReadOnlyList<string> contextualUsings,
        IReadOnlySet<string> inheritedShadowingNames,
        ImmutableArray<CSharpTypePrintDiagnostic>.Builder diagnostics)
    {
        var inScopeShadowingNames = inheritedShadowingNames.ToHashSet(StringComparer.Ordinal);
        inScopeShadowingNames.UnionWith(prepared.Type.TypeParameters.Select(
            parameter => parameter.Name));
        inScopeShadowingNames.UnionWith(prepared.NestedTypes.Select(
            nested => CSharpFormatter.StripArity(nested.Type.Name)));
        var formatter = DeclarationFormatter(
            prepared.Namespace,
            options,
            contextualUsings,
            inScopeShadowingNames);
        if (prepared.Type.Kind == "delegate")
            return RenderDelegate(prepared, formatter, indent);

        var propertyFormatter = DeclarationFormatter(
            prepared.Namespace,
            options,
            contextualUsings,
            inScopeShadowingNames,
            omitPropertyAccessors: true);
        var diagnosticPass = DeclarationFormatter(
            prepared.Namespace,
            options,
            contextualUsings,
            inScopeShadowingNames,
            terminateMemberDeclaration: true)
            .FormatTypeUnit(
                prepared.Type,
                prepared.Members.Select(member => member.Member));
        diagnostics.AddRange(diagnosticPass.Diagnostics.Select(
            diagnostic => new CSharpTypePrintDiagnostic(prepared.Type.FullName, diagnostic)));

        string pad = new(' ', indent * 4);
        string declaration = formatter.FormatTypeDeclaration(
            prepared.Type,
            prepared.PrimaryConstructorParameters);

        var lines = new List<string>
        {
            PadDeclaration(declaration, pad),
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
                lines.AddRange(RenderMember(prepared, member, formatter, propertyFormatter, indent + 1));
            foreach (var nested in prepared.NestedTypes)
            {
                lines.Add(RenderType(
                    nested,
                    indent + 1,
                    options,
                    contextualUsings,
                    inScopeShadowingNames,
                    diagnostics));
            }
        }
        lines.Add($"{pad}}}");
        return string.Join('\n', lines);
    }

    static IEnumerable<string> RenderMember(
        PreparedType type,
        PreparedMember member,
        CSharpFormatter formatter,
        CSharpFormatter propertyFormatter,
        int indent)
    {
        string pad = new(' ', indent * 4);
        if (member.Member.Kind == "field")
        {
            string declaration = formatter.FormatMember(type.Type, member.Member);
            if (member.Body is CSharpFieldInitializer fieldInitializer)
                return [$"{PadDeclaration(declaration, pad)} = {fieldInitializer.Source};"];
            return [PadDeclaration(EnsureTerminated(declaration), pad)];
        }

        if (IsProperty(member.Member))
            return RenderProperty(type, member, formatter, propertyFormatter, indent);

        if (IsEvent(member.Member))
            return RenderEvent(type, member, formatter, indent);

        string memberDeclaration = member.Body is null
            ? formatter.FormatMember(type.Type, member.Member)
            : formatter.FormatMemberWithBody(type.Type, member.Member, member.Body);
        if (type.Type.Kind == "interface"
            || member.Member.IsAbstract
            || member.Policy == CSharpBodyPolicy.Skeleton)
        {
            return [PadDeclaration(EnsureTerminated(memberDeclaration), pad)];
        }
        string initializer = member.Body is CSharpBlockBody { ConstructorInitializer: { } constructorInitializer }
            ? " " + CSharpFormatter.FormatConstructorInitializer(constructorInitializer)
            : "";
        if (member.Body is null && member.Policy == CSharpBodyPolicy.Stub)
            return [$"{PadDeclaration(memberDeclaration, pad)}{initializer} {{ throw null; }}"];

        var body = member.Body switch
        {
            CSharpBlockBody block => block.Source,
            _ => throw new InvalidOperationException(
                $"Member '{member.Member.Name}' has no renderable block body."),
        };
        if (member.Policy == CSharpBodyPolicy.Stub && body == "throw null;")
            return [$"{PadDeclaration(memberDeclaration, pad)}{initializer} {{ throw null; }}"];
        return RenderBlock(memberDeclaration + initializer, body, indent);
    }

    static IEnumerable<string> RenderProperty(
        PreparedType type,
        PreparedMember member,
        CSharpFormatter formatter,
        CSharpFormatter propertyFormatter,
        int indent)
    {
        string pad = new(' ', indent * 4);
        if (member.Policy == CSharpBodyPolicy.Skeleton)
        {
            string skeleton = formatter.FormatMember(type.Type, member.Member);
            return [PadDeclaration(EnsureTerminated(skeleton), pad)];
        }

        var body = (CSharpPropertyBody)member.Body!;
        string declaration = propertyFormatter.FormatMemberWithBody(
            type.Type,
            member.Member,
            member.Body!);
        if (AllAuto(body))
        {
            var accessors = new List<string>();
            if (body.Getter is not null)
                accessors.Add(AccessorHead(member.Member, "get") + ";");
            if (body.Setter is not null)
                accessors.Add(AccessorHead(member.Member, SetterKeyword(member.Member)) + ";");
            return [$"{PadDeclaration(declaration, pad)} {{ {string.Join(" ", accessors)} }}"];
        }

        var lines = new List<string>
        {
            PadDeclaration(declaration, pad),
            $"{pad}{{"
        };
        AddAccessor(lines, member.Member, "get", body.Getter, indent + 1);
        AddAccessor(lines, member.Member, SetterKeyword(member.Member), body.Setter, indent + 1);
        lines.Add($"{pad}}}");
        return lines;
    }

    // An init-only property's write accessor is spelled `init`, not `set`. Honor the
    // accessor model so full-body rendering does not silently downgrade `init` to a
    // public `set` (dropping the required modreq(IsExternalInit)).
    static string SetterKeyword(ApiMember member)
        => member.SignatureModel?.Accessors is { } accessors
            && accessors.Any(accessor => accessor.Kind == "init")
            ? "init"
            : "set";

    static IEnumerable<string> RenderEvent(
        PreparedType type,
        PreparedMember member,
        CSharpFormatter formatter,
        int indent)
    {
        string pad = new(' ', indent * 4);
        if (member.Policy == CSharpBodyPolicy.Skeleton)
        {
            string skeleton = formatter.FormatMember(type.Type, member.Member);
            return [PadDeclaration(EnsureTerminated(skeleton), pad)];
        }

        var body = (CSharpEventBody)member.Body!;
        string declaration = formatter.FormatMemberWithBody(
            type.Type,
            member.Member,
            body);
        var lines = new List<string>
        {
            PadDeclaration(declaration, pad),
            $"{pad}{{"
        };
        AddAccessor(lines, member.Member, "add", body.Adder, indent + 1);
        AddAccessor(lines, member.Member, "remove", body.Remover, indent + 1);
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

    static string RenderDelegate(PreparedType prepared, CSharpFormatter formatter, int indent)
    {
        string pad = new(' ', indent * 4);
        var invoke = prepared.Members.Single(member => member.Member.Name == "Invoke").Member;
        return PadDeclaration(formatter.FormatDelegate(prepared.Type, invoke), pad);
    }

    static string RenderEnumMember(PreparedMember member, int indent, bool trailingComma)
    {
        string pad = new(' ', indent * 4);
        string initializer = member.Body is CSharpFieldInitializer value
            ? $" = {value.Source}"
            : "";
        return $"{pad}{CSharpFormatter.EscapeIdentifier(member.Member.Name)}{initializer}{(trailingComma ? "," : "")}";
    }

    static CSharpFormatter DeclarationFormatter(
        string containingNamespace,
        CSharpTypePrintOptions options,
        IReadOnlyList<string> contextualUsings,
        IReadOnlyCollection<string> additionalShadowingNames,
        bool omitPropertyAccessors = false,
        bool terminateMemberDeclaration = false)
        => new(new CSharpFormatOptions
        {
            // ShortWithUsings is planned once across the complete output unit;
            // each declaration then shortens only against that shared safe set.
            TypeNamePolicy = options.TypeNamePolicy == CSharpTypeNamePolicy.Qualified
                ? CSharpTypeNamePolicy.Qualified
                : CSharpTypeNamePolicy.ContextualShort,
            ContainingNamespace = containingNamespace.Length == 0 ? null : containingNamespace,
            Usings = contextualUsings,
            AdditionalShadowingNames = additionalShadowingNames,
            NamespacePolicy = CSharpNamespacePolicy.Omit,
            IncludeCustomAttributes = options.IncludeCustomAttributes,
            OmitPropertyAccessors = omitPropertyAccessors,
            TerminateMemberDeclaration = terminateMemberDeclaration
        });

    static IEnumerable<string> RenderBlock(string declaration, string source, int indent)
    {
        string pad = new(' ', indent * 4);
        yield return PadDeclaration(declaration, pad);
        yield return $"{pad}{{";
        foreach (var line in SourceLines(source))
            yield return $"{pad}    {line}";
        yield return $"{pad}}}";
    }

    // A rendered declaration may span several lines when leading attributes are
    // emitted one per line; indent every line so nested members stay aligned.
    static string PadDeclaration(string declaration, string pad)
        => declaration.Contains('\n', StringComparison.Ordinal)
            ? string.Join('\n', declaration.Split('\n').Select(line => line.Length == 0 ? line : pad + line))
            : pad + declaration;

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
            DefinitionName = type.DefinitionName,
            Accessibility = type.Accessibility,
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
            SignatureDecodeStatus = member.SignatureDecodeStatus,
            IsStatic = member.IsStatic,
            IsVirtual = member.IsVirtual,
            IsAbstract = member.IsAbstract,
            IsOverride = member.IsOverride,
            IsSealed = member.IsSealed,
            IsFinalizer = member.IsFinalizer,
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
            Constraints = constraints?.ToList()!,
            StructuredConstraints = parameter.StructuredConstraints,
            // Carried like StructuredConstraints: the snapshot feeds the declaration
            // writer, which cannot restate the constraint an inheriting member requires
            // without it, and losing it renders an override that does not compile.
            TypeKind = parameter.TypeKind
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
        int primaryConstructorParameterCount,
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
        if (member.IsAbstract && policy.BodyPolicy != CSharpBodyPolicy.Skeleton)
        {
            throw new ArgumentException(
                $"Abstract member '{member.Name}' must use skeleton body policy.",
                parameterName);
        }
        if (IsExplicitInterfaceEvent(member) && policy.BodyPolicy == CSharpBodyPolicy.Skeleton)
        {
            throw new NotSupportedException(
                $"Explicit interface event '{member.Name}' requires add/remove bodies.");
        }
        if (member.Kind == "event"
            && policy.BodyPolicy != CSharpBodyPolicy.Skeleton
            && policy.Body is null)
        {
            throw new NotSupportedException(
                $"Event '{member.Name}' does not support body policy '{policy.BodyPolicy}'.");
        }
        if (IsEvent(member)
            && policy.BodyPolicy != CSharpBodyPolicy.Skeleton
            && policy.Body is not CSharpEventBody)
        {
            throw new NotSupportedException(
                $"Event '{member.Name}' requires an explicit add/remove body shape.");
        }
        if (policy.Body is CSharpEventBody { Adder.Kind: CSharpAccessorBodyKind.Auto }
            or CSharpEventBody { Remover.Kind: CSharpAccessorBodyKind.Auto })
        {
            throw new ArgumentException(
                $"Event '{member.Name}' cannot use auto accessor bodies.",
                parameterName);
        }
        if (member.Kind == "field" && policy.BodyPolicy == CSharpBodyPolicy.Stub)
        {
            throw new NotSupportedException(
                $"Field '{member.Name}' does not support body policy '{policy.BodyPolicy}'.");
        }
        if (IsProperty(member)
            && policy.BodyPolicy != CSharpBodyPolicy.Skeleton
            && policy.Body is null)
        {
            throw new NotSupportedException(
                $"Property '{member.Name}' requires an explicit accessor body shape.");
        }
        if (policy.Body is CSharpBlockBody { ConstructorInitializer: not null }
            && member.Kind != "constructor")
        {
            throw new ArgumentException(
                $"Only constructors can carry a constructor initializer.",
                parameterName);
        }
        if (member.Kind == "constructor"
            && primaryConstructorParameterCount > 0
            && policy.BodyPolicy != CSharpBodyPolicy.Skeleton
            && policy.Body is not CSharpBlockBody { ConstructorInitializer: not null })
        {
            throw new NotSupportedException(
                $"Constructor '{member.Name}' in a primary-constructor type requires an explicit constructor initializer.");
        }

        bool validBody = (member.Kind, policy.Body) switch
        {
            (_, null) => policy.BodyPolicy != CSharpBodyPolicy.Full,
            ("field", CSharpFieldInitializer) => true,
            (_, CSharpPropertyBody) when IsProperty(member) => true,
            (_, CSharpEventBody) when IsEvent(member) => true,
            ("method" or "extension-method" or "explicit-interface-implementation" or "constructor" or "finalizer", CSharpBlockBody) => true,
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

    static bool IsProperty(ApiMember member)
        => member.Kind == "property"
            || member.Kind == "explicit-interface-implementation"
                && member.Name.Contains('.', StringComparison.Ordinal)
                && HasOnlyAccessors(member, "get", "set");

    static bool IsEvent(ApiMember member)
        => member.Kind == "event"
            || IsExplicitInterfaceEvent(member);

    static bool IsExplicitInterfaceEvent(ApiMember member)
        => member.Kind == "explicit-interface-implementation"
            && member.Name.Contains('.', StringComparison.Ordinal)
            && HasOnlyAccessors(member, "add", "remove");

    static bool HasOnlyAccessors(ApiMember member, string first, string second)
        => member.SignatureModel?.Accessors is { Count: > 0 } accessors
            && accessors.All(accessor => accessor.Kind == first || accessor.Kind == second);

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
