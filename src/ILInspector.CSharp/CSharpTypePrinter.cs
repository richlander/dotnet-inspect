using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.Text;

namespace ILInspector.CSharp;

public sealed class CSharpTypePrinter
{
    public CSharpTypePrintOutcome Print(
        CSharpTypePrintRequest request,
        CSharpTypePrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PrintBatch([request], options);
    }

    public CSharpTypePrintOutcome PrintBatch(
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
        if (requestList.Sum(CountReplacementTargets) > 1)
        {
            throw new ArgumentException(
                "A C# type print batch can select at most one replacement target.",
                nameof(requests));
        }

        // A single request produces at most one namespace, so a file-scoped
        // namespace declaration is always legal and reads cleaner. Multiple
        // requests may span namespaces, which only block-scoped namespaces can
        // represent in one file.
        bool useFileScopedNamespace = requestList.Length == 1;

        var preparedTypes = new List<PreparedType>();
        var diagnostics = ImmutableArray.CreateBuilder<CSharpTypePrintDiagnostic>();
        foreach (var request in requestList)
        {
            preparedTypes.Add(PrepareType(
                request,
                containingNamespace: null,
                canonicalParent: null,
                outputParent: null,
                nameof(requests)));
        }

        var selfNameFailures = preparedTypes
            .SelectMany(SelfNameFailures)
            .ToImmutableArray();
        if (selfNameFailures.Length > 0)
            return new CSharpTypePrintOutcome.NotRendered(selfNameFailures);

        ValidateDuplicateTypes(preparedTypes, nameof(requests));

        var typeNameContext = ComputeTypeNameContext(preparedTypes, options);
        var safeUsings = typeNameContext.SafeUsings;
        var derivedUsings = options.TypeNamePolicy == CSharpTypeNamePolicy.ShortWithUsings
            ? safeUsings
            : [];
        var contextualUsings = TypeNameContext(options, configuredUsings, safeUsings);
        var effectiveUsings = options.IncludeUsings
            ? configuredUsings
                .Concat(derivedUsings)
                .ToImmutableSortedSet(StringComparer.Ordinal)
            : ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
        var declaredTypeFullNamesByNamespace =
            new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        foreach (var group in preparedTypes.GroupBy(type => type.Namespace, StringComparer.Ordinal))
        {
            var declaredTypeFullNames = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var pendingTypes = new Stack<(PreparedType Type, string? Parent)>(
                group.Select(type => (type, (string?)null)));
            while (pendingTypes.TryPop(out var pending))
            {
                string name = CSharpFormatter.StripArity(pending.Type.Type.Name);
                string fullName = pending.Parent is null ? name : $"{pending.Parent}.{name}";
                declaredTypeFullNames.Add(fullName);
                foreach (var nested in pending.Type.NestedTypes)
                    pendingTypes.Push((nested, fullName));
            }
            declaredTypeFullNamesByNamespace.Add(group.Key, declaredTypeFullNames.ToImmutable());
        }
        var importedDeclaredTypeFullNames = declaredTypeFullNamesByNamespace
            .Where(entry => effectiveUsings.Contains(entry.Key))
            .SelectMany(entry => entry.Value.Select(path => (Namespace: entry.Key, Path: path)))
            .GroupBy(entry => entry.Path, StringComparer.Ordinal)
            .Where(group => group.Select(entry => entry.Namespace).Distinct(StringComparer.Ordinal).Count() == 1)
            .Select(group => group.Key)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var importedDeclaredTypeNames = preparedTypes
            .Where(type => effectiveUsings.Contains(type.Namespace))
            .Select(type => CSharpFormatter.StripArity(type.Type.Name))
            .ToImmutableHashSet(StringComparer.Ordinal);
        var globalDeclaredTypeNames = preparedTypes
            .Where(type => type.Namespace.Length == 0
                && type.Type.TypeParameters.Count == 0)
            .Select(type => CSharpFormatter.StripArity(type.Type.Name))
            .ToImmutableHashSet(StringComparer.Ordinal);
        foreach (string globalTypeName in globalDeclaredTypeNames)
        {
            bool conflictsWithNamespace = preparedTypes.Any(type =>
                NamespaceRoot(type.Namespace) == globalTypeName);
            bool conflictsWithUsing = effectiveUsings.Any(@namespace =>
                NamespaceRoot(@namespace) == globalTypeName);
            if (conflictsWithNamespace || conflictsWithUsing)
            {
                diagnostics.Add(new CSharpTypePrintDiagnostic(
                    globalTypeName,
                    $"Namespace root '{globalTypeName}' conflicts with global type '{globalTypeName}'; emitted namespace or using directives cannot bind that root as a namespace."));
            }
        }
        var globalAttributeOptions = new CSharpDeclarationOptions
        {
            TypeNameMode = options.TypeNamePolicy == CSharpTypeNamePolicy.Qualified
                ? CSharpTypeNameMode.Qualified
                : CSharpTypeNameMode.ContextualShort,
            Usings = contextualUsings,
            AdditionalRootShadowingNames = globalDeclaredTypeNames,
            AdditionalUnresolvableRootNames = globalDeclaredTypeNames,
            AdditionalKnownNamespaces = typeNameContext.KnownNamespaces
        };
        var plannedAssemblyAttributes = CSharpDeclarationWriter.RenderAttributeBodies(
            options.AssemblyAttributes,
            globalAttributeOptions);
        var plannedModuleAttributes = CSharpDeclarationWriter.RenderAttributeBodies(
            options.ModuleAttributes,
            globalAttributeOptions);
        diagnostics.AddRange(plannedAssemblyAttributes.Diagnostics.Select(
            diagnostic => new CSharpTypePrintDiagnostic("<assembly>", diagnostic)));
        diagnostics.AddRange(plannedModuleAttributes.Diagnostics.Select(
            diagnostic => new CSharpTypePrintDiagnostic("<module>", diagnostic)));
        var units = ImmutableArray.CreateBuilder<CSharpTypeSourceUnit>();
        var renderedUnits = ImmutableArray.CreateBuilder<RenderedFragment>();
        foreach (var group in preparedTypes.GroupBy(type => type.Namespace, StringComparer.Ordinal))
        {
            var groupedTypes = group.ToList();
            var declaredTypeFullNameSet = declaredTypeFullNamesByNamespace
                .Where(entry => string.Equals(entry.Key, group.Key, StringComparison.Ordinal)
                    || IsAncestorNamespace(entry.Key, group.Key))
                .SelectMany(entry => entry.Value)
                .ToImmutableHashSet(StringComparer.Ordinal);
            var containingNamespace = group.Key.Length == 0 ? null : group.Key;
            bool useBlockScopedNamespace = containingNamespace is not null && !useFileScopedNamespace;
            var ancestorTypeNames = preparedTypes
                .Where(candidate => IsAncestorNamespace(candidate.Namespace, group.Key))
                .Select(candidate => CSharpFormatter.StripArity(candidate.Type.Name))
                .ToImmutableHashSet(StringComparer.Ordinal);
            var referencedAncestorTypeNames = typeNameContext.ReferencedTypeNames
                .Where(reference => string.Equals(reference.Namespace, group.Key, StringComparison.Ordinal)
                    || IsAncestorNamespace(reference.Namespace, group.Key))
                .Select(reference => reference.SimpleName);
            var rendered = Join(
                groupedTypes.Select(type => RenderType(
                    type,
                    indent: 0,
                    options,
                    contextualUsings,
                    inheritedShadowingNames: ImmutableHashSet<string>.Empty,
                    inheritedRootShadowingNames: groupedTypes
                        .Where(sibling => !ReferenceEquals(sibling, type))
                        .Select(sibling => CSharpFormatter.StripArity(sibling.Type.Name))
                        .Concat(ancestorTypeNames)
                        .Concat(referencedAncestorTypeNames)
                        .Concat(importedDeclaredTypeNames)
                        .ToImmutableHashSet(StringComparer.Ordinal),
                    globalDeclaredTypeNames,
                    declaredTypeFullNameSet,
                    importedDeclaredTypeFullNames,
                    typeNameContext.KnownNamespaces,
                    diagnostics)),
                "\n\n");
            if (containingNamespace is not null)
            {
                string renderedNamespace = CSharpFormatter.EscapeNamespace(containingNamespace);
                rendered = useFileScopedNamespace
                    ? rendered.Wrap($"namespace {renderedNamespace};\n\n", "")
                    : rendered
                        .Indent(1)
                        .Wrap($"namespace {renderedNamespace}\n{{\n", "\n}");
            }

            units.Add(new CSharpTypeSourceUnit(containingNamespace, rendered.Source));
            renderedUnits.Add(rendered);
        }

        var unitList = units.ToImmutable();
        var renderedUnitList = renderedUnits.ToImmutable();
        return new CSharpTypePrintOutcome.Printed(
            new CSharpTypePrintResult(
                unitList,
                effectiveUsings,
                diagnostics.Distinct().ToImmutableArray(),
                () => ComposeSource(
                    renderedUnitList,
                    effectiveUsings,
                    plannedAssemblyAttributes.Attributes,
                    plannedModuleAttributes.Attributes,
                    options)));
    }

    /// <summary>
    /// Derives collision-safe namespaces and the namespace identities known to the
    /// complete output unit.
    /// </summary>
    static (
        IReadOnlyList<string> SafeUsings,
        IReadOnlyList<string> KnownNamespaces,
        IReadOnlyList<(string Namespace, string SimpleName)> ReferencedTypeNames) ComputeTypeNameContext(
        IReadOnlyList<PreparedType> preparedTypes,
        CSharpTypePrintOptions options)
    {
        var scopes = new List<(
            ApiType Type,
            IEnumerable<ApiMember> Members,
            IEnumerable<ApiParameter> AdditionalParameters,
            string DeclaredTypeFullName,
            bool CanImportDeclaringNamespace)>();
        void Flatten(PreparedType prepared, string? parentPath)
        {
            string name = CSharpFormatter.StripArity(prepared.Type.Name);
            string path = parentPath is null ? name : $"{parentPath}.{name}";
            string fullName = prepared.Namespace.Length == 0
                ? path
                : $"{prepared.Namespace}.{path}";
            scopes.Add((
                prepared.Type,
                prepared.Members.Select(member => member.Member),
                prepared.PrimaryConstructorParameters,
                fullName,
                parentPath is null));
            foreach (var nested in prepared.NestedTypes)
                Flatten(nested, path);
        }
        foreach (var prepared in preparedTypes)
            Flatten(prepared, parentPath: null);

        return CSharpDeclarationWriter.DeriveTypeNameContext(
            scopes,
            options.Usings,
            options.AssemblyAttributes.Concat(options.ModuleAttributes));
    }

    static string NamespaceRoot(string @namespace)
    {
        int separator = @namespace.IndexOf('.');
        return separator < 0 ? @namespace : @namespace[..separator];
    }

    static bool IsAncestorNamespace(string candidate, string descendant)
        => candidate.Length < descendant.Length
            && descendant.StartsWith(candidate, StringComparison.Ordinal)
            && (candidate.Length == 0 || descendant[candidate.Length] == '.');

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

    static CSharpSourceArtifact ComposeSource(
        ImmutableArray<RenderedFragment> units,
        IReadOnlyCollection<string> usings,
        IReadOnlyList<string> assemblyAttributes,
        IReadOnlyList<string> moduleAttributes,
        CSharpTypePrintOptions options)
    {
        var sb = new System.Text.StringBuilder();
        CSharpSourceRange? bodyRange = null;
        string? bodyIndent = null;
        if (options.EmitPragmaWarningDisable)
            sb.AppendLf("#pragma warning disable");
        foreach (var attribute in assemblyAttributes)
            sb.AppendLf($"[assembly: {attribute}]");
        foreach (var attribute in moduleAttributes)
            sb.AppendLf($"[module: {attribute}]");
        if (options.IncludeUsings)
        {
            foreach (var ns in usings
                .Select(CSharpFormatter.EscapeNamespace)
                .Order(StringComparer.Ordinal))
                sb.AppendLf($"using {ns};");
        }
        foreach (var unit in units)
        {
            int unitStart = sb.Length;
            sb.AppendLf(unit.Source);
            if (unit.ReplaceableBodyRange is { } range)
            {
                bodyRange = new CSharpSourceRange(unitStart + range.Start, range.Length);
                bodyIndent = unit.ReplaceableBodyIndent;
            }
        }

        return new CSharpSourceArtifact(sb.ToString(), bodyRange, bodyIndent);
    }

    static int CountReplacementTargets(CSharpTypePrintRequest request)
    {
        int count = request.MemberPolicyOverrides.Sum(policy => policy.Body switch
        {
            CSharpBlockBody { IsReplacementTarget: true } => 1,
            CSharpPropertyBody property => CountReplacementTargets(property),
            CSharpEventBody @event => CountReplacementTargets(@event),
            _ => 0,
        });
        return count + request.NestedTypes.Sum(CountReplacementTargets);
    }

    static int CountReplacementTargets(CSharpPropertyBody body)
        => (body.Getter?.IsReplacementTarget == true ? 1 : 0)
            + (body.Setter?.IsReplacementTarget == true ? 1 : 0);

    static int CountReplacementTargets(CSharpEventBody body)
        => (body.Adder.IsReplacementTarget ? 1 : 0)
            + (body.Remover.IsReplacementTarget ? 1 : 0);

    static string NormalizeNamespace(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value;

    static PreparedType PrepareType(
        CSharpTypePrintRequest request,
        string? containingNamespace,
        string? canonicalParent,
        string? outputParent,
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
        if (type.Name is null)
            throw new ArgumentException("Type print requests require a non-empty type name.");
        if (type.TypeParameters is null)
            throw new ArgumentException($"Type '{type.FullName}' has a null type-parameter collection.");
        var metadataName = string.IsNullOrWhiteSpace(type.MetadataName)
            ? type.Name
            : type.MetadataName;
        string classificationLeaf = type.DefinitionName is { } definitionName
            ? definitionName.Segments[^1]
            : type.Name;
        bool hasGeneratedMetadataName =
            CSharpFormatter.IsGeneratedMetadataName(classificationLeaf);
        bool hasExactOrdinaryEvidence =
            !hasGeneratedMetadataName
            && type.DefinitionName is not null
            && type.IntroducedTypeParameterCounts is { Count: > 0 };
        CSharpDeclaredTypeSelfNameAdmission? selfNameAdmission =
            hasExactOrdinaryEvidence
                ? CSharpDeclaredTypeSelfName.Admit(
                    type.DefinitionName!,
                    type.IntroducedTypeParameterCounts!,
                    type.TypeParameters)
                : null;
        string? legacyDeclaredTypeIdentifier =
            hasGeneratedMetadataName
                ? CSharpFormatter.NormalizeGeneratedMetadataTypeName(
                    classificationLeaf)
                : null;
        if (legacyDeclaredTypeIdentifier is not null)
            type.Name = legacyDeclaredTypeIdentifier;
        else if (hasExactOrdinaryEvidence)
            type.Name = classificationLeaf;
        ValidateRequiredShape(
            type,
            validateMetadataArity: !hasGeneratedMetadataName
                && !hasExactOrdinaryEvidence,
            validateTypeNameSpelling: !hasGeneratedMetadataName
                && !hasExactOrdinaryEvidence);
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
                parameterName))
            .ToImmutableArray();
        return new PreparedType(
            typeNamespace,
            metadataName,
            type,
            members.ToImmutable(),
            primaryConstructorParameters,
            nestedTypes,
            selfNameAdmission,
            legacyDeclaredTypeIdentifier);
    }

    static IEnumerable<CSharpDeclaredTypeSelfNameFailure> SelfNameFailures(
        PreparedType prepared)
    {
        if (prepared.SelfNameAdmission
            is CSharpDeclaredTypeSelfNameAdmission.Unrepresentable unrepresentable)
        {
            yield return unrepresentable.Failure;
        }

        foreach (PreparedType nested in prepared.NestedTypes)
        {
            foreach (CSharpDeclaredTypeSelfNameFailure failure in SelfNameFailures(nested))
                yield return failure;
        }
    }

    static void ValidateDuplicateTypes(
        IReadOnlyList<PreparedType> preparedTypes,
        string parameterName)
    {
        var canonicalIdentities = new HashSet<TypeOutputIdentity>();
        var outputIdentities = new HashSet<TypeOutputIdentity>();
        foreach (PreparedType prepared in preparedTypes)
            Validate(prepared, canonicalParent: null, outputParent: null);

        void Validate(
            PreparedType prepared,
            string? canonicalParent,
            string? outputParent)
        {
            ApiType type = prepared.Type;
            string outputName = prepared.LegacyDeclaredTypeIdentifier is { } legacyIdentifier
                ? type.TypeParameters.Count == 0
                    ? legacyIdentifier
                    : $"{legacyIdentifier}`{type.TypeParameters.Count}"
                : CSharpFormatter.FormatTypeName(type);
            string canonicalPath = canonicalParent is null
                ? prepared.CanonicalMetadataName
                : $"{canonicalParent}+{prepared.CanonicalMetadataName}";
            string outputPath = outputParent is null
                ? outputName
                : $"{outputParent}.{outputName}";
            if (!canonicalIdentities.Add(
                    new TypeOutputIdentity(prepared.Namespace, canonicalPath))
                || !outputIdentities.Add(
                    new TypeOutputIdentity(prepared.Namespace, outputPath)))
            {
                throw new ArgumentException(
                    $"Type print requests contain duplicate C# type '{type.FullName}'.",
                    parameterName);
            }

            foreach (PreparedType nested in prepared.NestedTypes)
                Validate(nested, canonicalPath, outputPath);
        }
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

    static RenderedFragment RenderType(
        PreparedType prepared,
        int indent,
        CSharpTypePrintOptions options,
        IReadOnlyList<string> contextualUsings,
        IReadOnlySet<string> inheritedShadowingNames,
        IReadOnlySet<string> inheritedRootShadowingNames,
        IReadOnlySet<string> unresolvableRootNames,
        IReadOnlySet<string> declaredTypeFullNames,
        IReadOnlySet<string> importedDeclaredTypeFullNames,
        IReadOnlyCollection<string> knownNamespaces,
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
            inScopeShadowingNames,
            inheritedRootShadowingNames,
            unresolvableRootNames,
            declaredTypeFullNames,
            importedDeclaredTypeFullNames,
            knownNamespaces,
            prepared.AdmittedSelfName,
            prepared.LegacyDeclaredTypeIdentifier);
        var diagnosticPass = DeclarationFormatter(
            prepared.Namespace,
            options,
            contextualUsings,
            inScopeShadowingNames,
            inheritedRootShadowingNames,
            unresolvableRootNames,
            declaredTypeFullNames,
            importedDeclaredTypeFullNames,
            knownNamespaces,
            prepared.AdmittedSelfName,
            prepared.LegacyDeclaredTypeIdentifier,
            terminateMemberDeclaration: true)
            .FormatTypeUnit(
                prepared.Type,
                prepared.Members.Select(member => member.Member),
                prepared.PrimaryConstructorParameters);
        diagnostics.AddRange(diagnosticPass.Diagnostics.Select(
            diagnostic => new CSharpTypePrintDiagnostic(prepared.Type.FullName, diagnostic)));
        if (prepared.Type.Kind == "delegate")
            return new RenderedFragment(RenderDelegate(prepared, formatter, indent));

        var propertyFormatter = DeclarationFormatter(
            prepared.Namespace,
            options,
            contextualUsings,
            inScopeShadowingNames,
            inheritedRootShadowingNames,
            unresolvableRootNames,
            declaredTypeFullNames,
            importedDeclaredTypeFullNames,
            knownNamespaces,
            prepared.AdmittedSelfName,
            prepared.LegacyDeclaredTypeIdentifier,
            omitPropertyAccessors: true);
        string pad = new(' ', indent * 4);
        string declaration = formatter.FormatTypeDeclaration(
            prepared.Type,
            prepared.PrimaryConstructorParameters);

        var fragments = new List<RenderedFragment>
        {
            new(PadDeclaration(declaration, pad)),
            new($"{pad}{{")
        };
        if (prepared.Type.Kind == "enum")
        {
            fragments.AddRange(prepared.Members.Select((member, index) =>
                new RenderedFragment(
                    RenderEnumMember(member, indent + 1, index < prepared.Members.Length - 1))));
        }
        else
        {
            foreach (var member in prepared.Members)
                fragments.Add(RenderMember(prepared, member, formatter, propertyFormatter, indent + 1));
            foreach (var nested in prepared.NestedTypes)
            {
                var nestedShadowingNames = inScopeShadowingNames.ToHashSet(StringComparer.Ordinal);
                var nestedRootShadowingNames = inheritedRootShadowingNames.ToHashSet(StringComparer.Ordinal);
                nestedRootShadowingNames.Add(CSharpFormatter.StripArity(prepared.Type.Name));
                fragments.Add(RenderType(
                    nested,
                    indent + 1,
                    options,
                    contextualUsings,
                    nestedShadowingNames,
                    nestedRootShadowingNames,
                    unresolvableRootNames,
                    declaredTypeFullNames,
                    importedDeclaredTypeFullNames,
                    knownNamespaces,
                    diagnostics));
            }
        }
        fragments.Add(new RenderedFragment($"{pad}}}"));
        return Join(fragments, "\n");
    }

    static RenderedFragment RenderMember(
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
                return new RenderedFragment($"{PadDeclaration(declaration, pad)} = {fieldInitializer.Source};");
            return new RenderedFragment(PadDeclaration(EnsureTerminated(declaration), pad));
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
            return new RenderedFragment(PadDeclaration(EnsureTerminated(memberDeclaration), pad));
        }
        string initializer = member.Body is CSharpBlockBody { ConstructorInitializer: { } constructorInitializer }
            ? " " + CSharpFormatter.FormatConstructorInitializer(constructorInitializer)
            : "";
        if (member.Body is null && member.Policy == CSharpBodyPolicy.Stub)
        {
            return new RenderedFragment(
                $"{PadDeclaration(memberDeclaration, pad)}{initializer} {{ throw null; }}");
        }

        var block = member.Body switch
        {
            CSharpBlockBody body => body,
            _ => throw new InvalidOperationException(
                $"Member '{member.Member.Name}' has no renderable block body."),
        };
        if (member.Policy == CSharpBodyPolicy.Stub && block.Source == "throw null;")
        {
            return new RenderedFragment(
                $"{PadDeclaration(memberDeclaration, pad)}{initializer} {{ throw null; }}");
        }
        return RenderBlock(
            memberDeclaration + initializer,
            block.Source,
            indent,
            block.IsReplacementTarget);
    }

    static RenderedFragment RenderProperty(
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
            return new RenderedFragment(PadDeclaration(EnsureTerminated(skeleton), pad));
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
                accessors.Add(formatter.FormatAccessorHead(type.Type, member.Member, "get") + ";");
            if (body.Setter is not null)
                accessors.Add(formatter.FormatAccessorHead(
                    type.Type,
                    member.Member,
                    SetterKeyword(member.Member)) + ";");
            return new RenderedFragment(
                $"{PadDeclaration(declaration, pad)} {{ {string.Join(" ", accessors)} }}");
        }

        var fragments = new List<RenderedFragment>
        {
            new(PadDeclaration(declaration, pad)),
            new($"{pad}{{")
        };
        if (body.Getter is not null)
        {
            fragments.Add(
                RenderAccessor(type.Type, member.Member, "get", body.Getter, formatter, indent + 1));
        }
        if (body.Setter is not null)
        {
            fragments.Add(
                RenderAccessor(
                    type.Type,
                    member.Member,
                    SetterKeyword(member.Member),
                    body.Setter,
                    formatter,
                    indent + 1));
        }
        fragments.Add(new RenderedFragment($"{pad}}}"));
        return Join(fragments, "\n");
    }

    // An init-only property's write accessor is spelled `init`, not `set`. Honor the
    // accessor model so full-body rendering does not silently downgrade `init` to a
    // public `set` (dropping the required modreq(IsExternalInit)).
    static string SetterKeyword(ApiMember member)
        => member.SignatureModel?.Accessors is { } accessors
            && accessors.Any(accessor => accessor.Kind == "init")
            ? "init"
            : "set";

    static RenderedFragment RenderEvent(
        PreparedType type,
        PreparedMember member,
        CSharpFormatter formatter,
        int indent)
    {
        string pad = new(' ', indent * 4);
        if (member.Policy == CSharpBodyPolicy.Skeleton)
        {
            string skeleton = formatter.FormatMember(type.Type, member.Member);
            return new RenderedFragment(PadDeclaration(EnsureTerminated(skeleton), pad));
        }

        var body = (CSharpEventBody)member.Body!;
        string declaration = formatter.FormatMemberWithBody(
            type.Type,
            member.Member,
            body);
        var fragments = new List<RenderedFragment>
        {
            new(PadDeclaration(declaration, pad)),
            new($"{pad}{{"),
            RenderAccessor(type.Type, member.Member, "add", body.Adder, formatter, indent + 1),
            RenderAccessor(type.Type, member.Member, "remove", body.Remover, formatter, indent + 1),
            new($"{pad}}}")
        };
        return Join(fragments, "\n");
    }

    static RenderedFragment RenderAccessor(
        ApiType declaringType,
        ApiMember member,
        string kind,
        CSharpAccessorBody body,
        CSharpFormatter formatter,
        int indent)
    {
        string pad = new(' ', indent * 4);
        string head = formatter.FormatAccessorHead(declaringType, member, kind);
        if (body.Kind == CSharpAccessorBodyKind.Auto)
            return new RenderedFragment($"{pad}{head};");

        string source = body.Kind == CSharpAccessorBodyKind.Throw
            ? "throw null;"
            : body.Source!;
        var block = RenderBodyBlock(source, indent, body.IsReplacementTarget);
        return block.Wrap($"{pad}{head}\n", "");
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
        IReadOnlyCollection<string> additionalRootShadowingNames,
        IReadOnlyCollection<string> additionalUnresolvableRootNames,
        IReadOnlyCollection<string> additionalDeclaredTypeFullNames,
        IReadOnlyCollection<string> additionalImportedDeclaredTypeFullNames,
        IReadOnlyCollection<string> additionalKnownNamespaces,
        CSharpDeclaredTypeSelfNameAdmission.Admitted? declaredTypeSelfName = null,
        string? legacyDeclaredTypeIdentifier = null,
        bool omitPropertyAccessors = false,
        bool terminateMemberDeclaration = false)
        => new(new CSharpFormatOptions
        {
            TypeNamePolicy = options.TypeNamePolicy == CSharpTypeNamePolicy.Qualified
                ? CSharpTypeNamePolicy.Qualified
                : CSharpTypeNamePolicy.ContextualShort,
            ContainingNamespace = containingNamespace.Length == 0 ? null : containingNamespace,
            Usings = contextualUsings,
            AdditionalShadowingNames = additionalShadowingNames,
            AdditionalRootShadowingNames = additionalRootShadowingNames,
            AdditionalUnresolvableRootNames = additionalUnresolvableRootNames,
            AdditionalDeclaredTypeFullNames = additionalDeclaredTypeFullNames,
            AdditionalImportedDeclaredTypeFullNames = additionalImportedDeclaredTypeFullNames,
            AdditionalKnownNamespaces = additionalKnownNamespaces,
            DeclaredTypeSelfName = declaredTypeSelfName,
            LegacyDeclaredTypeIdentifier = legacyDeclaredTypeIdentifier,
            NamespacePolicy = CSharpNamespacePolicy.Omit,
            IncludeCustomAttributes = options.IncludeCustomAttributes,
            OmitPropertyAccessors = omitPropertyAccessors,
            TerminateMemberDeclaration = terminateMemberDeclaration
        });

    static RenderedFragment RenderBlock(
        string declaration,
        string source,
        int indent,
        bool isReplacementTarget)
    {
        string pad = new(' ', indent * 4);
        var body = RenderBodyBlock(source, indent, isReplacementTarget);
        return body.Wrap(PadDeclaration(declaration, pad) + "\n", "");
    }

    static RenderedFragment RenderBodyBlock(
        string source,
        int indent,
        bool isReplacementTarget)
    {
        string pad = new(' ', indent * 4);
        string block = CSharpSourceLayout.RenderBlock(source, pad);
        return isReplacementTarget
            ? new RenderedFragment(
                block,
                new CSharpSourceRange(0, block.Length),
                pad)
            : new RenderedFragment(block);
    }

    // A rendered declaration may span several lines when leading attributes are
    // emitted one per line; indent every line so nested members stay aligned.
    static string PadDeclaration(string declaration, string pad)
        => declaration.Contains('\n', StringComparison.Ordinal)
            ? string.Join('\n', declaration.Split('\n').Select(line => line.Length == 0 ? line : pad + line))
            : pad + declaration;

    static bool AllAuto(CSharpPropertyBody body)
        => (body.Getter is null || body.Getter.Kind == CSharpAccessorBodyKind.Auto)
            && (body.Setter is null || body.Setter.Kind == CSharpAccessorBodyKind.Auto);

    static string EnsureTerminated(string declaration)
        => declaration.EndsWith(';') || declaration.EndsWith('}')
            ? declaration
            : declaration + ";";

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
            IntroducedTypeParameterCounts =
                type.IntroducedTypeParameterCounts?.ToList(),
            Accessibility = type.Accessibility,
            Kind = type.Kind,
            Layout = type.Layout,
            MemorySafety = type.MemorySafety,
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
            MemorySafety = member.MemorySafety,
            AccessorMemorySafety = member.AccessorMemorySafety,
            BackingStorage = member.BackingStorage,
            MethodImplementation = member.MethodImplementation,
            AccessorImplementations = member.AccessorImplementations,
            HasMethodBody = member.HasMethodBody,
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

    static void ValidateRequiredShape(
        ApiType type,
        bool validateMetadataArity,
        bool validateTypeNameSpelling)
    {
        if (validateTypeNameSpelling && string.IsNullOrWhiteSpace(type.Name))
            throw new ArgumentException("Type print requests require a non-empty type name.");
        if (type.TypeParameters is null)
            throw new ArgumentException($"Type '{type.FullName}' has a null type-parameter collection.");
        if (validateTypeNameSpelling
            && (type.Name.Contains('<', StringComparison.Ordinal)
                || type.Name.Contains('>', StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' must use a metadata name rather than C# type-argument spelling.");
        }
        if (!validateMetadataArity)
            return;

        // Only a canonical `N is arity (MetadataNameArity): int.TryParse would
        // accept "Widget`+1", padded digits, and non-ASCII digits, letting a name
        // that is not a generic spelling satisfy the arity contract.
        if (!MetadataNameArity.TryReadSuffix(type.Name, out int arity, out _))
        {
            bool trustedMissingArity =
                type.DefinitionName is { } definitionName
                && type.IntroducedTypeParameterCounts is { } introduced
                && introduced.Count
                    == definitionName.Segments.Length
                && introduced[^1]
                    == type.TypeParameters.Count;
            if (type.TypeParameters.Count > 0
                && !trustedMissingArity)
            {
                throw new ArgumentException(
                    $"Generic type '{type.FullName}' requires metadata arity in its name.");
            }

            return;
        }

        if (arity != type.TypeParameters.Count)
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' has inconsistent metadata arity and type parameters.");
        }
    }

    static void ValidateTypeKindAndContainment(ApiType type, bool isNested)
    {
        bool hasDeclaringType = type.DefinitionName is { } exactName
            ? exactName.Segments.Length > 1
            : type.MetadataName?.Contains('+', StringComparison.Ordinal) == true
                || type.Name.Contains('.', StringComparison.Ordinal)
                || type.Name.Contains('+', StringComparison.Ordinal);
        if (!isNested && hasDeclaringType)
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
        if (policy.Body is CSharpBlockBody { IsReplacementTarget: true }
            && policy.BodyPolicy != CSharpBodyPolicy.Full)
        {
            throw new ArgumentException(
                $"Replacement target '{member.Name}' must use full body policy.",
                parameterName);
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
                && HasOnlyAccessors(member, "get", "set", "init");

    static bool IsEvent(ApiMember member)
        => member.Kind == "event"
            || IsExplicitInterfaceEvent(member);

    static bool IsExplicitInterfaceEvent(ApiMember member)
        => member.Kind == "explicit-interface-implementation"
            && member.Name.Contains('.', StringComparison.Ordinal)
            && HasOnlyAccessors(member, "add", "remove");

    static bool HasOnlyAccessors(ApiMember member, params string[] kinds)
        => member.SignatureModel?.Accessors is { Count: > 0 } accessors
            && accessors.All(accessor => kinds.Contains(accessor.Kind, StringComparer.Ordinal));

    static RenderedFragment Join(IEnumerable<RenderedFragment> fragments, string separator)
    {
        var array = fragments.ToArray();
        if (array.Length == 0)
            return new RenderedFragment("");

        var source = new System.Text.StringBuilder();
        CSharpSourceRange? bodyRange = null;
        string? bodyIndent = null;
        for (int i = 0; i < array.Length; i++)
        {
            if (i > 0)
                source.Append(separator);

            var fragment = array[i];
            int fragmentStart = source.Length;
            source.Append(fragment.Source);
            if (fragment.ReplaceableBodyRange is { } range)
            {
                if (bodyRange is not null)
                    throw new InvalidOperationException("Rendered C# contains multiple replacement targets.");
                bodyRange = new CSharpSourceRange(fragmentStart + range.Start, range.Length);
                bodyIndent = fragment.ReplaceableBodyIndent;
            }
        }

        return new RenderedFragment(source.ToString(), bodyRange, bodyIndent);
    }

    sealed record RenderedFragment(
        string Source,
        CSharpSourceRange? ReplaceableBodyRange = null,
        string? ReplaceableBodyIndent = null)
    {
        internal RenderedFragment Indent(int depth)
        {
            string pad = new(' ', depth * 4);
            string IndentSource(string source)
                => string.Join(
                    '\n',
                    source.Split('\n').Select(line => line.Length == 0 ? line : pad + line));

            if (ReplaceableBodyRange is not { } range)
                return new RenderedFragment(IndentSource(Source));

            string prefix = IndentSource(Source[..range.Start]);
            string body = IndentSource(Source.Substring(range.Start, range.Length));
            string suffix = IndentSource(Source[range.End..]);
            return new RenderedFragment(
                prefix + body + suffix,
                new CSharpSourceRange(prefix.Length, body.Length),
                pad + ReplaceableBodyIndent);
        }

        internal RenderedFragment Wrap(string prefix, string suffix)
            => new(
                prefix + Source + suffix,
                ReplaceableBodyRange is { } range
                    ? new CSharpSourceRange(prefix.Length + range.Start, range.Length)
                    : null,
                ReplaceableBodyIndent);
    }

    sealed record PreparedType(
        string Namespace,
        string CanonicalMetadataName,
        ApiType Type,
        ImmutableArray<PreparedMember> Members,
        ImmutableArray<ApiParameter> PrimaryConstructorParameters,
        ImmutableArray<PreparedType> NestedTypes,
        CSharpDeclaredTypeSelfNameAdmission? SelfNameAdmission,
        string? LegacyDeclaredTypeIdentifier)
    {
        internal CSharpDeclaredTypeSelfNameAdmission.Admitted? AdmittedSelfName
            => SelfNameAdmission as CSharpDeclaredTypeSelfNameAdmission.Admitted;
    }

    readonly record struct PreparedMember(
        ApiMember Member,
        CSharpBodyPolicy Policy,
        CSharpMemberBody? Body);

    readonly record struct TypeOutputIdentity(string Namespace, string Name);
}
