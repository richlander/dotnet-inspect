using System.Collections.Immutable;
using System.Text;
using CSharpText;
using ILInspector.Metadata;
using ILInspector.Text;

namespace ILInspector.CSharp;

internal enum CSharpTypeNameMode
{
    Qualified,
    ShortWithUsings,
    ContextualShort
}

internal enum CSharpNamespaceMode
{
    Omit,
    FileScoped
}

internal sealed record CSharpDeclarationOptions
{
    public CSharpTypeNameMode TypeNameMode { get; init; } = CSharpTypeNameMode.Qualified;
    public string? ContainingNamespace { get; init; }
    public IReadOnlyCollection<string> Usings { get; init; } = [];
    // Fields avoid shifting the MethodDef tokens pinned by AuthoredCorpusHarnessProcessTests.
    public IReadOnlyCollection<string> AdditionalShadowingNames = [];
    public IReadOnlyCollection<string> AdditionalRootShadowingNames = [];
    public IReadOnlyCollection<string> AdditionalUnresolvableRootNames = [];
    public IReadOnlyCollection<string> AdditionalDeclaredTypeFullNames = [];
    public IReadOnlyCollection<string> AdditionalImportedDeclaredTypeFullNames = [];
    public IReadOnlyCollection<string> AdditionalKnownNamespaces = [];
    public CSharpDeclaredTypeSelfNameAdmission.Admitted? DeclaredTypeSelfName { get; init; }
    public string? LegacyDeclaredTypeIdentifier { get; init; }
    public CSharpNamespaceMode NamespaceMode { get; init; } = CSharpNamespaceMode.Omit;
    public bool AbbreviateSignature { get; init; }
    public bool TerminateMemberDeclaration { get; init; }
    public bool ForceAsync { get; init; }
    public bool ForceUnsafe { get; init; }
    public bool IncludeCustomAttributes { get; init; } = false;
    public bool IncludeSignatureAttributes { get; init; } = true;
    public bool IncludeObsoleteAttribute { get; init; } = true;
    public bool OmitInterfaceMemberModifiers { get; init; }
    public bool OmitPropertyAccessors { get; init; }

    /// <summary>
    /// When true, a finalizer member (<see cref="ApiMember.IsFinalizer"/>) is
    /// rendered as the literal <c>void Finalize()</c> method rather than the
    /// <c>~Type()</c> destructor syntax. Set on body-bearing renders whose body
    /// was not recovered as a canonical destructor, so the emitted source does
    /// not silently re-inject the compiler's mandatory <c>base.Finalize()</c>.
    /// </summary>
    public bool SuppressFinalizerSpelling { get; init; }
}

internal sealed record CSharpRenderedDeclaration(
    string Source,
    ImmutableSortedSet<string> Usings,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Cheap C# declaration and signature composition over the API metadata model.
/// It never imports method bodies, opens inspected assemblies, or depends on the decompiler.
/// </summary>
internal static class CSharpDeclarationWriter
{
    public static CSharpRenderedDeclaration RenderMemberUnit(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions? options = null,
        IReadOnlyList<string>? methodParameters = null)
    {
        options ??= new CSharpDeclarationOptions();
        ApiMember? signatureMember = options.IncludeSignatureAttributes ? member : null;
        var attributeReferences = CollectAttributeTypeReferences(member.Attributes, signatureMember)
            .ToHashSet(StringComparer.Ordinal);
        var attributeValueReferences = CollectAttributeValueTypeReferences(
                member.Attributes,
                signatureMember)
            .ToHashSet(StringComparer.Ordinal);
        var qualificationOnlyAttributeReferences = (options.IncludeSignatureAttributes
                ? CollectQualificationOnlyAttributeTypeReferences(member.Attributes, member)
                : CollectDeclaredAttributeTypeReferences(member.Attributes))
            .Concat(CollectExplicitInterfaceTypeReferences(member))
            .ToHashSet(StringComparer.Ordinal);
        var synthesizedAttributeReferences = member.IsObsolete && options.IncludeObsoleteAttribute
            ? new HashSet<string>(["System.Obsolete"], StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var memberReferences = CollectMemberTypeReferences(
                member,
                options.IncludeSignatureAttributes)
            .ToHashSet(StringComparer.Ordinal);
        var explicitInterfaceReferences = CollectExplicitInterfaceTypeReferences(member)
            .ToHashSet(StringComparer.Ordinal);
        var references = memberReferences
            .Concat(attributeReferences)
            .Concat(explicitInterfaceReferences)
            .Concat(synthesizedAttributeReferences);
        var plan = TypeNamePlan.Create(
            references,
            options,
            CollectShadowingNames(type, [member]),
            CSharpFormatter.StripArity(type.Name),
            qualificationOnlyAttributeReferences,
            memberReferences.Except(explicitInterfaceReferences).ToHashSet(StringComparer.Ordinal),
            attributeValueReferences,
            synthesizedAttributeReferences);
        var declaration = RenderMemberDeclarationCore(type, member, options, methodParameters);
        declaration = plan.Apply(declaration);

        if (options.TerminateMemberDeclaration && NeedsTerminator(declaration))
            declaration += ";";

        var source = ComposeUnit([declaration], plan.GeneratedUsings, options);
        return new CSharpRenderedDeclaration(source, plan.GeneratedUsings, plan.Diagnostics);
    }

    public static string RenderMemberDeclaration(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions? options = null,
        IReadOnlyList<string>? methodParameters = null)
    {
        options ??= new CSharpDeclarationOptions();
        ApiMember? signatureMember = options.IncludeSignatureAttributes ? member : null;
        var attributeReferences = CollectAttributeTypeReferences(member.Attributes, signatureMember)
            .ToHashSet(StringComparer.Ordinal);
        var attributeValueReferences = CollectAttributeValueTypeReferences(
                member.Attributes,
                signatureMember)
            .ToHashSet(StringComparer.Ordinal);
        var qualificationOnlyAttributeReferences = (options.IncludeSignatureAttributes
                ? CollectQualificationOnlyAttributeTypeReferences(member.Attributes, member)
                : CollectDeclaredAttributeTypeReferences(member.Attributes))
            .Concat(CollectExplicitInterfaceTypeReferences(member))
            .ToHashSet(StringComparer.Ordinal);
        var synthesizedAttributeReferences = member.IsObsolete && options.IncludeObsoleteAttribute
            ? new HashSet<string>(["System.Obsolete"], StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var memberReferences = CollectMemberTypeReferences(
                member,
                options.IncludeSignatureAttributes)
            .ToHashSet(StringComparer.Ordinal);
        var explicitInterfaceReferences = CollectExplicitInterfaceTypeReferences(member)
            .ToHashSet(StringComparer.Ordinal);
        var references = memberReferences
            .Concat(attributeReferences)
            .Concat(explicitInterfaceReferences)
            .Concat(synthesizedAttributeReferences);
        var plan = TypeNamePlan.Create(
            references,
            options,
            CollectShadowingNames(type, [member]),
            CSharpFormatter.StripArity(type.Name),
            qualificationOnlyAttributeReferences,
            memberReferences.Except(explicitInterfaceReferences).ToHashSet(StringComparer.Ordinal),
            attributeValueReferences,
            synthesizedAttributeReferences);
        var declaration = RenderMemberDeclarationCore(type, member, options, methodParameters);
        declaration = plan.Apply(declaration);
        return options.TerminateMemberDeclaration && NeedsTerminator(declaration)
            ? declaration + ";"
            : declaration;
    }

    public static CSharpRenderedDeclaration RenderTypeUnit(
        ApiType type,
        IEnumerable<ApiMember>? members = null,
        CSharpDeclarationOptions? options = null,
        IReadOnlyList<ApiParameter>? primaryConstructorParameters = null)
    {
        options ??= new CSharpDeclarationOptions { NamespaceMode = CSharpNamespaceMode.FileScoped };
        var memberList = members?.ToList() ?? type.Members;
        var parameters = primaryConstructorParameters ?? [];
        var attributeReferences = CollectAttributeTypeReferences(type.Attributes)
            .Concat(memberList.SelectMany(member => CollectAttributeTypeReferences(
                member.Attributes,
                options.IncludeSignatureAttributes ? member : null)))
            .ToHashSet(StringComparer.Ordinal);
        var attributeValueReferences = CollectAttributeValueTypeReferences(type.Attributes)
            .Concat(memberList.SelectMany(member =>
                CollectAttributeValueTypeReferences(
                    member.Attributes,
                    options.IncludeSignatureAttributes ? member : null)))
            .Concat(options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectAttributeValueTypeReferences(parameter.Attributes))
                : [])
            .ToHashSet(StringComparer.Ordinal);
        var qualificationOnlyAttributeReferences = CollectDeclaredAttributeTypeReferences(type.Attributes)
            .Concat(memberList.SelectMany(member =>
                options.IncludeSignatureAttributes
                    ? CollectQualificationOnlyAttributeTypeReferences(member.Attributes, member)
                    : CollectDeclaredAttributeTypeReferences(member.Attributes)))
            .Concat(options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectAttributeArgumentTypeReferences(parameter.Attributes))
                : [])
            .Concat(memberList.SelectMany(CollectExplicitInterfaceTypeReferences))
            .ToHashSet(StringComparer.Ordinal);
        var synthesizedAttributeReferences = memberList
            .Where(member => member.IsObsolete && options.IncludeObsoleteAttribute)
            .Select(_ => "System.Obsolete")
            .ToHashSet(StringComparer.Ordinal);
        var primaryParameterAttributeReferences = (options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectAttributeTypeReferences(parameter.Attributes))
                : [])
            .ToHashSet(StringComparer.Ordinal);
        var primaryParameterDeclaredAttributeReferences = (options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectDeclaredAttributeTypeReferences(parameter.Attributes))
                : [])
            .ToHashSet(StringComparer.Ordinal);
        var shortenableReferences = CollectTypeReferences(type)
            .Concat(memberList.SelectMany(member =>
                CollectMemberTypeReferences(member, options.IncludeSignatureAttributes)))
            .Concat(parameters.SelectMany(parameter =>
                ExtractTypeNames(parameter.Type)))
            .ToHashSet(StringComparer.Ordinal);
        shortenableReferences.ExceptWith(memberList.SelectMany(CollectExplicitInterfaceTypeReferences));
        var references = shortenableReferences
            .Concat(parameters.SelectMany(parameter =>
                CollectParameterTypeReferences(parameter, options.IncludeSignatureAttributes)))
            .Concat(attributeReferences)
            .Concat(primaryParameterAttributeReferences)
            .Concat(memberList.SelectMany(CollectExplicitInterfaceTypeReferences))
            .Concat(synthesizedAttributeReferences);
        var plan = TypeNamePlan.Create(
            references,
            options,
            CollectShadowingNames(type, memberList),
            CSharpFormatter.StripArity(type.Name),
            qualificationOnlyAttributeReferences,
            shortenableReferences,
            attributeValueReferences,
            synthesizedAttributeReferences,
            primaryParameterDeclaredAttributeReferences);

        string typeDeclaration = AddPrimaryConstructorParameters(
            type,
            RenderTypeDeclarationCore(type, options),
            options,
            parameters);
        List<string> lines = [plan.Apply(typeDeclaration)];
        lines.Add("{");
        foreach (var member in memberList)
        {
            var declaration = RenderMemberDeclarationCore(
                type,
                member,
                options with { TerminateMemberDeclaration = true });
            if (declaration.Length > 0 && NeedsTerminator(declaration))
                declaration += ";";
            if (declaration.Length > 0)
                lines.Add(IndentEveryLine(plan.Apply(declaration), "    "));
        }
        lines.Add("}");

        var source = ComposeUnit(lines, plan.GeneratedUsings, options);
        return new CSharpRenderedDeclaration(source, plan.GeneratedUsings, plan.Diagnostics);
    }

    static string IndentEveryLine(string text, string pad)
        => text.Contains('\n', StringComparison.Ordinal)
            ? string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? line : pad + line))
            : pad + text;

    public static string RenderTypeDeclaration(
        ApiType type,
        CSharpDeclarationOptions? options = null,
        IReadOnlyList<ApiParameter>? primaryConstructorParameters = null,
        ApiMember? delegateInvoke = null)
    {
        options ??= new CSharpDeclarationOptions();
        var parameters = primaryConstructorParameters ?? [];
        if (delegateInvoke is not null)
        {
            if (delegateInvoke.SignatureModel is not { } signature)
            {
                throw new NotSupportedException(
                    $"Delegate '{type.FullName}' requires a structured Invoke signature.");
            }

            var delegateAttributeReferences = CollectAttributeTypeReferences(type.Attributes)
                .Concat(CollectAttributeTypeReferences(
                    delegateInvoke.Attributes,
                    options.IncludeSignatureAttributes ? delegateInvoke : null))
                .ToHashSet(StringComparer.Ordinal);
            var delegateAttributeValueReferences = CollectAttributeValueTypeReferences(type.Attributes)
                .Concat(CollectAttributeValueTypeReferences(
                    delegateInvoke.Attributes,
                    options.IncludeSignatureAttributes ? delegateInvoke : null))
                .ToHashSet(StringComparer.Ordinal);
            var delegateSignatureReferences = CollectTypeReferences(type)
                .Concat(CollectMemberTypeReferences(
                    delegateInvoke,
                    options.IncludeSignatureAttributes))
                .ToHashSet(StringComparer.Ordinal);
            var delegateQualificationOnlyReferences = CollectDeclaredAttributeTypeReferences(type.Attributes)
                .Concat(options.IncludeSignatureAttributes
                    ? CollectDeclaredAttributeTypeReferences(
                        delegateInvoke.Attributes,
                        delegateInvoke)
                    : CollectDeclaredAttributeTypeReferences(delegateInvoke.Attributes))
                .ToHashSet(StringComparer.Ordinal);
            delegateSignatureReferences.ExceptWith(delegateQualificationOnlyReferences);
            var references = delegateSignatureReferences
                .Concat(delegateQualificationOnlyReferences)
                .Concat(delegateAttributeReferences)
                .ToList();
            var delegatePlan = TypeNamePlan.Create(
                references,
                options,
                CollectShadowingNames(type, [delegateInvoke]),
                CSharpFormatter.StripArity(type.Name),
                delegateQualificationOnlyReferences,
                delegateSignatureReferences,
                valueReferences: delegateAttributeValueReferences);
            string attributes = options.IncludeCustomAttributes && type.Attributes.Count > 0
                ? string.Join("\n", type.Attributes.Select(attribute => $"[{attribute}]")) + "\n"
                : "";
            string unsafeText = delegateInvoke.IsUnsafe ? " unsafe" : "";
            string parameterList = $"({string.Join(", ", signature.Parameters.Select(parameter =>
                FormatParameter(parameter, options.IncludeSignatureAttributes)))})";
            string returnAttributes = options.IncludeSignatureAttributes
                && signature.ReturnAttributes.Count > 0
                ? $"[return: {string.Join(", ", signature.ReturnAttributes)}]\n"
                : "";
            string delegateDeclaration =
                $"{attributes}{returnAttributes}{TypeAccessibility(type)}{unsafeText} delegate {signature.ReturnType ?? "void"} {FormatTypeDisplayName(type, options)}{parameterList}";
            delegateDeclaration = AppendTypeParameterConstraints(delegateDeclaration, type.TypeParameters);
            return delegatePlan.Apply(delegateDeclaration + ";");
        }

        var attributeReferences = CollectAttributeTypeReferences(type.Attributes).ToHashSet(StringComparer.Ordinal);
        var attributeValueReferences = CollectAttributeValueTypeReferences(type.Attributes)
            .Concat(options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectAttributeValueTypeReferences(parameter.Attributes))
                : [])
            .ToHashSet(StringComparer.Ordinal);
        var parameterAttributeReferences = (options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectAttributeTypeReferences(parameter.Attributes))
                : [])
            .ToHashSet(StringComparer.Ordinal);
        var qualificationOnlyAttributeReferences = CollectDeclaredAttributeTypeReferences(type.Attributes)
            .Concat(options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectAttributeArgumentTypeReferences(parameter.Attributes))
                : [])
            .ToHashSet(StringComparer.Ordinal);
        var shortenableReferences = CollectTypeReferences(type)
            .Concat(parameters.SelectMany(parameter =>
                ExtractTypeNames(parameter.Type)))
            .ToHashSet(StringComparer.Ordinal);
        var primaryParameterDeclaredAttributeReferences = (options.IncludeSignatureAttributes
                ? parameters.SelectMany(parameter =>
                    CollectDeclaredAttributeTypeReferences(parameter.Attributes))
                : [])
            .ToHashSet(StringComparer.Ordinal);
        var plan = TypeNamePlan.Create(
            shortenableReferences
                .Concat(parameters.SelectMany(parameter =>
                    CollectParameterTypeReferences(
                        parameter,
                        options.IncludeSignatureAttributes)))
                .Concat(attributeReferences)
                .Concat(parameterAttributeReferences),
            options,
            CollectShadowingNames(type, []),
            CSharpFormatter.StripArity(type.Name),
            qualificationOnlyAttributeReferences,
            shortenableReferences,
            attributeValueReferences,
            attributeNameReferences: primaryParameterDeclaredAttributeReferences);
        string declaration = AddPrimaryConstructorParameters(
            type,
            RenderTypeDeclarationCore(type, options),
            options,
            parameters);
        return plan.Apply(declaration);
    }

    internal static (
        IReadOnlyList<string> Attributes,
        IReadOnlyList<string> Diagnostics) RenderAttributeBodies(
        IReadOnlyList<string> attributes,
        CSharpDeclarationOptions options)
    {
        var references = CollectAttributeTypeReferences(attributes).ToHashSet(StringComparer.Ordinal);
        var valueReferences = CollectAttributeValueTypeReferences(attributes).ToHashSet(StringComparer.Ordinal);
        var qualificationOnlyReferences = CollectDeclaredAttributeTypeReferences(attributes)
            .ToHashSet(StringComparer.Ordinal);
        var plan = TypeNamePlan.Create(
            references,
            options,
            new HashSet<string>(StringComparer.Ordinal),
            "",
            qualificationOnlyReferences,
            valueReferences: valueReferences);
        return (attributes.Select(plan.Apply).ToArray(), plan.Diagnostics);
    }

    /// <summary>
    /// Computes a collision-safe set of namespaces that can be imported as
    /// <c>using</c> directives for a compilation unit declaring
    /// <paramref name="types"/> (including any nested types the caller flattens in).
    /// A namespace is included only when every simple type name it contributes is
    /// unambiguous across the whole unit: the simple name maps to a single full name
    /// and is not shadowed by a declaration or visible namespace. Importing such a
    /// namespace and shortening those references therefore cannot rebind them.
    /// Ambiguous or shadowed references stay qualified and their namespaces are
    /// excluded.
    /// </summary>
    public static IReadOnlyList<string> DeriveContextualUsings(IReadOnlyCollection<ApiType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return DeriveTypeNameContext(types.Select(type => (
            Type: type,
            Members: (IEnumerable<ApiMember>)type.Members,
            AdditionalParameters: Enumerable.Empty<ApiParameter>(),
            DeclaredTypeFullName: string.IsNullOrWhiteSpace(type.Namespace)
                ? CSharpFormatter.StripArity(type.Name)
                : $"{type.Namespace}.{CSharpFormatter.StripArity(type.Name)}",
            CanImportDeclaringNamespace: true)))
            .SafeUsings;
    }

    internal static (
        IReadOnlyList<string> SafeUsings,
        IReadOnlyList<string> KnownNamespaces,
        IReadOnlyList<(string Namespace, string SimpleName)> ReferencedTypeNames) DeriveTypeNameContext(
        IEnumerable<(
            ApiType Type,
            IEnumerable<ApiMember> Members,
            IEnumerable<ApiParameter> AdditionalParameters,
            string DeclaredTypeFullName,
            bool CanImportDeclaringNamespace)> scopes,
        IEnumerable<string>? contextualNamespaces = null,
        IEnumerable<string>? additionalAttributes = null)
    {
        var contextualNamespaceList = (contextualNamespaces ?? []).ToList();
        var scopeList = scopes
            .Select(scope => (
                scope.Type,
                Members: scope.Members.ToList(),
                AdditionalParameters: scope.AdditionalParameters.ToList(),
                scope.DeclaredTypeFullName,
                scope.CanImportDeclaringNamespace))
            .ToList();
        var typeRefs = scopeList
            .SelectMany(scope => CollectTypeReferences(scope.Type)
                .Concat(scope.Members.SelectMany(member =>
                    CollectMemberTypeReferences(
                        member,
                        includeSignatureAttributes: scope.Type.Kind != "delegate")))
                .Concat(scope.AdditionalParameters.SelectMany(parameter =>
                    ExtractTypeNames(parameter.Type))))
            .Select(TypeRef.TryCreate)
            .Where(r => r is not null)
            .Select(r => r!)
            .DistinctBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
        var attributeTypeRefs = scopeList
            .SelectMany(scope => CollectDeclaredAttributeTypeReferences(scope.Type.Attributes)
                .Concat(scope.Members.SelectMany(member =>
                    CollectDeclaredAttributeTypeReferences(member.Attributes, member)))
                .Concat(scope.Members.SelectMany(CollectExplicitInterfaceTypeReferences))
                .Concat(scope.AdditionalParameters.SelectMany(parameter =>
                    CollectDeclaredAttributeTypeReferences(parameter.Attributes)))
                .Concat(scope.Members
                    .Where(member => member.IsObsolete)
                    .Select(_ => "System.Obsolete")))
            .Concat(CollectDeclaredAttributeTypeReferences(additionalAttributes ?? []))
            .Select(TypeRef.TryCreate)
            .Where(r => r is not null)
            .Select(r => r!)
            .DistinctBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
        var primaryAttributeTypeRefs = scopeList
            .SelectMany(scope => scope.AdditionalParameters.SelectMany(parameter =>
                CollectDeclaredAttributeTypeReferences(parameter.Attributes)))
            .Select(TypeRef.TryCreate)
            .Where(r => r is not null)
            .Select(r => r!)
            .DistinctBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
        // Dotted signature text does not distinguish namespaces from enclosing types.
        // Newly planned delegate and primary-constructor surfaces need independent
        // namespace evidence before they can introduce a using.
        var existingSurfaceTypeFullNames = scopeList
            .SelectMany(scope =>
                scope.Type.Kind == "delegate"
                    ? []
                    : CollectTypeReferences(scope.Type)
                        .Concat(scope.Members.SelectMany(member =>
                            CollectMemberTypeReferences(
                                member,
                                includeSignatureAttributes: true))))
            .Select(TypeRef.TryCreate)
            .Where(reference => reference is not null)
            .Select(reference => reference!.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var exclusiveImportTypeFullNames = scopeList
            .SelectMany(scope =>
                (scope.Type.Kind == "delegate"
                    ? CollectTypeReferences(scope.Type)
                        .Concat(scope.Members.SelectMany(member =>
                            CollectMemberTypeReferences(
                                member,
                                includeSignatureAttributes: false)))
                    : [])
                .Concat(scope.AdditionalParameters.SelectMany(parameter =>
                    ExtractTypeNames(parameter.Type))))
            .Select(TypeRef.TryCreate)
            .Where(reference => reference is not null)
            .Select(reference => reference!.FullName)
            .ToHashSet(StringComparer.Ordinal);
        exclusiveImportTypeFullNames.ExceptWith(existingSurfaceTypeFullNames);

        var knownNamespaces = typeRefs
            .Select(typeRef => typeRef.Namespace)
            .Concat(attributeTypeRefs.Select(typeRef => typeRef.Namespace))
            .Concat(contextualNamespaceList)
            .Concat(scopeList.Select(scope => scope.Type.Namespace))
            .Where(ns => !string.IsNullOrWhiteSpace(ns))
            .Select(ns => ns!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var declaredTypeNames = new HashSet<string>(StringComparer.Ordinal);
        var uniquelyImportableDeclaredTypeFullNames = scopeList
            .GroupBy(
                scope => CSharpFormatter.StripArity(scope.Type.Name),
                StringComparer.Ordinal)
            .Where(group =>
            {
                var identities = group
                    .Select(scope => scope.DeclaredTypeFullName)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                return identities.Count == 1
                    && group.All(scope => scope.CanImportDeclaringNamespace);
            })
            .Select(group => group.First().DeclaredTypeFullName)
            .ToHashSet(StringComparer.Ordinal);
        var shadowingNames = new HashSet<string>(StringComparer.Ordinal);
        var rootShadowingNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopeList)
        {
            string declaredTypeName = CSharpFormatter.StripArity(scope.Type.Name);
            declaredTypeNames.Add(declaredTypeName);
            rootShadowingNames.Add(declaredTypeName);
            foreach (var knownNamespace in knownNamespaces)
            {
                AddVisibleNamespaceNames(
                    shadowingNames,
                    scope.Type.Namespace,
                    knownNamespace,
                    rootShadowingNames);
            }
            var lexicalShadowingNames = CollectShadowingNames(scope.Type, scope.Members);
            shadowingNames.UnionWith(lexicalShadowingNames);
            rootShadowingNames.UnionWith(lexicalShadowingNames);
        }

        var usings = new SortedSet<string>(StringComparer.Ordinal);
        var potentiallyImportedNamespaces = typeRefs
            .Select(typeRef => typeRef.Namespace)
            .Concat(contextualNamespaceList)
            .Concat(scopeList
                .Select(scope => scope.Type.Namespace)
                .Where(ns => !string.IsNullOrWhiteSpace(ns))
                .Select(ns => ns!))
            .ToHashSet(StringComparer.Ordinal);
        var collidingSimpleNames = CollidingSimpleNames(typeRefs);
        var unsafePrimaryAttributeNamespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attributeTypeRef in attributeTypeRefs)
        {
            if (!potentiallyImportedNamespaces.Contains(attributeTypeRef.Namespace))
                continue;

            var collidingTypeNames = new HashSet<string>(StringComparer.Ordinal)
            {
                attributeTypeRef.SimpleName
            };
            if (!attributeTypeRef.SimpleName.EndsWith("Attribute", StringComparison.Ordinal))
                collidingTypeNames.Add($"{attributeTypeRef.SimpleName}Attribute");
            bool collides = typeRefs.Any(typeRef =>
                typeRef.FullName != attributeTypeRef.FullName
                && collidingTypeNames.Contains(typeRef.SimpleName));
            if (collides)
            {
                collidingSimpleNames.UnionWith(collidingTypeNames);
                if (primaryAttributeTypeRefs.Any(primary =>
                    primary.FullName == attributeTypeRef.FullName))
                {
                    unsafePrimaryAttributeNamespaces.Add(attributeTypeRef.Namespace);
                }
            }
        }
        foreach (var attributeTypeRef in primaryAttributeTypeRefs)
        {
            if (!potentiallyImportedNamespaces.Contains(attributeTypeRef.Namespace))
                continue;

            var lookupNames = AttributeLookupNames(attributeTypeRef);
            bool collides = attributeTypeRefs.Any(other =>
                other.FullName != attributeTypeRef.FullName
                && potentiallyImportedNamespaces.Contains(other.Namespace)
                && lookupNames.Overlaps(AttributeLookupNames(other)));
            if (collides)
                unsafePrimaryAttributeNamespaces.Add(attributeTypeRef.Namespace);
        }
        var unsafeNamespaces = UnsafeNamespaces(
            typeRefs,
            shadowingNames,
            collidingSimpleNames,
            rootShadowingNames,
            declaredTypeNames,
            uniquelyImportableDeclaredTypeFullNames,
            typeRefs.Concat(attributeTypeRefs).ToList());
        unsafeNamespaces.UnionWith(unsafePrimaryAttributeNamespaces);
        var establishedNamespaces = contextualNamespaceList
            .Concat(scopeList
                .Select(scope => scope.Type.Namespace)
                .Where(ns => !string.IsNullOrWhiteSpace(ns))
                .Select(ns => ns!))
            .Concat(typeRefs
                .Where(typeRef =>
                    !exclusiveImportTypeFullNames.Contains(typeRef.FullName))
                .Select(typeRef => typeRef.Namespace))
            .ToHashSet(StringComparer.Ordinal);

        var exclusiveImportNamespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in typeRefs.GroupBy(r => r.SimpleName, StringComparer.Ordinal))
        {
            if (collidingSimpleNames.Contains(group.Key))
                continue;
            if (shadowingNames.Contains(group.Key))
                continue;
            bool importsDeclaredType = declaredTypeNames.Contains(group.Key);
            if (importsDeclaredType
                && !uniquelyImportableDeclaredTypeFullNames.Contains(group.First().FullName))
                continue;
            var ns = group.First().Namespace;
            if (ns.Length == 0)
                continue;
            if (unsafeNamespaces.Contains(ns))
                continue;
            bool requiresEstablishedNamespace =
                exclusiveImportTypeFullNames.Contains(group.First().FullName);
            if (requiresEstablishedNamespace
                && !establishedNamespaces.Contains(ns))
            {
                continue;
            }
            usings.Add(ns);
            if (importsDeclaredType
                || requiresEstablishedNamespace)
            {
                exclusiveImportNamespaces.Add(ns);
            }
        }

        var effectiveImportedNamespaces = usings
            .Concat(contextualNamespaceList)
            .Where(ns => !string.IsNullOrWhiteSpace(ns))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var exclusiveNamespace in exclusiveImportNamespaces)
        {
            if (effectiveImportedNamespaces.Any(ns =>
                !string.Equals(ns, exclusiveNamespace, StringComparison.Ordinal)))
            {
                usings.Remove(exclusiveNamespace);
            }
        }

        var referencedTypeNames = typeRefs
            .Concat(attributeTypeRefs)
            .Select(typeRef => (typeRef.Namespace, typeRef.SimpleName))
            .Distinct()
            .ToList();
        return (usings.ToList(), knownNamespaces, referencedTypeNames);

        static HashSet<string> AttributeLookupNames(TypeRef typeRef)
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                typeRef.SimpleName
            };
            if (!typeRef.SimpleName.EndsWith("Attribute", StringComparison.Ordinal))
                names.Add($"{typeRef.SimpleName}Attribute");
            return names;
        }
    }

    static IEnumerable<string> CollectParameterTypeReferences(
        ApiParameter parameter,
        bool includeAttributes = true)
    {
        if (!string.IsNullOrWhiteSpace(parameter.Type))
            foreach (var reference in ExtractTypeNames(parameter.Type))
                yield return reference;
        if (includeAttributes)
            foreach (var attribute in parameter.Attributes)
                foreach (var reference in ExtractTypeNames(StripAttributeArguments(attribute)))
                    yield return reference;
    }

    static IEnumerable<string> CollectAttributeTypeReferences(
        IEnumerable<string> attributes,
        ApiMember? member = null)
        => CollectDeclaredAttributeTypeReferences(attributes, member)
            .Concat(CollectAttributeValueTypeReferences(attributes, member));

    static IEnumerable<string> CollectDeclaredAttributeTypeReferences(
        IEnumerable<string> attributes,
        ApiMember? member = null)
    {
        foreach (var attribute in AttributeTexts(attributes, member))
        {
            foreach (var reference in ExtractTypeNames(StripAttributeArguments(attribute)))
                yield return reference;
            foreach (var reference in CollectAttributeArgumentTypeReferences([attribute]))
                yield return reference;
        }
    }

    static IEnumerable<string> CollectAttributeArgumentTypeReferences(
        IEnumerable<string> attributes,
        ApiMember? member = null)
    {
        foreach (var attribute in AttributeTexts(attributes, member))
        {
            int firstArgumentList = attribute.IndexOf('(', StringComparison.Ordinal);
            for (var index = 0; index < attribute.Length; index++)
            {
                if (IsStringLiteralStart(attribute, index))
                {
                    index = SkipStringLiteral(attribute, index) - 1;
                    continue;
                }
                if (attribute[index] == '\'')
                {
                    index = SkipCharLiteral(attribute, index) - 1;
                    continue;
                }

                bool isTypeOf = attribute.AsSpan(index).StartsWith("typeof(", StringComparison.Ordinal);
                bool isNestedCast = attribute[index] == '('
                    && index > firstArgumentList;
                if (!isTypeOf && !isNestedCast)
                    continue;

                int open = isTypeOf ? index + "typeof".Length : index;
                int close = attribute.IndexOf(')', open + 1);
                if (close < 0)
                    break;
                if (isNestedCast)
                {
                    int next = close + 1;
                    while (next < attribute.Length && char.IsWhiteSpace(attribute[next]))
                        next++;
                    // A following + or - is ambiguous with a parenthesized value
                    // expression, so preserve it rather than inventing type evidence.
                    if (next >= attribute.Length
                        || !char.IsAsciiDigit(attribute[next]))
                    {
                        continue;
                    }
                }

                foreach (var reference in ExtractTypeNames(attribute[(open + 1)..close]))
                    yield return reference;
                index = close;
            }
        }
    }

    static IEnumerable<string> CollectAttributeValueTypeReferences(
        IEnumerable<string> attributes,
        ApiMember? member = null)
    {
        foreach (var attribute in AttributeTexts(attributes, member))
        {
            int firstArgumentList = attribute.IndexOf('(', StringComparison.Ordinal);
            if (firstArgumentList < 0)
                continue;
            var recognizedReferences = CollectAttributeArgumentTypeReferences([attribute])
                .ToHashSet(StringComparer.Ordinal);
            foreach (var valueExpression in ExtractTypeNames(
                attribute[(firstArgumentList + 1)..]))
            {
                if (recognizedReferences.Contains(valueExpression))
                    continue;
                int memberSeparator = valueExpression.LastIndexOf('.');
                if (memberSeparator <= 0)
                    continue;
                string declaringType = valueExpression[..memberSeparator];
                if (declaringType.Contains('.', StringComparison.Ordinal))
                    yield return declaringType;
            }
        }
    }

    static IEnumerable<string> CollectQualificationOnlyAttributeTypeReferences(
        IEnumerable<string> memberAttributes,
        ApiMember member)
    {
        foreach (var reference in CollectDeclaredAttributeTypeReferences(memberAttributes))
            yield return reference;
        if (member.SignatureModel is not { } signature)
            yield break;

        foreach (var reference in CollectDeclaredAttributeTypeReferences(signature.ReturnAttributes))
            yield return reference;
        foreach (var accessor in signature.Accessors)
            foreach (var reference in CollectDeclaredAttributeTypeReferences(accessor.ReturnAttributes))
                yield return reference;
        foreach (var parameter in signature.Parameters)
            foreach (var reference in CollectAttributeArgumentTypeReferences(parameter.Attributes))
                yield return reference;
    }

    static IEnumerable<string> AttributeTexts(
        IEnumerable<string> attributes,
        ApiMember? member)
    {
        foreach (var attribute in attributes)
            yield return attribute;
        if (member?.SignatureModel is not { } signature)
            yield break;

        foreach (var attribute in signature.ReturnAttributes)
            yield return attribute;
        foreach (var parameter in signature.Parameters)
            foreach (var attribute in parameter.Attributes)
                yield return attribute;
        foreach (var accessor in signature.Accessors)
            foreach (var attribute in accessor.ReturnAttributes)
                yield return attribute;
    }

    static string AddPrimaryConstructorParameters(
        ApiType type,
        string declaration,
        CSharpDeclarationOptions options,
        IReadOnlyList<ApiParameter> parameters)
    {
        if (parameters.Count == 0)
            return declaration;
        string declarationWithoutAttributes = RenderTypeDeclarationCore(
            type,
            options with { IncludeCustomAttributes = false });
        if (!declaration.EndsWith(declarationWithoutAttributes, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"C# type declaration for '{type.FullName}' has an unexpected attribute prefix.");
        }

        string parameterList = $"({string.Join(", ", parameters.Select(parameter =>
            FormatParameter(parameter, options.IncludeSignatureAttributes)))})";
        int constraints = declarationWithoutAttributes.IndexOf(" where ", StringComparison.Ordinal);
        string head = constraints >= 0
            ? declarationWithoutAttributes[..constraints]
            : declarationWithoutAttributes;
        string tail = constraints >= 0 ? declarationWithoutAttributes[constraints..] : "";
        int inheritance = head.IndexOf(" : ", StringComparison.Ordinal);
        string withParameters = inheritance >= 0
            ? head[..inheritance] + parameterList + head[inheritance..] + tail
            : $"{head}{parameterList}{tail}";
        return declaration[..^declarationWithoutAttributes.Length] + withParameters;
    }

    static HashSet<string> CollectShadowingNames(
        ApiType type,
        IEnumerable<ApiMember> members)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeParameter in type.TypeParameters)
            names.Add(typeParameter.Name);
        foreach (var member in members)
        {
            if (member.SignatureModel is { } signature)
                foreach (var typeParameter in signature.TypeParameters)
                    names.Add(typeParameter.Name);

            foreach (var name in RawSignatureGenericParameterNames(member))
                names.Add(name);
        }
        return names;
    }

    static void AddVisibleNamespaceNames(
        HashSet<string> names,
        string? containingNamespace,
        string? knownNamespace,
        HashSet<string>? shadowedGlobalRoots = null)
    {
        if (string.IsNullOrWhiteSpace(knownNamespace))
            return;
        var knownSegments = knownNamespace.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (knownSegments.Length == 0)
            return;
        names.Add(knownSegments[0]);

        if (string.IsNullOrWhiteSpace(containingNamespace))
            return;
        var containingSegments = containingNamespace.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int sharedLength = Math.Min(containingSegments.Length, knownSegments.Length - 1);
        for (var i = 0; i < sharedLength; i++)
        {
            if (!string.Equals(
                containingSegments[i],
                knownSegments[i],
                StringComparison.Ordinal))
            {
                break;
            }
            names.Add(knownSegments[i + 1]);
            shadowedGlobalRoots?.Add(knownSegments[i + 1]);
        }
    }

    static HashSet<string> CollidingSimpleNames(IReadOnlyList<TypeRef> typeRefs)
        => typeRefs
            .GroupBy(r => r.SimpleName, StringComparer.Ordinal)
            .Where(g => g.Select(r => r.FullName).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    static string NamespaceRoot(string ns)
    {
        int separator = ns.IndexOf('.');
        return separator < 0 ? ns : ns[..separator];
    }

    static HashSet<string> UnsafeNamespaces(
        IReadOnlyList<TypeRef> typeRefs,
        IReadOnlySet<string> shadowingNames,
        IReadOnlySet<string> collidingSimpleNames,
        IReadOnlySet<string> rootShadowingNames,
        IReadOnlySet<string>? declaredTypeNames = null,
        IReadOnlySet<string>? uniquelyImportableDeclaredTypeFullNames = null,
        IReadOnlyList<TypeRef>? bindingEvidence = null)
    {
        declaredTypeNames ??= new HashSet<string>(StringComparer.Ordinal);
        uniquelyImportableDeclaredTypeFullNames ??= new HashSet<string>(StringComparer.Ordinal);
        var unsafeNamespaces = typeRefs
            .Where(r => collidingSimpleNames.Contains(r.SimpleName)
                || shadowingNames.Contains(r.SimpleName)
                || (declaredTypeNames.Contains(r.SimpleName)
                    && !uniquelyImportableDeclaredTypeFullNames.Contains(r.FullName))
                || rootShadowingNames.Contains(NamespaceRoot(r.Namespace)))
            .Select(r => r.Namespace)
            .ToHashSet(StringComparer.Ordinal);

        var referencedFullNames = (bindingEvidence ?? typeRefs)
            .Select(r => r.FullName)
            .ToHashSet(StringComparer.Ordinal);
        unsafeNamespaces.UnionWith(typeRefs
            .Where(r => referencedFullNames.Contains(r.Namespace))
            .Select(r => r.Namespace));

        return unsafeNamespaces;
    }

    static string ComposeUnit(IReadOnlyList<string> bodyLines, IReadOnlyList<string> usings, CSharpDeclarationOptions options)
    {
        var sb = new StringBuilder();
        foreach (var ns in usings)
            sb.AppendLf($"using {EscapeNamespace(ns)};");

        if (usings.Count > 0)
            sb.AppendLf();

        if (options.NamespaceMode == CSharpNamespaceMode.FileScoped
            && !string.IsNullOrWhiteSpace(options.ContainingNamespace))
        {
            sb.AppendLf($"namespace {EscapeNamespace(options.ContainingNamespace)};");
            sb.AppendLf();
        }

        foreach (var line in bodyLines)
            sb.AppendLf(line);

        return sb.ToString().TrimEnd();
    }

    static string RenderTypeDeclarationCore(ApiType type, CSharpDeclarationOptions options)
    {
        var parts = new List<string> { TypeAccessibility(type) };
        if (type.Kind is "class" or "record")
        {
            if (type.IsStatic)
                parts.Add("static");
            else
            {
                if (type.IsAbstract) parts.Add("abstract");
                if (type.IsSealed) parts.Add("sealed");
            }
        }
        else if (type.Kind == "struct")
        {
            if (type.IsReadOnly) parts.Add("readonly");
            if (type.IsByRefLike) parts.Add("ref");
        }

        parts.Add(type.Kind == "enum" ? "enum" : type.Kind);
        parts.Add(FormatTypeDisplayName(type, options));
        var declaration = string.Join(" ", parts);

        var bases = new List<string>();
        if (EnumUnderlyingBase(type) is { } enumUnderlyingBase)
            bases.Add(enumUnderlyingBase);
        else if (type.BaseType is { } baseType
                 && baseType is not ("System.Object" or "object" or "System.ValueType" or "System.Enum"))
            bases.Add(EscapeKnownIdentifiers(baseType, type.TypeParameters.Select(p => p.Name)));
        bases.AddRange(type.Interfaces.Select(iface => EscapeKnownIdentifiers(iface, type.TypeParameters.Select(p => p.Name))));

        if (bases.Count > 0)
            declaration += " : " + string.Join(", ", bases);

        declaration = AppendTypeParameterConstraints(declaration, type.TypeParameters);
        if (!options.IncludeCustomAttributes || type.Attributes.Count == 0)
            return declaration;
        return string.Join("\n", type.Attributes.Select(attribute => $"[{attribute}]"))
            + "\n" + declaration;
    }

    static bool NeedsTerminator(string declaration)
        => !declaration.EndsWith(';') && !declaration.EndsWith('}');

    static string RenderMemberDeclarationCore(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions options,
        IReadOnlyList<string>? methodParameters = null)
    {
        string signature;
        var renderedFromModel = false;
        if (member.Kind == "field" && member.Signature == null && !string.IsNullOrWhiteSpace(member.ReturnType))
        {
            signature = $"{member.ReturnType} {member.Name}";
        }
        else if (TryRenderSignatureModel(type, member, options, methodParameters, out var modelSignature))
        {
            signature = modelSignature;
            renderedFromModel = true;
        }
        else
        {
            if (!options.IncludeSignatureAttributes
                && !CanSafelySuppressCompatibilitySignatureAttributes(member))
            {
                throw new NotSupportedException(
                    $"Member '{member.Name}' requires compatibility signature text, "
                    + "whose signature attributes cannot be suppressed safely.");
            }
            signature = member.Signature ?? member.ReturnType ?? "";
        }
        if (string.IsNullOrWhiteSpace(signature))
        {
            if (member.Kind == "field" && !string.IsNullOrWhiteSpace(member.ReturnType))
                signature = $"{member.ReturnType} {member.Name}";
            else
                return "";
        }

        if (options.AbbreviateSignature)
            signature = AbbreviateSignature(signature);
        signature = EscapeKnownIdentifiers(signature, type.TypeParameters.Concat(member.SignatureModel?.TypeParameters ?? []).Select(p => p.Name));

        if (member.Name == ".cctor")
        {
            signature = $"{FormatConstructorTypeName(type, options)}()";
        }
        else if (member.IsFinalizer && !options.SuppressFinalizerSpelling)
        {
            signature = $"~{FormatConstructorTypeName(type, options)}()";
        }
        else if (member.Kind == "constructor")
        {
            var typeName = FormatConstructorTypeName(type, options);
            signature = $"{typeName}{FormatConstructorCall(signature)}";
        }
        else if (member.Name.StartsWith("op_", StringComparison.Ordinal))
        {
            signature = FormatOperatorSignature(signature, member.Name);
        }
        else if (member.Kind is "method" or "extension-method" or "explicit-interface-implementation"
            && !IsExplicitInterfaceEvent(member))
        {
            if (methodParameters is { Count: > 0 })
                signature = AddMethodGenericParameters(signature, member.Name, methodParameters);

            // TryRenderSignatureModel appends the `where` clauses itself, so only the
            // text fallback has to recover them — and it is reached two different ways.
            // A caller that supplies its own generic-parameter names makes the model
            // path decline (the single-member view), and so does a member kind the
            // model path does not render (an explicit interface implementation, in the
            // whole-type view). Both used to lose the clauses, which renders a
            // constrained generic member as uncompilable C#: the declaration stops
            // stating a constraint its own signature and body still rely on.
            //
            // When the caller supplied names, they and the model's type parameters come
            // from the same GenericParameter rows in the same order, so the two line up
            // by construction; requiring equal arity keeps a mismatched pairing from
            // spelling a clause for the wrong parameter. When the caller supplied none
            // there is nothing to pair with, and the model's list stands alone.
            if (!renderedFromModel
                && member.SignatureModel?.TypeParameters is { Count: > 0 } modelTypeParameters
                && (methodParameters is not { Count: > 0 } || methodParameters.Count == modelTypeParameters.Count))
            {
                signature = AppendMemberTypeParameterConstraints(signature, member, modelTypeParameters);
            }
            if (member.IsExtension)
                signature = AddExtensionThisModifier(signature);
            signature = EscapeMemberNameInSignature(signature, member.Name);
        }
        else if (IsEvent(member) && !signature.StartsWith("event ", StringComparison.Ordinal))
        {
            signature = $"event {signature}";
        }
        if (member.Kind is "property" or "field" or "event" || IsExplicitInterfaceEvent(member))
        {
            signature = EscapeMemberNameInSignature(signature, member.Name);
        }

        signature = EscapeQualifiedKeywordSegments(
            signature,
            preserveQualifiedIndexerKeyword: IsExplicitInterfaceProperty(member)
                && member.SignatureModel?.MemberName == "this[]");
        // Parameter names from SignatureModel are escaped in FormatParameter.
        // Re-lexing the composed string is only for compatibility text.
        if (!options.AbbreviateSignature && !renderedFromModel)
            signature = EscapeParameterLists(signature);

        List<string> attributeLines = [];
        if (options.IncludeCustomAttributes)
        {
            foreach (var attribute in member.Attributes)
                attributeLines.Add($"[{attribute}]");
        }
        if (options.IncludeObsoleteAttribute && member.IsObsolete)
            attributeLines.Add(FormatObsoleteAttribute(member.ObsoleteMessage));
        if (options.IncludeSignatureAttributes
            && member.SignatureModel?.ReturnAttributes is { Count: > 0 } returnAttributes)
            attributeLines.Add($"[return: {string.Join(", ", returnAttributes)}]");

        List<string> parts = [];
        List<string> modifiers = [];
        if (member.Name == ".cctor")
        {
            modifiers.Add("static");
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }
        else if (member.IsFinalizer)
        {
            // A finalizer carries no accessibility or override modifiers. In the
            // destructor spelling (`~Type()`) only `unsafe` is legal. In the
            // suppressed fallback (literal `void Finalize()`, used when the
            // recovered body did not reconstruct the destructor scaffold), adding
            // `public`/`virtual` would misrepresent it as a new virtual slot
            // (CS0465) rather than the object-finalizer override it is, so keep
            // the fallback modifier-free too.
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }
        else if (member.Kind != "explicit-interface-implementation")
        {
            var omitInterfaceModifiers = options.OmitInterfaceMemberModifiers
                && type.Kind == "interface"
                && member.Kind == "method";
            modifiers.Add(member.Accessibility ?? "public");
            if (member.IsConst)
                modifiers.Add("const");
            else if (member.IsStatic && !omitInterfaceModifiers)
                modifiers.Add("static");
            if (!member.IsConst && member.IsReadOnly)
                modifiers.Add("readonly");
            if (!omitInterfaceModifiers)
            {
                if (member.IsSealed)
                    modifiers.Add("sealed");
                if (member.IsAbstract)
                    modifiers.Add("abstract");
                if (member.IsOverride)
                    modifiers.Add("override");
                else if (!member.IsAbstract && member.IsVirtual && !member.IsStatic)
                    modifiers.Add("virtual");
            }
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }
        else
        {
            // Explicit interface implementations omit the access modifier but must still
            // carry `static` (C# 11 static-abstract interface members implemented explicitly)
            // and `unsafe`. Order mirrors the .cctor branch: static then unsafe.
            if (member.IsStatic)
                modifiers.Add("static");
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }

        if ((options.ForceAsync || member.IsAsync)
            && !member.IsFinalizer
            && member.Kind is "method" or "extension-method" or "explicit-interface-implementation")
            modifiers.Add("async");

        if (modifiers.Count > 0)
            parts.Add(string.Join(" ", modifiers));
        parts.Add(signature);

        string declarationLine = string.Join(" ", parts);
        if (attributeLines.Count == 0)
            return declarationLine;

        // On the opt-in surfaces that emit custom attributes (annotated source and
        // the full type printer) each leading attribute goes on its own line, as is
        // idiomatic C#. Single-line/table contexts (which never emit custom
        // attributes) keep obsolete/return attributes inline so the row stays intact.
        string separator = options.IncludeCustomAttributes ? "\n" : " ";
        return string.Join(separator, attributeLines) + separator + declarationLine;
    }

    static bool CanSafelySuppressCompatibilitySignatureAttributes(ApiMember member)
        => member.SignatureModel is { } model
            && model.ReturnAttributes.Count == 0
            && model.Parameters.All(parameter =>
                parameter.Attributes.Count == 0
                && (!parameter.HasDefault
                    || !string.IsNullOrWhiteSpace(parameter.DefaultValueText)))
            && model.Accessors.All(accessor => accessor.ReturnAttributes.Count == 0);

    static IEnumerable<string> CollectTypeReferences(ApiType type)
    {
        if (type.BaseType is { Length: > 0 })
        {
            foreach (var reference in ExtractTypeNames(type.BaseType))
                yield return reference;
        }
        foreach (var iface in type.Interfaces)
        {
            foreach (var reference in ExtractTypeNames(iface))
                yield return reference;
        }
        foreach (var typeParameter in type.TypeParameters)
        {
            foreach (var constraint in typeParameter.Constraints)
            {
                foreach (var reference in ExtractTypeNames(constraint))
                    yield return reference;
            }
        }
    }

    static IEnumerable<string> CollectMemberTypeReferences(
        ApiMember member,
        bool includeSignatureAttributes = true)
    {
        foreach (var expression in MemberTypeExpressions(member, includeSignatureAttributes))
        {
            foreach (var reference in ExtractTypeNames(expression))
                yield return reference;
        }
    }

    static IEnumerable<string> CollectExplicitInterfaceTypeReferences(ApiMember member)
    {
        if (member.Kind != "explicit-interface-implementation")
            yield break;

        int memberSeparator = member.Name.LastIndexOf('.');
        if (memberSeparator > 0)
        {
            foreach (var reference in ExtractTypeNames(member.Name[..memberSeparator]))
                yield return reference;
        }
    }

    static IEnumerable<string> MemberTypeExpressions(
        ApiMember member,
        bool includeSignatureAttributes)
    {
        if (!string.IsNullOrWhiteSpace(member.ReturnType))
            yield return member.ReturnType!;
        if (member.SignatureModel is { } signatureModel)
        {
            if (!string.IsNullOrWhiteSpace(signatureModel.ReturnType))
                yield return signatureModel.ReturnType!;
            foreach (var parameter in signatureModel.Parameters)
            {
                if (!string.IsNullOrWhiteSpace(parameter.Type))
                    yield return parameter.Type;
                if (includeSignatureAttributes)
                    foreach (var attribute in parameter.Attributes)
                        yield return StripAttributeArguments(attribute);
            }
            foreach (var typeParameter in signatureModel.TypeParameters)
                foreach (var constraint in typeParameter.Constraints)
                    foreach (var reference in ExtractTypeNames(constraint))
                        yield return reference;
        }

        var signature = member.Signature;
        if (string.IsNullOrWhiteSpace(signature))
            yield break;

        if (member.Kind == "property")
        {
            foreach (var expression in PropertyTypeExpressions(signature))
                yield return expression;
            yield break;
        }

        if (member.Kind == "field" || member.Kind == "event")
        {
            if (TryTypeBeforeName(signature, member.Name, out var type))
                yield return type;
            yield break;
        }

        var parenStart = signature.IndexOf('(');
        var parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd < parenStart)
            yield break;

        if (member.Kind != "constructor")
        {
            var prefix = signature[..parenStart].TrimEnd();
            var name = member.Name;
            var nameIndex = prefix.LastIndexOf(name, StringComparison.Ordinal);
            if (nameIndex > 0)
                yield return prefix[..nameIndex].TrimEnd();
        }

        foreach (var parameter in SplitTopLevel(signature[(parenStart + 1)..parenEnd]))
        {
            if (TryParameterType(parameter, out var parameterType))
                yield return parameterType;
        }
    }

    static IEnumerable<string> RawSignatureGenericParameterNames(ApiMember member)
    {
        var signature = member.Signature;
        if (string.IsNullOrWhiteSpace(signature))
            yield break;
        if (member.Kind is "property" or "field" or "event" or "constructor")
            yield break;

        // The generic method parameter list is `Name<...>(` — the method name (a whole
        // token) immediately followed by an angle-bracket group and then the parameter
        // list. Anchor on that shape rather than on the first '(' in the signature, so a
        // tuple or generic return type (whose own '(' / '<' come first) does not fool the
        // parser. Scan every occurrence of the name to skip matches inside the return
        // type (e.g. `TaskRun Run<T>()` or `Run<U> Run<T>()`).
        var name = member.Name;
        if (name.Length == 0)
            yield break;

        var searchStart = 0;
        while (searchStart <= signature.Length - name.Length)
        {
            var nameIndex = signature.IndexOf(name, searchStart, StringComparison.Ordinal);
            if (nameIndex < 0)
                yield break;
            searchStart = nameIndex + 1;

            if (nameIndex > 0 && (char.IsLetterOrDigit(signature[nameIndex - 1]) || signature[nameIndex - 1] == '_'))
                continue;

            var j = nameIndex + name.Length;
            while (j < signature.Length && char.IsWhiteSpace(signature[j]))
                j++;
            if (j >= signature.Length || signature[j] != '<')
                continue;

            var depth = 0;
            var start = j + 1;
            var end = -1;
            for (var i = j; i < signature.Length; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }
            if (end < 0)
                continue;

            var afterBracket = end + 1;
            while (afterBracket < signature.Length && char.IsWhiteSpace(signature[afterBracket]))
                afterBracket++;
            if (afterBracket >= signature.Length || signature[afterBracket] != '(')
                continue;

            foreach (var part in SplitTopLevel(signature[start..end]))
            {
                var parameterName = part.Trim();
                var space = parameterName.IndexOf(' ');
                if (space >= 0)
                    parameterName = parameterName[(space + 1)..].Trim();
                if (parameterName.Length > 0)
                    yield return parameterName;
            }
            yield break;
        }
    }

    static IEnumerable<string> PropertyTypeExpressions(string signature)
    {
        if (signature.StartsWith("required ", StringComparison.Ordinal))
            signature = signature["required ".Length..];

        var indexerStart = signature.IndexOf("this[", StringComparison.Ordinal);
        if (indexerStart > 0)
        {
            yield return signature[..indexerStart].TrimEnd();
            var close = signature.IndexOf(']', indexerStart);
            if (close > indexerStart)
            {
                foreach (var parameter in SplitTopLevel(signature[(indexerStart + "this[".Length)..close]))
                {
                    if (TryParameterType(parameter, out var parameterType))
                        yield return parameterType;
                }
            }
            yield break;
        }

        var brace = signature.IndexOf('{');
        var head = brace >= 0 ? signature[..brace].TrimEnd() : signature.TrimEnd();
        var lastSpace = LastTopLevelSpace(head);
        if (lastSpace > 0)
            yield return head[..lastSpace].TrimEnd();
    }

    static bool TryTypeBeforeName(string signature, string name, out string type)
    {
        type = "";
        var nameIndex = signature.LastIndexOf(name, StringComparison.Ordinal);
        if (nameIndex <= 0)
            return false;
        type = signature[..nameIndex].TrimEnd();
        if (type.StartsWith("event ", StringComparison.Ordinal))
            type = type["event ".Length..];
        return type.Length > 0;
    }

    static bool TryParameterType(string parameter, out string type)
    {
        type = "";
        parameter = StripDefaultValue(parameter).Trim();
        parameter = StripLeadingAttributes(parameter).Trim();

        bool changed;
        do
        {
            changed = false;
            foreach (var modifier in s_parameterModifiers)
            {
                if (parameter.StartsWith(modifier + " ", StringComparison.Ordinal))
                {
                    parameter = parameter[(modifier.Length + 1)..].TrimStart();
                    changed = true;
                }
            }
        } while (changed);

        var lastSpace = LastTopLevelSpace(parameter);
        if (lastSpace <= 0)
            return false;

        type = parameter[..lastSpace].TrimEnd();
        return type.Length > 0;
    }

    static string StripDefaultValue(string parameter)
    {
        var depth = 0;
        for (var i = 0; i < parameter.Length; i++)
        {
            var c = parameter[i];
            if (c is '<' or '[' or '(') depth++;
            else if (c is '>' or ']' or ')') depth--;
            else if (c == '=' && depth == 0)
                return parameter[..i];
        }

        return parameter;
    }

    static string StripLeadingAttributes(string parameter)
    {
        while (parameter.StartsWith('['))
        {
            var close = Matching(parameter, 0, '[', ']');
            if (close < 0)
                return parameter;
            parameter = parameter[(close + 1)..].TrimStart();
        }

        return parameter;
    }

    // Attribute usages carry a constructor argument list whose contents are value
    // expressions (enum member accesses like UnmanagedType.I4, consts, typeof), not
    // type references. Treating those dotted value expressions as type names would
    // derive bogus namespaces (e.g. `using System.Runtime.InteropServices.UnmanagedType;`)
    // and mis-shorten the argument, so only the attribute type name (before the
    // constructor argument list) participates in reference extraction and using derivation.
    static string StripAttributeArguments(string attribute)
    {
        int paren = attribute.IndexOf('(', StringComparison.Ordinal);
        return paren < 0 ? attribute : attribute[..paren];
    }

    static IEnumerable<string> ExtractTypeNames(string expression)
    {
        foreach (var token in DottedIdentifierTokens(expression))
        {
            if (token.StartsWith("global.", StringComparison.Ordinal)
                || token.StartsWith("global::", StringComparison.Ordinal))
            {
                continue;
            }

            string normalized = token.StartsWith('@') ? token[1..] : token;
            yield return normalized
                .Replace(".@", ".", StringComparison.Ordinal)
                .Replace("+@", "+", StringComparison.Ordinal);
        }
    }

    static IEnumerable<string> DottedIdentifierTokens(string text)
    {
        for (var i = 0; i < text.Length;)
        {
            if (IsStringLiteralStart(text, i))
            {
                i = SkipStringLiteral(text, i);
                continue;
            }
            if (text[i] == '\'')
            {
                i = SkipCharLiteral(text, i);
                continue;
            }
            if (!IsIdentifierStart(text[i])
                && (text[i] != '@'
                    || i + 1 >= text.Length
                    || !IsIdentifierStart(text[i + 1])))
            {
                i++;
                continue;
            }

            var token = new StringBuilder();
            bool yieldedConstructedRoot = false;
            while (i < text.Length)
            {
                int segmentStart = i++;
                while (i < text.Length && IsIdentifierPart(text[i]))
                    i++;
                token.Append(text.AsSpan(segmentStart, i - segmentStart));

                if (i < text.Length && text[i] == '<')
                {
                    if (!yieldedConstructedRoot)
                    {
                        yield return token.ToString();
                        yieldedConstructedRoot = true;
                    }
                    int close = MatchingAngleBracket(text, i);
                    if (close < 0)
                        break;
                    foreach (string nested in DottedIdentifierTokens(text[(i + 1)..close]))
                        yield return nested;
                    i = close + 1;
                }

                if (i + 1 >= text.Length
                    || text[i] is not ('.' or '+')
                    || (!IsIdentifierStart(text[i + 1]) && text[i + 1] != '@'))
                {
                    break;
                }

                token.Append(text[i++]);
            }
            if (!yieldedConstructedRoot)
                yield return token.ToString();
        }
    }

    static int MatchingAngleBracket(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '<')
                depth++;
            else if (text[i] == '>' && --depth == 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Appends a method's <c>where</c> clauses. An <c>override</c> and an explicit
    /// interface implementation inherit their constraints and mostly may not restate
    /// them (CS0460) — but C# carves out exactly one exception, and it is load-bearing
    /// rather than cosmetic: a bare <c>class</c> or <c>struct</c> constraint *may* be
    /// restated, and it is what decides how <c>T?</c> binds. Dropping it silently
    /// rewrites <c>T?</c> from a nullable reference type to <see cref="System.Nullable{T}"/>
    /// (or the reverse), so those two are reduced to their legal spelling and kept.
    /// Constraints outside that pair are omitted here; see
    /// <see cref="AppendInheritedConstraintRestatement"/> for the cases that leaves open.
    /// </summary>
    /// <remarks>
    /// Both member call sites (the <see cref="ApiSignature"/> renderer and the text
    /// path a caller-supplied generic-parameter list forces) route through here, so
    /// the two cannot disagree about which clauses are legal. The type-declaration
    /// call site does not: a type always owns, and so always restates, its own
    /// constraints.
    /// </remarks>
    static string AppendMemberTypeParameterConstraints(
        string declaration,
        ApiMember member,
        IReadOnlyList<TypeParameter> typeParameters)
        => member.IsOverride || member.Kind == "explicit-interface-implementation"
            ? AppendInheritedConstraintRestatement(declaration, typeParameters)
            : AppendTypeParameterConstraints(declaration, typeParameters);

    /// <summary>
    /// Restates what C# permits on a member that inherits its constraints. The
    /// inherited constraints themselves may not be repeated -- that is CS0460 -- but
    /// exactly one fact about each type parameter must be, because it decides whether
    /// <c>T?</c> in the signature binds as a nullable reference type or as
    /// Nullable&lt;T&gt;: whether the parameter is known to be a reference type
    /// (<c>class</c>), known to be a value type (<c>struct</c>), or neither
    /// (<c>default</c>).
    /// </summary>
    /// <remarks>
    /// Every clause emitted here was compiled against csc as a restatement on an
    /// override, and the reduction is gated by
    /// <c>OverrideGenericMethod_RestatesWhatTheClassifiedKindRequires</c> plus the
    /// real-artifact canaries in <c>ApiOutputFormatterTests</c>. Note that the fact
    /// cannot be recovered from the constraint spelling: <c>System.Enum</c> is a class
    /// yet requires <c>default</c>, while any other named class requires <c>class</c>,
    /// so a name-based guess gets that row backwards. <see cref="TypeParameter.TypeKind"/>
    /// carries the classified answer instead, decided in Metadata where the constraint
    /// type can actually be resolved. When Metadata could not classify it the clause is
    /// omitted, which renders exactly as this member did before the rule existed --
    /// guessing would be CS8822 or CS8665 rather than merely incomplete.
    /// </remarks>
    static string AppendInheritedConstraintRestatement(
        string declaration,
        IReadOnlyList<TypeParameter> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        {
            if (RestatableConstraint(typeParameter) is not { } keyword)
                continue;

            declaration += $" where {SanitizeIdentifier(typeParameter.Name)} : {keyword}";
        }

        return declaration;
    }

    /// <summary>
    /// The single keyword an inheriting member must restate for one type parameter, or
    /// null when Metadata could not classify it and no clause can be emitted safely.
    /// </summary>
    static string? RestatableConstraint(TypeParameter typeParameter)
        => typeParameter.TypeKind switch
        {
            // The bare keyword, never the annotated `class?` metadata records: the
            // annotated form is itself CS0460 here.
            TypeParameterTypeKind.ReferenceType => "class",
            TypeParameterTypeKind.ValueType => "struct",

            // Nothing is proven about T, so `T?` in the signature would bind to
            // Nullable<T> unless the override says otherwise. `default` is the only
            // spelling that says it, and it is legal only on a member that inherits its
            // constraints -- which is the only place this runs.
            TypeParameterTypeKind.NeitherReferenceNorValue => "default",

            // A constraint type this assembly could not classify. Both concrete answers
            // are compile errors when guessed wrong (CS8822 restating `default` against
            // a reference type, CS8665 restating `class` against System.Enum), so the
            // clause is omitted and the render stays as it was before #3721.
            _ => null,
        };

    static string AppendTypeParameterConstraints(string declaration, IReadOnlyList<TypeParameter> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        {
            if (typeParameter.Constraints.Count == 0)
                continue;

            declaration += $" where {SanitizeIdentifier(typeParameter.Name)} : {FormatConstraintList(typeParameter, typeParameters.Select(p => p.Name))}";
        }

        return declaration;
    }

    /// <summary>
    /// Renders the constraint list that follows <c>where X : </c> for one type
    /// parameter, escaping reserved-keyword identifiers inside constraint type names
    /// while emitting special-constraint keywords verbatim. Uses
    /// <see cref="TypeParameter.StructuredConstraints"/> for the keyword/type-name
    /// distinction when available; otherwise falls back to a token heuristic that
    /// cannot disambiguate a type literally named like a constraint keyword.
    /// </summary>
    /// <remarks>
    /// The result is contained before it is returned. A constraint entry is a type
    /// name out of metadata, so it is untrusted, and keyword escaping is not
    /// containment: it changes <c>class</c> to <c>@class</c> and leaves a bidi
    /// override or a line terminator exactly where it was. Adversarial review of
    /// issue #3319 found a hostile interface name reaching the terminal raw through
    /// this list while the type parameter beside it was already contained, so the
    /// rendered row was half guarded. Containment goes here rather than at the call
    /// sites because this method is what composes the untrusted text into a single
    /// display string, and its two callers would otherwise each have to remember.
    /// </remarks>
    internal static string FormatConstraintList(TypeParameter typeParameter, IEnumerable<string> parameterNames)
    {
        var parts = typeParameter.StructuredConstraints is { } structured
            ? structured.Select(entry => entry.IsTypeName ? EscapeReservedKeywordIdentifiers(entry.Value) : entry.Value)
            : typeParameter.Constraints.Select(SpellConstraint);
        return CSharpIdentifierCore.ContainComposedName(
            EscapeKnownIdentifiers(string.Join(", ", parts), parameterNames));
    }

    // Fallback used only when structured constraint kinds are unavailable: a
    // constraint entry equal to a special-constraint keyword is emitted verbatim,
    // otherwise it is treated as a type name subject to reserved-keyword escaping.
    // This cannot disambiguate a type literally named like a keyword (e.g. a global
    // type named "class"); producers that populate StructuredConstraints avoid it.
    static string SpellConstraint(string constraint)
        => s_specialConstraintKeywords.Contains(constraint)
            ? constraint
            : EscapeReservedKeywordIdentifiers(constraint);

    static string EscapeReservedKeywordIdentifiers(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (IsIdentifierStart(text[i]))
            {
                int start = i++;
                while (i < text.Length && IsIdentifierPart(text[i]))
                    i++;
                string token = text[start..i];
                bool alreadyEscaped = start > 0 && text[start - 1] == '@';
                sb.Append(!alreadyEscaped && CSharpKeywords.RequiresDeclarationEscape(token) ? EscapeIdentifier(token) : token);
                continue;
            }
            sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    static bool TryRenderSignatureModel(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions options,
        IReadOnlyList<string>? methodParameters,
        out string signature)
    {
        signature = "";
        if (member.SignatureModel is not { } model)
            return false;

        // Compatibility text may still carry metadata-only default attributes
        // that have not been projected into the structured parameter shape.
        if (model.Parameters.Any(static parameter =>
                parameter.HasDefault
                && string.IsNullOrWhiteSpace(parameter.DefaultValueText)
                && !HasStructuredMetadataOnlyDefault(parameter)))
        {
            return false;
        }
        if (!options.IncludeSignatureAttributes
            && model.Parameters.Any(static parameter =>
                parameter.HasDefault
                && string.IsNullOrWhiteSpace(parameter.DefaultValueText)))
        {
            return false;
        }

        var parameters = string.Join(
            ", ",
            model.Parameters.Select(parameter =>
                FormatParameter(parameter, options.IncludeSignatureAttributes)));
        if (member.Name == ".cctor")
        {
            signature = $"{FormatConstructorTypeName(type, options)}()";
            return true;
        }

        if (member.Kind == "constructor")
        {
            signature = $"{FormatConstructorTypeName(type, options)}({parameters})";
            return true;
        }
        if (member.Kind == "method"
            && methodParameters is not { Count: > 0 }
            && model.MemberName is { Length: > 0 } memberName
            && model.ReturnType is { Length: > 0 } returnType)
        {
            if (memberName.Contains('<', StringComparison.Ordinal) && model.TypeParameters.Count == 0)
                return false;
            signature = AppendMemberTypeParameterConstraints($"{returnType} {memberName}({parameters})", member, model.TypeParameters);
            return true;
        }
        if ((member.Kind == "property" || IsExplicitInterfaceProperty(member))
            && model.ReturnType is { Length: > 0 } propertyType
            && model.Accessors.Count > 0
            && (member.Kind == "explicit-interface-implementation"
                || IsOrdinaryPropertyName(member.Name)
                    && IsOrdinaryPropertyName(model.MemberName)))
        {
            var head = model.IsRequired ? $"required {propertyType}" : propertyType;
            var propertyMemberName = model.MemberName == "this[]"
                ? IsExplicitInterfaceProperty(member)
                    ? $"{member.Name[..(member.Name.LastIndexOf('.') + 1)]}this[{parameters}]"
                    : $"this[{parameters}]"
                : string.IsNullOrWhiteSpace(model.MemberName)
                    ? member.Name
                    : model.MemberName!;
            signature = options.OmitPropertyAccessors
                ? $"{head} {propertyMemberName}"
                : $"{head} {propertyMemberName} {{ {string.Join(" ", model.Accessors.Select(accessor => AccessorDeclaration(accessor, options.IncludeSignatureAttributes)))} }}";
            return true;
        }
        if ((member.Kind == "event" || IsExplicitInterfaceEvent(member))
            && model.ReturnType is { Length: > 0 } eventType
            && (IsExplicitInterfaceEvent(member)
                || IsOrdinaryPropertyName(member.Name)
                    && IsOrdinaryPropertyName(model.MemberName)))
        {
            var eventMemberName = string.IsNullOrWhiteSpace(model.MemberName)
                ? member.Name
                : model.MemberName!;
            signature = $"{eventType} {eventMemberName}";
            return true;
        }

        // Keep extension projections, explicit implementations, operators, and
        // unsupported event shapes on compatibility text until the remaining
        // declaration-level facts are represented in ApiSignature.
        return false;

        static bool HasStructuredMetadataOnlyDefault(ApiParameter parameter)
            => parameter.Attributes.Any(static attribute =>
                    attribute == "System.Runtime.InteropServices.Optional")
                && parameter.Attributes.Any(static attribute =>
                    attribute.StartsWith(
                        "System.Runtime.CompilerServices.DateTimeConstant(",
                        StringComparison.Ordinal));

        static string AccessorDeclaration(
            ApiAccessor accessor,
            bool includeSignatureAttributes)
        {
            var attributePrefix = !includeSignatureAttributes
                || accessor.ReturnAttributes.Count == 0
                ? ""
                : $"[return: {string.Join(", ", accessor.ReturnAttributes)}] ";
            return string.IsNullOrWhiteSpace(accessor.Accessibility)
                ? $"{attributePrefix}{accessor.Kind};"
                : $"{attributePrefix}{accessor.Accessibility} {accessor.Kind};";
        }

        static bool IsOrdinaryPropertyName(string? name)
            => string.IsNullOrWhiteSpace(name)
               || name == "this[]"
               || !name.Contains('.', StringComparison.Ordinal);
    }

    static bool IsExplicitInterfaceProperty(ApiMember member)
        => member.Kind == "explicit-interface-implementation"
            && member.Name.Contains('.', StringComparison.Ordinal)
            && HasOnlyAccessors(member, "get", "set", "init");

    static bool IsExplicitInterfaceEvent(ApiMember member)
        => member.Kind == "explicit-interface-implementation"
            && member.Name.Contains('.', StringComparison.Ordinal)
            && HasOnlyAccessors(member, "add", "remove");

    static bool IsEvent(ApiMember member)
        => member.Kind == "event" || IsExplicitInterfaceEvent(member);

    static bool HasOnlyAccessors(ApiMember member, params string[] kinds)
        => member.SignatureModel?.Accessors is { Count: > 0 } accessors
            && accessors.All(accessor => kinds.Contains(accessor.Kind, StringComparer.Ordinal));

    internal static string FormatParameter(
        ApiParameter parameter,
        bool includeAttributes = true)
    {
        string type = EscapeTypeKeywords(parameter.Type);
        string head = string.IsNullOrEmpty(parameter.Modifier)
            ? type
            : $"{parameter.Modifier} {type}";
        var declaration = string.IsNullOrWhiteSpace(parameter.Name)
            ? head
            : $"{head} {SanitizeIdentifier(parameter.Name)}";
        declaration = parameter.HasDefault && parameter.DefaultValueText is { Length: > 0 }
            ? $"{declaration} = {parameter.DefaultValueText}"
            : declaration;
        return !includeAttributes || parameter.Attributes.Count == 0
            ? declaration
            : $"[{string.Join(", ", parameter.Attributes)}] {declaration}";
    }

    /// <summary>
    /// Rewrites CLR primitive full names (<c>System.Int32</c>, <c>System.Boolean</c>,
    /// <c>System.IntPtr</c>, …) to their C# keyword spelling (<c>int</c>, <c>bool</c>,
    /// <c>nint</c>, …) wherever they appear as a complete type-name segment inside a
    /// type string, including nested in generics, arrays, pointers, and by-ref forms
    /// (e.g. <c>System.Collections.Generic.List&lt;System.Int32&gt;[]</c> →
    /// <c>System.Collections.Generic.List&lt;int&gt;[]</c>). Only whole dotted-name
    /// runs are considered, so a longer name that merely contains a primitive as a
    /// substring (<c>System.Int32Enum</c>, <c>A.System.Int32</c>) is left untouched,
    /// as is an explicitly-escaped identifier (<c>@System.Int32</c>).
    /// The keyword pairs are the single source of truth in
    /// <see cref="PrimitiveTypeNames"/>, so this spelling always matches the rest of
    /// the C# layer. This is the authoritative primitive-alias rewriter; consumers
    /// must not reimplement it.
    /// </summary>
    internal static string AliasPrimitiveTypeNames(string type)
    {
        if (type.IndexOf("System.", StringComparison.Ordinal) < 0)
            return type;

        var builder = new StringBuilder(type.Length);
        for (int index = 0; index < type.Length;)
        {
            if (!IsTypeNameSegmentChar(type[index]))
            {
                builder.Append(type[index++]);
                continue;
            }

            int end = index + 1;
            while (end < type.Length && IsTypeNameSegmentChar(type[end]))
                end++;

            string run = type[index..end];
            // A run immediately preceded by '@' is an explicitly-escaped identifier
            // (e.g. `@System.Int32`), not a primitive type reference; leave it as-is
            // rather than emitting a malformed `@int`.
            bool escaped = index > 0 && type[index - 1] == '@';
            builder.Append(!escaped && PrimitiveTypeNames.TryToKeyword(run, out var keyword) ? keyword : run);
            index = end;
        }
        return builder.ToString();
    }

    // A dotted type-name run: identifier characters plus the '.' segment separator.
    // Type-syntax delimiters ('<', '>', ',', '[', ']', '*', '&', whitespace) break
    // the run, so an embedded primitive full name is isolated as its own run.
    static bool IsTypeNameSegmentChar(char c)
        => char.IsLetterOrDigit(c) || c is '_' or '.';

    /// <summary>
    /// Escapes C# reserved keywords that appear as identifiers (type or namespace
    /// name segments) inside a type string, while leaving keywords that are C# type
    /// <em>syntax</em> bare (primitive aliases, parameter/type modifiers, and
    /// function-pointer syntax). This is the single authoritative type-keyword
    /// escaper for the C# layer; consumers reach it through
    /// <see cref="CSharpFormatter.EscapeTypeKeywords"/>.
    /// </summary>
    internal static string EscapeTypeKeywords(string type)
    {
        var builder = new StringBuilder(type.Length);
        for (int index = 0; index < type.Length;)
        {
            if (!IsIdentifierStart(type[index]))
            {
                builder.Append(type[index++]);
                continue;
            }

            int end = index + 1;
            while (end < type.Length && IsIdentifierPart(type[end]))
                end++;

            string identifier = type[index..end];
            bool isTypeSyntaxKeyword = IsTypeSyntaxKeyword(type, identifier, index, end);
            if ((index == 0 || type[index - 1] != '@')
                && EscapeIdentifier(identifier) != identifier
                && !isTypeSyntaxKeyword)
            {
                builder.Append('@');
            }
            builder.Append(identifier);
            index = end;
        }

        // A type string is composed from untrusted metadata names. Containment
        // happens at this single display choke point rather than at the sites
        // that spell parameters, return types, and base types, so a new caller
        // cannot reopen issue #3319. This escaper is display-only — identity
        // lives in the raw metadata names — and containment is a no-op on clean
        // text.
        return CSharpIdentifierCore.ContainComposedName(builder.ToString());
    }

    static bool IsTypeSyntaxKeyword(string type, string identifier, int start, int end)
    {
        // Primitive/void aliases are bare when they name the primitive, and escaped
        // only when they are a segment of a dotted qualified name (a type literally
        // named e.g. "int" under some namespace: "N.int" -> "N.@int"). Being
        // followed by '.' is member access, not a name segment, so it stays bare.
        if (IsPrimitiveOrVoidKeyword(identifier))
            return start == 0 || type[start - 1] != '.';

        // Function-pointer type head: "delegate*<...>" or "delegate* unmanaged<...>".
        // A real function-pointer head is "delegate*" followed (after optional
        // whitespace) by '<', or by a calling-convention run (whitespace then an
        // identifier such as "managed"/"unmanaged"). A "delegate*" followed by
        // anything else — end of string, or terminating punctuation ('[', ',', '>',
        // ')') after whitespace — is a pointer to a type literally named "delegate"
        // and must be escaped ("@delegate*"). A function-pointer head is also never a
        // qualified name segment, so a dotted "N.delegate*<...>" is a pointer to a
        // type named "delegate" and is escaped ("N.@delegate*<...>").
        if (identifier == "delegate")
        {
            if (start > 0 && type[start - 1] == '.')
                return false;
            if (end >= type.Length || type[end] != '*')
                return false;
            int afterStar = end + 1;
            if (afterStar >= type.Length)
                return false;
            if (type[afterStar] == '<')
                return true;
            if (!char.IsWhiteSpace(type[afterStar]))
                return false;
            int convStart = afterStar;
            while (convStart < type.Length && char.IsWhiteSpace(type[convStart]))
                convStart++;
            return convStart < type.Length
                && (IsIdentifierStart(type[convStart]) || type[convStart] == '<');
        }

        // Parameter/type modifiers are bare in a leading modifier run — at the start
        // of the string or of a type slot ("ref int", "ref readonly int",
        // "scoped ref int", "in long", "params byte[]"), and inside a function
        // pointer signature ("delegate*<ref int, void>", "delegate* unmanaged<...>").
        // A modifier is only type syntax when a type actually follows it: it must be
        // separated by whitespace from a following type start (identifier, '@', or a
        // tuple '('), and never immediately precedes a pointer '*' or terminating
        // punctuation. So "ref int" stays bare while "ref*"/"ref ," are pointers to /
        // uses of a type named "ref" and must be escaped ("@ref*").
        if (identifier is "ref" or "in" or "out" or "params" or "readonly" or "scoped" or "unmanaged")
        {
            if (end >= type.Length || !char.IsWhiteSpace(type[end]))
                return false;
            int typeStart = end;
            while (typeStart < type.Length && char.IsWhiteSpace(type[typeStart]))
                typeStart++;
            bool typeFollows = typeStart < type.Length
                && (IsIdentifierStart(type[typeStart]) || type[typeStart] is '@' or '(');
            return typeFollows && InModifierPosition(type, start);
        }

        return false;
    }

    static bool IsPrimitiveOrVoidKeyword(string identifier)
        => identifier is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "object" or "short" or "ushort" or "string" or "void";

    // A modifier keyword is in modifier position when everything preceding it in the
    // current type slot is only other modifiers and whitespace — i.e. walking back
    // reaches the start of the string or a slot boundary ('<', ',', '(', '*') after
    // skipping intervening modifier tokens.
    static bool InModifierPosition(string type, int start)
    {
        int i = start - 1;
        while (i >= 0)
        {
            while (i >= 0 && char.IsWhiteSpace(type[i]))
                i--;
            if (i < 0)
                return true;
            if (type[i] is '<' or ',' or '(' or '*')
                return true;

            int wordEnd = i + 1;
            while (i >= 0 && IsIdentifierPart(type[i]))
                i--;
            string word = type[(i + 1)..wordEnd];
            if (word is "ref" or "in" or "out" or "params" or "readonly" or "scoped" or "unmanaged")
                continue;
            return false;
        }
        return true;
    }

    // ApiType.Name spells a nested chain with '.', so arity is stripped per
    // component and a backtick that is not a canonical suffix stays in the name
    // (MetadataNameArity). Truncating at the first backtick dropped every
    // following component, spelling Outer`1.Inner as the unrelated type Outer.
    static string FormatTypeDisplayName(
        ApiType type,
        CSharpDeclarationOptions options)
        => options.DeclaredTypeSelfName is { } selfName
            ? type.TypeParameters.Count == 0
                ? selfName.Identifier
                : $"{selfName.Identifier}<{string.Join(", ", type.TypeParameters.Select(TypeParameterDisplayName))}>"
            : options.LegacyDeclaredTypeIdentifier is { } legacyIdentifier
                ? type.TypeParameters.Count == 0
                    ? legacyIdentifier
                    : $"{legacyIdentifier}<{string.Join(", ", type.TypeParameters.Select(TypeParameterDisplayName))}>"
            : CSharpFormatter.FormatTypeName(
                type,
                includeVariance: true);

    static string? EnumUnderlyingBase(ApiType type)
    {
        if (type.Kind != "enum" || type.EnumUnderlyingType is not { } underlying)
            return null;
        return EnumUnderlyingKeyword(underlying) is { } keyword && keyword != "int"
            ? keyword
            : null;
    }

    static string? EnumUnderlyingKeyword(string type) => type switch
    {
        "sbyte" or "System.SByte" => "sbyte",
        "byte" or "System.Byte" => "byte",
        "short" or "System.Int16" => "short",
        "ushort" or "System.UInt16" => "ushort",
        "int" or "System.Int32" => "int",
        "uint" or "System.UInt32" => "uint",
        "long" or "System.Int64" => "long",
        "ulong" or "System.UInt64" => "ulong",
        _ => null,
    };

    static string TypeParameterDisplayName(TypeParameter typeParameter)
        => typeParameter.Variance is { } variance
            ? $"{variance} {SanitizeIdentifier(typeParameter.Name)}"
            : SanitizeIdentifier(typeParameter.Name);

    static string FormatObsoleteAttribute(string? message)
        => string.IsNullOrWhiteSpace(message)
            ? "[System.Obsolete]"
            : $"[System.Obsolete(\"{EscapeCSharpString(message)}\")]";

    // The Obsolete message is attacker-controlled attribute text rendered inside a
    // C# string literal. Escaping only the classic C-escapes leaves vertical tabs,
    // ANSI escapes, and bidi overrides to reach the terminal raw (issue #3319), so
    // every remaining rendering hazard is spelled as a visible \uXXXX escape.
    static string EscapeCSharpString(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        if (!escaped.Any(CSharpIdentifier.RequiresLiteralEscape))
            return escaped;

        var builder = new StringBuilder(escaped.Length);
        foreach (var ch in escaped)
        {
            if (CSharpIdentifier.RequiresLiteralEscape(ch))
                builder.Append($"\\u{(int)ch:X4}");
            else
                builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Rewrites an <c>op_*</c> method signature into C# operator syntax.
    /// </summary>
    /// <remarks>
    /// The parameter list is located from the member-name occurrence, not by taking the
    /// first or last <c>(</c>. A conversion operator may return a tuple, and that tuple's
    /// parenthesis comes first — for <c>(int a, int b) op_Implicit(Foo f)</c> a
    /// first-paren scan finds index 0 and bails out, leaving the raw <c>op_Implicit</c>
    /// spelling. Equally, the metadata name can appear inside the return type or a
    /// parameter type (<c>Converter op_Implicit(op_Implicit value)</c>), so the member
    /// occurrence is identified as the whole-token one immediately followed by <c>(</c>
    /// rather than by textual position.
    /// </remarks>
    static string FormatOperatorSignature(string signature, string methodName)
    {
        if (!TryFindMemberNameBeforeParameterList(signature, methodName, out int nameIndex, out int parenStart))
            return signature;

        var returnType = signature[..nameIndex].TrimEnd();
        var parameters = signature[parenStart..];

        if (methodName.StartsWith("op_Checked", StringComparison.Ordinal)
            && OperatorNames.MapBinaryOrUnary(methodName["op_Checked".Length..]) is { } checkedSymbol)
            return $"{returnType} operator checked {checkedSymbol}{parameters}";

        return methodName switch
        {
            "op_Implicit" => $"implicit operator {returnType}{parameters}",
            "op_Explicit" => $"explicit operator {returnType}{parameters}",
            "op_CheckedExplicit" => $"explicit operator checked {returnType}{parameters}",
            _ => $"{returnType} {OperatorNames.FormatDisplayName(methodName)}{parameters}"
        };
    }

    /// <summary>
    /// Finds the occurrence of <paramref name="memberName"/> that is the declared member
    /// rather than part of a type spelling: a whole identifier token whose next
    /// non-whitespace character opens the parameter list.
    /// </summary>
    static bool TryFindMemberNameBeforeParameterList(
        string signature, string memberName, out int nameIndex, out int parenStart)
    {
        nameIndex = -1;
        parenStart = -1;
        if (memberName.Length == 0)
            return false;

        for (int i = signature.IndexOf(memberName, StringComparison.Ordinal);
            i >= 0;
            i = signature.IndexOf(memberName, i + 1, StringComparison.Ordinal))
        {
            if (i > 0 && (IsIdentifierPart(signature[i - 1]) || signature[i - 1] == '@'))
                continue;

            int after = i + memberName.Length;
            if (after < signature.Length && IsIdentifierPart(signature[after]))
                continue;

            int scan = after;
            while (scan < signature.Length && char.IsWhiteSpace(signature[scan]))
                scan++;
            if (scan >= signature.Length || signature[scan] != '(')
                continue;

            nameIndex = i;
            parenStart = scan;
            return i > 0;
        }

        return false;
    }

    static string FormatConstructorTypeName(
        ApiType type,
        CSharpDeclarationOptions options)
        => options.DeclaredTypeSelfName?.Identifier
            ?? options.LegacyDeclaredTypeIdentifier
            ?? SanitizeIdentifier(
                CSharpFormatter.FormatDeclarationLeafMetadataName(type));

    static string EscapeMemberNameInSignature(string signature, string memberName)
    {
        if (string.IsNullOrEmpty(memberName))
            return signature;

        int searchEnd = signature.IndexOf('(');
        if (searchEnd < 0)
            searchEnd = signature.IndexOf('{');
        if (searchEnd < 0)
            searchEnd = signature.Length;
        if (searchEnd <= 0)
            return signature;

        int nameIndex = signature.LastIndexOf(memberName, searchEnd - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        // Containment, not just keyword escaping: this name is untrusted metadata
        // and the result is rendered into a signature cell. A qualified name keeps
        // its dots, so each segment is contained on its own (issue #3319).
        string escaped = ContainMemberName(memberName);
        return escaped == memberName
            ? signature
            : string.Concat(signature.AsSpan(0, nameIndex), escaped, signature.AsSpan(nameIndex + memberName.Length));
    }

    internal static string EscapeQualifiedKeywordSegments(
        string signature,
        bool preserveQualifiedIndexerKeyword = false)
    {
        var sb = new StringBuilder(signature.Length);
        bool inString = false;
        bool inChar = false;
        bool escapedChar = false;
        for (int i = 0; i < signature.Length; i++)
        {
            char c = signature[i];
            sb.Append(c);
            if (inString || inChar)
            {
                if (escapedChar)
                {
                    escapedChar = false;
                    continue;
                }
                if (c == '\\')
                {
                    escapedChar = true;
                    continue;
                }
                if (inString && c == '"')
                    inString = false;
                else if (inChar && c == '\'')
                    inChar = false;
                continue;
            }
            if (c == '"')
            {
                inString = true;
                continue;
            }
            if (c == '\'')
            {
                inChar = true;
                continue;
            }
            if (c != '.' || i + 1 >= signature.Length || signature[i + 1] == '@' || !IsIdentifierStart(signature[i + 1]))
                continue;

            int start = i + 1;
            int end = start + 1;
            while (end < signature.Length && IsIdentifierPart(signature[end]))
                end++;

            string segment = signature[start..end];
            if (preserveQualifiedIndexerKeyword
                && segment == "this"
                && end < signature.Length
                && signature[end] == '[')
            {
                continue;
            }
            string escaped = EscapeIdentifier(segment);
            if (escaped != segment)
            {
                sb.Append(escaped);
                i = end - 1;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Best-effort parameter-name escaping for compatibility signature text —
    /// opaque <see cref="ApiMember.Signature"/>, or a model
    /// <see cref="TryRenderSignatureModel"/> cannot yet emit. Signatures built
    /// from the model escape names in <see cref="FormatParameter"/> and must not
    /// come through here.
    /// <c>MemberDeclaration_SignatureModel_DoesNotEscapeUnnamedParameterType</c>
    /// is the skip-vs-scan gate.
    /// </summary>
    /// <remarks>
    /// Only a parenthesis run that actually opens a parameter list is rewritten. C#
    /// tuple types are parenthesized too, so a naive "first paren wins" scan treats a
    /// tuple-typed return (or a tuple nested in a constraint) as a parameter list and
    /// escapes each element's trailing token — turning the predefined-type keyword in
    /// <c>(int, string) Pair(int a)</c> into the identifier <c>(@int, @string)</c>,
    /// which no longer binds (CS0246). A named element hid the bug, because there the
    /// trailing token is the element name rather than the type keyword.
    ///
    /// A parenthesized group is the parameter list when it *ends* the declaration —
    /// optionally followed by generic constraints. A group that is followed by more
    /// content is a parenthesized type, because what follows it is the member name it
    /// types: <c>(int, string) Pair(int a)</c>. This keys on the one structural
    /// difference between the two, so it needs no table of keywords or modifiers and
    /// stays correct for escaped member names (<c>void @event (int a)</c>), generic
    /// members (<c>void M&lt;T&gt; (T a)</c>), tuple-returning conversion operators
    /// (<c>implicit operator (int a, int b)(Foo f)</c>), and arbitrary modifier runs.
    /// </remarks>
    static string EscapeParameterLists(string signature)
    {
        var sb = new StringBuilder(signature.Length);
        int start = 0;
        while (true)
        {
            int open = signature.IndexOf('(', start);
            if (open < 0)
            {
                sb.Append(signature, start, signature.Length - start);
                return sb.ToString();
            }
            if (!OpensParameterList(signature, open))
            {
                sb.Append(signature, start, open - start + 1);
                start = open + 1;
                continue;
            }
            int close = Matching(signature, open, '(', ')');
            if (close < 0)
            {
                sb.Append(signature, start, signature.Length - start);
                return sb.ToString();
            }

            sb.Append(signature, start, open - start + 1);
            sb.Append(string.Join(", ", SplitTopLevel(signature[(open + 1)..close]).Select(EscapeParameterName)));
            sb.Append(')');
            start = close + 1;
        }
    }

    static bool OpensParameterList(string signature, int open)
    {
        int close = Matching(signature, open, '(', ')');
        if (close < 0)
            return false;

        var trailing = signature.AsSpan(close + 1).TrimStart();
        return trailing.IsEmpty || StartsWithConstraintClause(trailing);
    }

    /// <summary>
    /// True when <paramref name="trailing"/> begins a generic constraint clause.
    /// </summary>
    /// <remarks>
    /// Requires the whole <c>where T :</c> shape, not just the leading word.
    /// <c>where</c> is a contextual keyword, so a member may legally be named it, and a
    /// tuple-returning one puts that name exactly where a constraint would go:
    /// <c>(int, int) where (int a)</c>. Matching the word alone classifies the tuple as
    /// a parameter list and mangles it to <c>(@int, @int)</c>.
    ///
    /// The two are told apart by which delimiter arrives first: a constraint reaches its
    /// <c>:</c>, whereas a member name reaches the <c>(</c> or <c>&lt;</c> of its
    /// parameter or type-argument list. Deciding on the delimiter rather than on the
    /// shape of the name in between keeps this correct for type parameters spelled with
    /// characters <see cref="IsIdentifierPart"/> does not model, such as the combining
    /// marks C# permits as identifier continuations.
    /// </remarks>
    static bool StartsWithConstraintClause(ReadOnlySpan<char> trailing)
    {
        if (!trailing.StartsWith("where", StringComparison.Ordinal))
            return false;
        if (trailing.Length <= 5 || !char.IsWhiteSpace(trailing[5]))
            return false;

        for (int i = 6; i < trailing.Length; i++)
        {
            if (trailing[i] == ':')
                return true;
            if (trailing[i] is '(' or '<')
                return false;
        }
        return false;
    }

    static string EscapeParameterName(string parameter)
    {
        if (parameter.Length == 0)
            return parameter;

        int equals = parameter.IndexOf('=');
        string prefix = equals >= 0 ? parameter[..equals] : parameter;
        string suffix = equals >= 0 ? parameter[equals..] : "";

        int end = prefix.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(prefix[end]))
            end--;
        int start = end;
        while (start >= 0 && (IsIdentifierPart(prefix[start]) || prefix[start] == '@'))
            start--;
        start++;
        if (start > end)
            return parameter;

        string name = prefix[start..(end + 1)];
        // StartsWith(char) — the (char, StringComparison) overload is net11-only
        // and breaks the net10.0 OfficialBuild package floor (CS1503 in pack).
        string escaped = name.StartsWith('@') ? name : EscapeIdentifier(name);
        return prefix[..start] + escaped + prefix[(end + 1)..] + suffix;
    }

    public static string EscapeKnownIdentifiers(string text, IEnumerable<string> rawNames)
    {
        var names = rawNames.Where(name => EscapeIdentifier(name) != name).ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (IsIdentifierStart(text[i]))
            {
                int start = i++;
                while (i < text.Length && IsIdentifierPart(text[i]))
                    i++;
                string token = text[start..i];
                bool alreadyEscaped = start > 0 && text[start - 1] == '@';
                sb.Append(!alreadyEscaped && names.Contains(token) ? EscapeIdentifier(token) : token);
                continue;
            }
            sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    static string EscapeQualifiedIdentifier(string name)
        => string.Join("+", name.Split('+').Select(EscapeIdentifier));

    static string EscapeQualifiedName(string name)
        => string.Join(".", name.Split('.').Select(part => string.Join("+", part.Split('+').Select(EscapeIdentifier))));

    /// <summary>
    /// <see cref="EscapeQualifiedName"/> with each segment contained rather than
    /// only keyword-escaped, for a name that came from untrusted metadata.
    /// </summary>
    static string ContainQualifiedName(string name)
        => string.Join(".", name.Split('.').Select(part => string.Join("+", part.Split('+').Select(SanitizeIdentifier))));

    /// <summary>
    /// Contains a member name that is about to be rendered into a declaration,
    /// keeping the dots of a qualified (explicit interface) name intact.
    /// </summary>
    static string ContainMemberName(string name)
        => name.Contains('.', StringComparison.Ordinal)
            ? ContainQualifiedName(name)
            : SanitizeIdentifier(name);

    public static string EscapeIdentifier(string name)
        => CSharpKeywords.RequiresDeclarationEscape(name) ? "@" + name : name;

    /// <summary>
    /// The spelling to use for a metadata name that reaches emitted declaration
    /// text: <see cref="EscapeIdentifier"/> handles keywords but leaves an
    /// unspellable name (one carrying a line terminator, say) intact, which would
    /// let it break out of the surrounding code fence. Sanitizing folds it to
    /// identifier characters instead.
    /// </summary>
    /// <remarks>
    /// Byte-neutral for every name a compiler can emit, since none of them carry a
    /// line terminator; pinned by <c>CSharpIdentifierSanitizationTests</c>.
    /// </remarks>
    static string SanitizeIdentifier(string name)
        => CSharpIdentifier.ContainIdentifierForDeclaration(name);

    static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    static string AddMethodGenericParameters(string signature, string methodName, IReadOnlyList<string> methodParameters)
    {
        if (methodParameters.Count == 0 || string.IsNullOrEmpty(methodName))
            return signature;

        var parenStart = signature.IndexOf('(');
        if (parenStart <= 0)
            return signature;

        var nameIndex = signature.LastIndexOf(methodName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        var insertAt = nameIndex + methodName.Length;
        if (insertAt < parenStart && signature[insertAt] == '<')
            return signature;

        return signature.Insert(insertAt, $"<{string.Join(", ", methodParameters.Select(SanitizeIdentifier))}>");
    }

    public static string EscapeNamespace(string name)
        => name.Length == 0
            ? ""
            : string.Join(
                ".",
                name.Split('.').Select(SanitizeIdentifier));

    internal static string TypeAccessibility(ApiType type)
        => type.Accessibility ?? "public";

    static string AddExtensionThisModifier(string signature)
    {
        var parenStart = signature.IndexOf('(');
        var parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return signature;

        var firstParameterStart = parenStart + 1;
        while (firstParameterStart < signature.Length && char.IsWhiteSpace(signature[firstParameterStart]))
            firstParameterStart++;

        // Parameter attributes precede the extension receiver modifier in C#:
        // [NotNull] this string value. The structured signature renderer has
        // already attached those attributes, so insert `this` after every
        // leading attribute list rather than at the raw parameter start.
        var modifierStart = firstParameterStart;
        while (modifierStart < signature.Length && signature[modifierStart] == '[')
        {
            var close = MatchingAttributeList(signature, modifierStart);
            if (close < 0)
                return signature;
            modifierStart = close + 1;
            while (modifierStart < signature.Length && char.IsWhiteSpace(signature[modifierStart]))
                modifierStart++;
        }

        if (signature.AsSpan(modifierStart).StartsWith("this ".AsSpan(), StringComparison.Ordinal))
            return signature;

        return signature.Insert(modifierStart, "this ");
    }

    static int MatchingAttributeList(string text, int start)
    {
        var depth = 0;
        char quote = '\0';
        bool escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                continue;
            }
            if (c == '[')
                depth++;
            else if (c == ']' && --depth == 0)
                return i;
        }

        return -1;
    }

    static string AbbreviateSignature(string signature)
    {
        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return signature;

        int parenEnd = signature.LastIndexOf(')');
        if (parenEnd < 0)
            return signature;

        string prefix = signature[..(parenStart + 1)];
        string suffix = signature[parenEnd..];
        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return signature;

        var paramTypes = SplitTopLevel(paramSection)
            .Select(param => TryAbbreviatedParameterType(param, out var type) ? type : param)
            .ToList();

        return prefix + string.Join(", ", paramTypes) + suffix;
    }

    static bool TryAbbreviatedParameterType(string parameter, out string type)
    {
        type = "";
        parameter = StripDefaultValue(parameter).Trim();

        var lastSpace = LastTopLevelSpace(parameter);
        if (lastSpace <= 0)
            return false;

        type = parameter[..lastSpace].TrimEnd();
        return type.Length > 0;
    }

    static string FormatConstructorCall(string signature)
    {
        int parenStart = signature.IndexOf('(');
        return parenStart < 0 ? "()" : signature[parenStart..];
    }

    static int LastTopLevelSpace(string text)
    {
        var depth = 0;
        var last = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '<' or '[' or '(') depth++;
            else if (c is '>' or ']' or ')') depth--;
            else if (char.IsWhiteSpace(c) && depth == 0)
                last = i;
        }
        return last;
    }

    static IEnumerable<string> SplitTopLevel(string text)
    {
        if (text.Length == 0)
            yield break;

        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                i = SkipLiteral(text, i);
                continue;
            }
            if (c is '<' or '[' or '(') depth++;
            else if (c is '>' or ']' or ')') depth--;
            else if (c == ',' && depth == 0)
            {
                yield return text[start..i].Trim();
                start = i + 1;
            }
        }
        yield return text[start..].Trim();
    }

    static int Matching(string text, int open, char openChar, char closeChar)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                i = SkipLiteral(text, i);
                continue;
            }
            if (c == openChar) depth++;
            else if (c == closeChar && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns the index of the closing quote of the string or character literal that
    /// starts at <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// Brace/paren scanners over a signature must not read punctuation inside a literal
    /// as structure. A parameter default may legally contain any character, so
    /// <c>void M(int event = ")")</c> otherwise terminates the parameter list at the
    /// <c>)</c> inside the string — which makes the trailing-context classification in
    /// <see cref="OpensParameterList"/> see leftover text and decline to escape a real
    /// parameter list. An unterminated literal returns the last index so every caller
    /// still makes progress rather than looping.
    ///
    /// Both literal forms are handled: in a verbatim string a backslash is an ordinary
    /// character and <c>""</c> is the escape, so treating <c>@"\"</c> as backslash-escaped
    /// would swallow the rest of the signature. All four prefix spellings are recognised
    /// (<c>"</c>, <c>@"</c>, <c>$"</c>, and both orders of <c>$@"</c>), and an
    /// interpolation hole is scanned with brace tracking so a quote or paren inside it is
    /// not read as structure. Raw string literals (<c>"""..."""</c>) are delegated to
    /// <see cref="SkipRawLiteral"/>.
    ///
    /// A literal inside an interpolation hole recurses, so nesting is capped at
    /// <see cref="MaxLiteralNestingDepth"/>. At the cap the scan stops descending and
    /// falls back to reading the inner punctuation as structure, which is what this code
    /// did before literals were modelled at all. Deep nesting is not producible from
    /// metadata — <c>ApiSurfaceExtractor.StringLiteral</c> backslash-escapes every quote,
    /// so a rendered default can never open a nested literal — but a stack overflow is
    /// uncatchable process death, so the bound is enforced rather than argued away.
    ///
    /// Comments are deliberately not modelled. This runs over a *rendered declaration*,
    /// not over C# source: no producer in this repository emits a comment into a
    /// signature string, whereas every literal form above is a legal spelling of a
    /// parameter default. See #3561 on escaping from the structured signature model
    /// instead of re-lexing rendered text.
    /// </remarks>
    static int SkipLiteral(string text, int index) => SkipLiteral(text, index, 0);

    const int MaxLiteralNestingDepth = 32;

    static int SkipLiteral(string text, int index, int depth)
    {
        char quote = text[index];
        bool verbatim = false;
        bool interpolated = false;
        if (quote == '"')
        {
            for (int p = index - 1; p >= 0 && text[p] is '@' or '$'; p--)
            {
                if (text[p] == '@')
                    verbatim = true;
                else
                    interpolated = true;
            }
        }

        int opening = 0;
        while (index + opening < text.Length && text[index + opening] == quote)
            opening++;
        // A raw string takes no '@' prefix, so in a verbatim string a run of quotes is
        // escaped content rather than a delimiter: @""""a is one quote, not a raw string.
        if (quote == '"' && !verbatim && opening >= 3)
            return SkipRawLiteral(text, index, opening);

        for (int i = index + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (interpolated && c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    i++;
                    continue;
                }
                if (depth >= MaxLiteralNestingDepth)
                    continue;
                i = SkipInterpolationHole(text, i, depth + 1);
                continue;
            }
            if (verbatim)
            {
                if (c != quote)
                    continue;
                if (i + 1 < text.Length && text[i + 1] == quote)
                {
                    i++;
                    continue;
                }
                return i;
            }
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (c == quote)
                return i;
        }
        return text.Length - 1;
    }

    /// <summary>
    /// Returns the index of the last quote of the delimiter closing the raw string
    /// literal that opens at <paramref name="index"/> with <paramref name="opening"/>
    /// quotes.
    /// </summary>
    /// <remarks>
    /// A raw string ends at the first run of at least as many quotes as opened it, and
    /// its content has no escape character at all — a shorter run of quotes, a backslash
    /// and an interpolation brace are all ordinary text. Scanning for the delimiter run
    /// therefore also handles the interpolated form (<c>$"""..."""</c>) without needing
    /// to model holes.
    /// </remarks>
    static int SkipRawLiteral(string text, int index, int opening)
    {
        for (int i = index + opening; i < text.Length; i++)
        {
            if (text[i] != '"')
                continue;

            int run = 0;
            while (i + run < text.Length && text[i + run] == '"')
                run++;
            if (run >= opening)
                return i + opening - 1;
            i += run - 1;
        }
        return text.Length - 1;
    }

    /// <summary>
    /// Returns the index of the <c>}</c> closing the interpolation hole that opens at
    /// <paramref name="open"/>. Nested literals inside the hole are skipped whole, so a
    /// quote or brace within them is not read as structure. Recursion is bounded by
    /// <see cref="MaxLiteralNestingDepth"/>; see <see cref="SkipLiteral(string, int, int)"/>.
    /// </summary>
    static int SkipInterpolationHole(string text, int open, int depth)
    {
        int braces = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'')
            {
                if (depth >= MaxLiteralNestingDepth)
                    continue;
                i = SkipLiteral(text, i, depth + 1);
                continue;
            }
            if (c == '{') braces++;
            else if (c == '}' && --braces == 0) return i;
        }
        return text.Length - 1;
    }

    sealed record TypeNamePlan(
        IReadOnlyList<KeyValuePair<string, (string Qualified, string? Shortened, string? Diagnostic)>> Replacements,
        ImmutableSortedSet<string> GeneratedUsings,
        List<string> Diagnostics)
    {
        public string Apply(string text)
        {
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length;)
            {
                if (IsStringLiteralStart(text, i))
                {
                    var end = SkipStringLiteral(text, i);
                    sb.Append(text.AsSpan(i, end - i));
                    i = end;
                    continue;
                }
                if (text[i] == '\'')
                {
                    var end = SkipCharLiteral(text, i);
                    sb.Append(text.AsSpan(i, end - i));
                    i = end;
                    continue;
                }

                bool matched = false;
                foreach (var (token, replacements) in Replacements)
                {
                    if (i + token.Length > text.Length
                        || !text.AsSpan(i, token.Length).SequenceEqual(token)
                        || !IsStartBoundary(text, i - 1)
                        || !IsEndBoundary(text, i + token.Length))
                    {
                        continue;
                    }

                    if (IsWithinGlobalAlias(text, i))
                    {
                        string qualified = replacements.Qualified;
                        sb.Append(qualified.StartsWith("global::", StringComparison.Ordinal)
                            ? qualified["global::".Length..]
                            : qualified);
                        AddDiagnostic(replacements.Diagnostic);
                    }
                    else
                    {
                        bool preserveQualification = IsAttributeValuePrefix(
                            text,
                            i,
                            i + token.Length);
                        string replacement = preserveQualification
                            ? replacements.Qualified
                            : replacements.Shortened ?? replacements.Qualified;
                        sb.Append(replacement);
                        if (replacement == replacements.Qualified)
                            AddDiagnostic(replacements.Diagnostic);
                    }
                    i += token.Length;
                    matched = true;
                    break;
                }

                if (!matched)
                    sb.Append(text[i++]);
            }

            return sb.ToString();

            void AddDiagnostic(string? diagnostic)
            {
                if (diagnostic is not null
                    && !Diagnostics.Contains(diagnostic, StringComparer.Ordinal))
                {
                    Diagnostics.Add(diagnostic);
                }
            }
        }

        public static TypeNamePlan Create(
            IEnumerable<string> references,
            CSharpDeclarationOptions options,
            IReadOnlySet<string> shadowingNames,
            string declaredTypeName,
            IReadOnlySet<string>? qualificationOnlyReferences = null,
            IReadOnlySet<string>? shortenableReferences = null,
            IReadOnlySet<string>? valueReferences = null,
            IReadOnlySet<string>? preferredSimpleNameReferences = null,
            IReadOnlySet<string>? attributeNameReferences = null)
        {
            var qualificationOnlyFullNames = qualificationOnlyReferences?
                .Select(TypeRef.TryCreate)
                .Where(reference => reference is not null)
                .Select(reference => reference!.FullName)
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            if (shortenableReferences is not null)
            {
                qualificationOnlyFullNames.ExceptWith(shortenableReferences
                    .Select(TypeRef.TryCreate)
                    .Where(reference => reference is not null)
                    .Select(reference => reference!.FullName));
            }
            var valueFullNames = valueReferences?
                .Select(TypeRef.TryCreate)
                .Where(reference => reference is not null)
                .Select(reference => reference!.FullName)
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var valueOnlyFullNames = valueFullNames.ToHashSet(StringComparer.Ordinal);
            valueOnlyFullNames.ExceptWith(qualificationOnlyFullNames);
            if (shortenableReferences is not null)
            {
                valueOnlyFullNames.ExceptWith(shortenableReferences
                    .Select(TypeRef.TryCreate)
                    .Where(reference => reference is not null)
                    .Select(reference => reference!.FullName));
            }
            var preferredSimpleNameFullNames = preferredSimpleNameReferences?
                .Select(TypeRef.TryCreate)
                .Where(reference => reference is not null)
                .Select(reference => reference!.FullName)
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var attributeNameFullNames = attributeNameReferences?
                .Select(TypeRef.TryCreate)
                .Where(reference => reference is not null)
                .Select(reference => reference!.FullName)
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var typeRefs = references
                .Select(TypeRef.TryCreate)
                .Where(r => r is not null)
                .Select(r => r!)
                .DistinctBy(r => r.FullName, StringComparer.Ordinal)
                .ToList();
            var bindingTypeRefs = typeRefs
                .Where(reference => !valueOnlyFullNames.Contains(reference.FullName))
                .ToList();

            var lexicalShadowingNames = shadowingNames.ToHashSet(StringComparer.Ordinal);
            lexicalShadowingNames.UnionWith(options.AdditionalShadowingNames);
            var namespaceShadowingNames = new HashSet<string>(StringComparer.Ordinal);
            var namespaceRootShadowingNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var typeRef in bindingTypeRefs)
            {
                AddVisibleNamespaceNames(
                    namespaceShadowingNames,
                    options.ContainingNamespace,
                    typeRef.Namespace,
                    namespaceRootShadowingNames);
            }
            foreach (var ns in options.Usings)
            {
                AddVisibleNamespaceNames(
                    namespaceShadowingNames,
                    options.ContainingNamespace,
                    ns,
                    namespaceRootShadowingNames);
            }
            foreach (var ns in options.AdditionalKnownNamespaces)
            {
                AddVisibleNamespaceNames(
                    namespaceShadowingNames,
                    options.ContainingNamespace,
                    ns,
                    namespaceRootShadowingNames);
            }
            AddVisibleNamespaceNames(
                namespaceShadowingNames,
                options.ContainingNamespace,
                options.ContainingNamespace,
                namespaceRootShadowingNames);
            if (bindingTypeRefs.Any(r => string.Equals(r.SimpleName, declaredTypeName, StringComparison.Ordinal)
                && !string.Equals(r.Namespace, options.ContainingNamespace, StringComparison.Ordinal)))
            {
                lexicalShadowingNames.Add(declaredTypeName);
            }
            var rootShadowingNames = lexicalShadowingNames.ToHashSet(StringComparer.Ordinal);
            rootShadowingNames.UnionWith(options.AdditionalRootShadowingNames);
            rootShadowingNames.Add(declaredTypeName);
            rootShadowingNames.UnionWith(namespaceRootShadowingNames);
            rootShadowingNames.UnionWith(bindingTypeRefs
                .Where(typeRef =>
                {
                    string containingNamespace = options.ContainingNamespace ?? "";
                    return typeRef.Namespace.Length <= containingNamespace.Length
                        && containingNamespace.StartsWith(typeRef.Namespace, StringComparison.Ordinal)
                        && (typeRef.Namespace.Length == containingNamespace.Length
                            || typeRef.Namespace.Length == 0
                            || containingNamespace[typeRef.Namespace.Length] == '.');
                })
                .Select(typeRef => typeRef.SimpleName));

            var shorteningTypeRefs = bindingTypeRefs
                .Where(reference => !qualificationOnlyFullNames.Contains(reference.FullName))
                .ToList();
            var contextualUsings = options.Usings.ToHashSet(StringComparer.Ordinal);
            var potentiallyImportedNamespaces = contextualUsings.ToHashSet(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(options.ContainingNamespace))
                potentiallyImportedNamespaces.Add(options.ContainingNamespace);
            if (options.TypeNameMode == CSharpTypeNameMode.ShortWithUsings)
                potentiallyImportedNamespaces.UnionWith(shorteningTypeRefs.Select(reference => reference.Namespace));
            var collisionEvidence = bindingTypeRefs
                .Where(reference => reference.Namespace.Length == 0
                    || potentiallyImportedNamespaces.Contains(reference.Namespace))
                .ToList();
            var collisions = CollidingSimpleNames(collisionEvidence);
            var allShadowingNames = lexicalShadowingNames
                .Concat(namespaceShadowingNames)
                .ToHashSet(StringComparer.Ordinal);
            var unsafeNamespaces = UnsafeNamespaces(
                shorteningTypeRefs,
                allShadowingNames,
                collisions,
                rootShadowingNames,
                bindingEvidence: bindingTypeRefs);
            var generatedUsings = new SortedSet<string>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            var replacements = new Dictionary<string, (string Qualified, string? Shortened, string? Diagnostic)>(
                StringComparer.Ordinal);
            void ReplaceQualifiedName(
                TypeRef typeRef,
                string qualifiedReplacement,
                string? shortenedReplacement = null,
                string? diagnostic = null)
            {
                var plan = (qualifiedReplacement, shortenedReplacement, diagnostic);
                Add(typeRef.FullName);
                Add(EscapeQualifiedKeywordSegments(typeRef.FullName));
                Add(EscapeNamespace(typeRef.FullName));

                void Add(string key)
                {
                    replacements[key] = plan;
                }
            }
            string ResolvableQualifiedName(TypeRef typeRef)
            {
                string root = NamespaceRoot(typeRef.Namespace);
                string escapedFullName = EscapeNamespace(typeRef.FullName);
                return rootShadowingNames.Contains(root)
                    ? $"global::{escapedFullName}"
                    : escapedFullName;
            }
            string? UnresolvableRootDiagnostic(TypeRef typeRef)
            {
                string root = NamespaceRoot(typeRef.Namespace);
                return options.AdditionalUnresolvableRootNames.Contains(root)
                    && !options.AdditionalDeclaredTypeFullNames.Contains(typeRef.FullName)
                    && IsKnownNamespaceRoot(root)
                        ? $"Type name '{typeRef.FullName}' conflicts with global type '{root}'; emitted the only available global-qualified spelling."
                        : null;
            }
            void KeepResolvableQualified(TypeRef typeRef)
            {
                ReplaceQualifiedName(
                    typeRef,
                    ResolvableQualifiedName(typeRef),
                    diagnostic: UnresolvableRootDiagnostic(typeRef));
            }
            void KeepAttributeValueQualified(TypeRef typeRef)
            {
                string root = NamespaceRoot(typeRef.Namespace);
                string qualified = (options.AdditionalDeclaredTypeFullNames.Contains(typeRef.FullName)
                        || options.AdditionalImportedDeclaredTypeFullNames.Contains(typeRef.FullName))
                    && !IsKnownNamespaceRoot(root)
                        ? EscapeNamespace(typeRef.FullName)
                        : ResolvableQualifiedName(typeRef);
                ReplaceQualifiedName(
                    typeRef,
                    qualified,
                    diagnostic: UnresolvableRootDiagnostic(typeRef));
            }
            bool IsKnownNamespaceRoot(string root)
                => options.AdditionalKnownNamespaces.Any(@namespace =>
                    string.Equals(@namespace, root, StringComparison.Ordinal)
                    || @namespace.StartsWith($"{root}.", StringComparison.Ordinal));

            foreach (var typeRef in typeRefs)
            {
                if (typeRef.Namespace.Length == 0)
                    continue;
                if (valueOnlyFullNames.Contains(typeRef.FullName))
                {
                    KeepAttributeValueQualified(typeRef);
                    continue;
                }
                if (qualificationOnlyFullNames.Contains(typeRef.FullName))
                {
                    KeepResolvableQualified(typeRef);
                    continue;
                }
                if (options.TypeNameMode == CSharpTypeNameMode.Qualified
                    && !preferredSimpleNameFullNames.Contains(typeRef.FullName))
                {
                    KeepResolvableQualified(typeRef);
                    continue;
                }
                if (attributeNameFullNames.Contains(typeRef.FullName)
                    && !typeRef.SimpleName.EndsWith("Attribute", StringComparison.Ordinal))
                {
                    string suffixedName = $"{typeRef.SimpleName}Attribute";
                    bool suffixCanBind = rootShadowingNames.Contains(suffixedName)
                        || allShadowingNames.Contains(suffixedName)
                        || collisionEvidence.Any(reference =>
                            reference.FullName != typeRef.FullName
                            && reference.SimpleName == suffixedName);
                    if (suffixCanBind)
                    {
                        diagnostics.Add(
                            $"Attribute name '{typeRef.SimpleName}' can bind to '{suffixedName}'; kept '{typeRef.FullName}' qualified.");
                        KeepResolvableQualified(typeRef);
                        continue;
                    }
                }
                var isSameNamespace = !string.IsNullOrWhiteSpace(options.ContainingNamespace)
                    && string.Equals(typeRef.Namespace, options.ContainingNamespace, StringComparison.Ordinal);
                if (!isSameNamespace && collisions.Contains(typeRef.SimpleName))
                {
                    diagnostics.Add($"Type name '{typeRef.SimpleName}' is ambiguous; kept '{typeRef.FullName}' qualified.");
                    KeepResolvableQualified(typeRef);
                    continue;
                }
                if (lexicalShadowingNames.Contains(typeRef.SimpleName))
                {
                    diagnostics.Add($"Type name '{typeRef.SimpleName}' is shadowed in this declaration; kept '{typeRef.FullName}' qualified.");
                    KeepResolvableQualified(typeRef);
                    continue;
                }
                if (!isSameNamespace && namespaceShadowingNames.Contains(typeRef.SimpleName))
                {
                    diagnostics.Add($"Type name '{typeRef.SimpleName}' is shadowed by a namespace in this declaration; kept '{typeRef.FullName}' qualified.");
                    KeepResolvableQualified(typeRef);
                    continue;
                }
                if (!isSameNamespace && unsafeNamespaces.Contains(typeRef.Namespace))
                {
                    diagnostics.Add($"Namespace '{typeRef.Namespace}' contains an ambiguous or shadowed type name; kept '{typeRef.FullName}' qualified.");
                    KeepResolvableQualified(typeRef);
                    continue;
                }
                var isInContext = isSameNamespace || contextualUsings.Contains(typeRef.Namespace);

                if (options.TypeNameMode == CSharpTypeNameMode.ContextualShort && !isInContext)
                {
                    KeepResolvableQualified(typeRef);
                    continue;
                }

                ReplaceQualifiedName(
                    typeRef,
                    valueFullNames.Contains(typeRef.FullName)
                        ? ResolvableQualifiedName(typeRef)
                        : EscapeNamespace(typeRef.FullName),
                    EscapeIdentifier(typeRef.SimpleName),
                    UnresolvableRootDiagnostic(typeRef));
                if (options.TypeNameMode == CSharpTypeNameMode.ShortWithUsings && !isSameNamespace)
                    generatedUsings.Add(typeRef.Namespace);
            }

            return new TypeNamePlan(
                replacements.OrderByDescending(kvp => kvp.Key.Length).ToArray(),
                generatedUsings.ToImmutableSortedSet(StringComparer.Ordinal),
                diagnostics);
        }

        static bool IsWithinGlobalAlias(string text, int index)
        {
            var start = index;
            while (start > 0
                && (IsIdentifierPart(text[start - 1]) || text[start - 1] is '.' or '+'))
            {
                start--;
            }
            return start >= "global::".Length
                && text.AsSpan(start - "global::".Length, "global::".Length)
                    .SequenceEqual("global::");
        }

        static bool IsStartBoundary(string text, int index)
            => index < 0
               || index >= text.Length
               || (!IsIdentifierPart(text[index]) && text[index] is not '.' and not '+' and not '@');

        static bool IsEndBoundary(string text, int index)
            => index < 0
               || index >= text.Length
               || (!IsIdentifierPart(text[index]) && text[index] != '+');

        static bool IsAttributeValuePrefix(string text, int start, int end)
        {
            if (end >= text.Length || text[end] != '.')
                return false;

            int parenthesisDepth = 0;
            var bracketParenthesisDepths = new Stack<int>();
            for (int index = 0; index < start;)
            {
                if (IsStringLiteralStart(text, index))
                {
                    index = SkipStringLiteral(text, index);
                    continue;
                }
                if (text[index] == '\'')
                {
                    index = SkipCharLiteral(text, index);
                    continue;
                }

                switch (text[index])
                {
                    case '[':
                        bracketParenthesisDepths.Push(parenthesisDepth);
                        break;
                    case ']':
                        if (bracketParenthesisDepths.Count > 0)
                            bracketParenthesisDepths.Pop();
                        break;
                    case '(':
                        parenthesisDepth++;
                        break;
                    case ')':
                        if (parenthesisDepth > 0)
                            parenthesisDepth--;
                        break;
                }
                index++;
            }

            return bracketParenthesisDepths.Count > 0
                && parenthesisDepth > bracketParenthesisDepths.Peek();
        }
    }

    static bool IsStringLiteralStart(string text, int index)
    {
        if (text[index] == '"')
            return true;
        if (text[index] is not ('@' or '$'))
            return false;
        do
        {
            index++;
        }
        while (index < text.Length && text[index] is '@' or '$');
        return index < text.Length && text[index] == '"';
    }

    static int SkipStringLiteral(string text, int start)
    {
        var i = start;
        while (i < text.Length && text[i] is '@' or '$')
            i++;
        if (i >= text.Length || text[i] != '"')
            return start + 1;
        return Math.Min(SkipLiteral(text, i) + 1, text.Length);
    }

    static int SkipCharLiteral(string text, int start)
    {
        var i = start + 1;
        while (i < text.Length)
        {
            if (text[i] == '\'')
                return i + 1;
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i += 2;
                continue;
            }

            i++;
        }

        return text.Length;
    }

    // An empty namespace records unqualified type-position evidence. It participates
    // in collision analysis but never produces a replacement or using directive.
    sealed record TypeRef(string FullName, string Namespace, string SimpleName)
    {
        public static TypeRef? TryCreate(string value)
        {
            value = value.Trim().TrimEnd('?');
            if (value.Length == 0)
                return null;
            var lastDot = value.LastIndexOf('.');
            if (lastDot == value.Length - 1)
                return null;
            if (lastDot < 0)
                return new TypeRef(value, "", StripArity(value));
            if (lastDot == 0)
                return null;
            var ns = value[..lastDot];
            var simple = StripArity(value[(lastDot + 1)..]);
            return new TypeRef(value, ns, simple);
        }

        static string StripArity(string name)
            => MetadataNameArity.StripFromSegment(name);
    }

    static readonly string[] s_parameterModifiers = ["this", "params", "ref", "out", "in", "scoped"];

    // Special-constraint tokens carried verbatim in TypeParameter.Constraints; every
    // other entry is a type name subject to reserved-keyword identifier escaping.
    static readonly HashSet<string> s_specialConstraintKeywords = new(StringComparer.Ordinal)
    {
        "class", "class?", "struct", "unmanaged", "notnull", "new()", "default", "allows ref struct",
    };

}
