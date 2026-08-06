using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Text;

namespace ILInspector.Decompiler;

public enum MemberBodyProductionStatus
{
    Complete,
    Absent,
    Failed,
}

/// <summary>
/// Product-owned result for one metadata-addressed C# method body. The body is a
/// typed increment for <see cref="TypeShellProducer"/>/<see cref="CSharpTypePrinter"/>;
/// it never contains or reconstructs the surrounding declaration.
/// </summary>
public sealed record MemberBodyProductionResult(
    MemberBodyProductionStatus Status,
    CSharpBlockBody? Body,
    DecompilerResult Projection)
{
    public bool IsComplete => Status == MemberBodyProductionStatus.Complete;

    /// <summary>
    /// The raised product IR that produced <see cref="Body"/>. Kept internal so
    /// trusted product/harness consumers can derive typed closure evidence from
    /// the exact projection without re-importing or reverse-engineering source.
    /// The publicly exposed members (<see cref="Body"/> and
    /// <see cref="Projection"/>) are fully materialized and hold no borrowed
    /// metadata. This IR seam is not: it may reference state owned by the
    /// producing <c>MetadataSource</c>, so it is valid only while that source is
    /// alive and must not be cached beyond the borrow session.
    /// </summary>
    internal Pipeline.IrFunction? RaisedFunction { get; init; }
}

/// <summary>
/// Product-owned result for one whole rendered member: the CSharp-owned
/// signature (spelled from the Metadata rich model) composed with the
/// decompiler-owned body — <c>signature { body }</c> / <c>signature =&gt; expr;</c>
/// / <c>signature;</c>. This is the per-member analog of the whole-type
/// <see cref="MemberBodyProducer.Project(ApiType, string, string?, IAssemblyReferenceResolver, ILInspector.Decompiler.Pipeline.MetadataContext?)"/>
/// listing: <see cref="Text"/> is byte-identical to the member's segment in that
/// listing (indented one level for a type body, with no surrounding blank-line
/// separators). Consumers that wrap the member in their own type shell add
/// <see cref="Namespaces"/> as using directives.
/// </summary>
public sealed record MemberRenderResult(
    MemberBodyProductionStatus Status,
    string? Text,
    IReadOnlyList<string> Namespaces)
{
    public bool IsComplete => Status == MemberBodyProductionStatus.Complete;
}

/// <summary>
/// Projects a whole type as one C# listing: the type declaration, field
/// declarations (including non-public fields, for context the bodies
/// reference), and every member's decompiled body — the reading unit for
/// building intuition about what a type does, and the comparison unit that
/// matches both reference decompilers and dotnet/runtime's per-type source
/// files.
/// </summary>
public static class MemberBodyProducer
{
    static readonly CSharpFormatter DefaultDeclarationFormatter = CreateDeclarationFormatter();
    static readonly CSharpFormatter TerminatedDeclarationFormatter = CreateDeclarationFormatter(terminateMemberDeclaration: true);

    /// <summary>
    /// Resolves one module-scoped method address into another live metadata
    /// source without exposing either source's borrowed metadata reader.
    /// </summary>
    public static MethodCorrespondenceResult ResolveCorrespondence(
        Pipeline.MetadataSource source,
        MetadataMethodAddress method,
        Pipeline.MetadataSource target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return MethodCorrespondenceResolver.Resolve(source.Reader, method, target.Reader);
    }

    /// <summary>
    /// Produces the typed C# body increment for one method in a live metadata
    /// source. The handle is session-bound and must belong to
    /// <paramref name="source"/>. Abstract, extern, and other bodyless methods
    /// return <see cref="MemberBodyProductionStatus.Absent"/>; import, raising,
    /// or rendering failures remain visible as <see cref="MemberBodyProductionStatus.Failed"/>.
    /// </summary>
    public static MemberBodyProductionResult ProduceBody(
        Pipeline.MetadataSource source,
        MetadataMethodAddress address)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!address.BelongsTo(source.Reader))
        {
            return Failed(
                DiagnosticIds.ContextUnavailable,
                "method address belongs to a different metadata module");
        }
        var methodHandle = address.Handle;
        if (methodHandle.IsNil)
        {
            return Failed(DiagnosticIds.ContextUnavailable, "method handle is nil");
        }
        int rowNumber = MetadataTokens.GetRowNumber(methodHandle);
        if (rowNumber <= 0 || rowNumber > source.Reader.GetTableRowCount(TableIndex.MethodDef))
        {
            return Failed(
                DiagnosticIds.ContextUnavailable,
                "method handle is outside this metadata source");
        }

        MethodDefinition method;
        try
        {
            method = source.Reader.GetMethodDefinition(methodHandle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return Failed(DiagnosticIds.ContextUnavailable, $"method handle is not valid for this metadata source: {ex.Message}");
        }

        if (method.RelativeVirtualAddress == 0)
        {
            return new MemberBodyProductionResult(
                MemberBodyProductionStatus.Absent,
                Body: null,
                DecompilerResult.Failure(
                    DiagnosticIds.ContextUnavailable,
                    "method has no IL body"));
        }

        try
        {
            var function = Pipeline.IrImporter.Import(source, methodHandle);
            if (function is null)
            {
                return Failed(
                    DiagnosticIds.ContextUnavailable,
                    "method body could not be imported");
            }

            var projection = Pipeline.CSharpPrinter.PrintRaised(
                function,
                importMethodBody: methodRef => Pipeline.IrImporter.Import(source, methodRef),
                typesProvablyDisjoint: source.AreProvablyDisjoint);
            if (projection.Output is null)
            {
                return new MemberBodyProductionResult(
                    MemberBodyProductionStatus.Failed,
                    Body: null,
                    projection);
            }

            var initializer = projection.ConstructorChain is { } chain
                ? CSharpFormatter.ParseConstructorInitializer(chain)
                : null;
            var body = new CSharpBlockBody(projection.Output.TrimEnd(), initializer)
            {
                RequiresAsyncModifier = projection.RequiresAsyncBodyModifier,
                RequiresUnsafeModifier = projection.RequiresUnsafeBodyModifier,
                // Member-agnostic destructor gate: suppress '~Type()' whenever the
                // body was not recovered as a canonical destructor (issue #3157).
                // Harmless for non-finalizers (the writer only consults this when
                // the member is a finalizer); correct for a finalizer consumer of
                // this shared substrate whose body did not match the scaffold.
                SuppressDestructorSyntax = !projection.BodyIsDestructor,
            };
            return new MemberBodyProductionResult(
                MemberBodyProductionStatus.Complete,
                body,
                projection)
            {
                RaisedFunction = function,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failed(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }

        static MemberBodyProductionResult Failed(string id, string message)
            => new(
                MemberBodyProductionStatus.Failed,
                Body: null,
                DecompilerResult.Failure(id, message));
    }

    /// <summary>
    /// Convenience entry point that resolves referenced assemblies with the
    /// default sibling policy (<see cref="Pipeline.MetadataSource.DefaultAssemblyReferenceResolver"/>).
    /// Prefer the <see cref="IAssemblyReferenceResolver"/> overload when a
    /// caller needs identity- or stream-backed resolution.
    /// <paramref name="printerOptions"/> defaults to the shipped output.
    /// </summary>
    public static DecompilerResult Project(ApiType type, string dllPath, string? pdbPath, Pipeline.MetadataContext? context = null, Pipeline.PrinterOptions? printerOptions = null)
    {
        var resolver = Pipeline.MetadataSource.DefaultAssemblyReferenceResolver(dllPath);
        return Project(type, dllPath, pdbPath, resolver, context, printerOptions);
    }

    /// <summary>
    /// Projects a whole type as one C# listing using the optional
    /// <paramref name="printerOptions"/> for body rendering and member layout;
    /// omitting them preserves the shipped byte-identical output.
    /// </summary>
    public static DecompilerResult Project(ApiType type, string dllPath, string? pdbPath, IAssemblyReferenceResolver resolver, Pipeline.MetadataContext? context = null, Pipeline.PrinterOptions? printerOptions = null)
    {
        var start = ResolvedAssemblyReference.CreateFromPath(
            dllPath,
            AssemblyResolutionProvenance.Local("StartAssembly"));
        var composed = ComposeCore(
            type,
            dllPath,
            pdbPath,
            () => ResolveDefinition(start, type, resolver, context),
            (definition, ctx) => Pipeline.MetadataSource.Open(
                definition.Assembly.Assembly,
                pdbPath,
                resolver,
                ctx),
            context,
            printerOptions);
        return composed is null
            ? DecompilerResult.Failure("DI_TYPESOURCE_NONE", $"No C# type source composed for {type.FullName}.")
            : DecompilerResult.Success(composed);
    }

    /// <summary>
    /// Convenience entry point that resolves referenced assemblies with the
    /// default sibling policy (<see cref="Pipeline.MetadataSource.DefaultAssemblyReferenceResolver"/>).
    /// Prefer the <see cref="IAssemblyReferenceResolver"/> overload when a
    /// caller needs identity- or stream-backed resolution.
    /// <paramref name="printerOptions"/> defaults to the shipped output.
    /// </summary>
    public static MemberRenderResult ProduceMember(ApiType type, ApiMember member, string dllPath, string? pdbPath, Pipeline.MetadataContext? context = null, Pipeline.PrinterOptions? printerOptions = null)
    {
        var resolver = Pipeline.MetadataSource.DefaultAssemblyReferenceResolver(dllPath);
        return ProduceMember(type, member, dllPath, pdbPath, resolver, context, printerOptions);
    }

    /// <summary>
    /// Renders one member of <paramref name="type"/> as a whole member —
    /// CSharp-owned signature composed with the decompiler-owned body — reusing
    /// the exact per-member composition of the whole-type
    /// <see cref="Project(ApiType, string, string?, IAssemblyReferenceResolver, Pipeline.MetadataContext?)"/>
    /// listing, so the rendered text is byte-identical to that member's segment.
    /// <paramref name="member"/> must be an instance from
    /// <see cref="ApiType.Members"/> (matched by reference). Enum values, and
    /// members that produce no listing output, return
    /// <see cref="MemberBodyProductionStatus.Absent"/>. Omitting
    /// <paramref name="printerOptions"/> preserves the shipped output.
    /// </summary>
    public static MemberRenderResult ProduceMember(ApiType type, ApiMember member, string dllPath, string? pdbPath, IAssemblyReferenceResolver resolver, Pipeline.MetadataContext? context = null, Pipeline.PrinterOptions? printerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        var start = ResolvedAssemblyReference.CreateFromPath(
            dllPath,
            AssemblyResolutionProvenance.Local("StartAssembly"));
        return ComposeMemberCore(
            type,
            member,
            () => ResolveDefinition(start, type, resolver, context),
            (definition, ctx) => Pipeline.MetadataSource.Open(
                definition.Assembly.Assembly,
                pdbPath,
                resolver,
                ctx),
            context,
            printerOptions);
    }

    /// <summary>
    /// Convenience entry point that resolves referenced assemblies with the
    /// default sibling policy (<see cref="Pipeline.MetadataSource.DefaultAssemblyReferenceResolver"/>).
    /// Prefer the <see cref="IAssemblyReferenceResolver"/> overload when a
    /// caller needs identity- or stream-backed resolution.
    /// </summary>
    public static IReadOnlyDictionary<ApiMember, MemberRenderResult> ProduceMembers(ApiType type, string dllPath, string? pdbPath, Pipeline.MetadataContext? context = null)
    {
        var resolver = Pipeline.MetadataSource.DefaultAssemblyReferenceResolver(dllPath);
        return ProduceMembers(type, dllPath, pdbPath, resolver, context);
    }

    /// <summary>
    /// Renders every member of <paramref name="type"/> as a whole member in one
    /// pass — the batch form of
    /// <see cref="ProduceMember(ApiType, ApiMember, string, string?, IAssemblyReferenceResolver, Pipeline.MetadataContext?)"/>.
    /// Each entry is byte-identical to what <c>ProduceMember</c> returns for that
    /// member, but the assembly is opened and its type maps built once for the
    /// whole type rather than once per member — the cost model a caller rendering
    /// many members of the same type (such as the compile-back harness) needs.
    /// The returned map is keyed by the same <see cref="ApiMember"/> instances in
    /// <see cref="ApiType.Members"/> (reference identity). Members that produce no
    /// listing output are mapped to <see cref="MemberBodyProductionStatus.Absent"/>.
    /// </summary>
    public static IReadOnlyDictionary<ApiMember, MemberRenderResult> ProduceMembers(ApiType type, string dllPath, string? pdbPath, IAssemblyReferenceResolver resolver, Pipeline.MetadataContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        var start = ResolvedAssemblyReference.CreateFromPath(
            dllPath,
            AssemblyResolutionProvenance.Local("StartAssembly"));
        return ComposeMembersBatch(
            type,
            () => ResolveDefinition(start, type, resolver, context),
            (definition, ctx) => Pipeline.MetadataSource.Open(
                definition.Assembly.Assembly,
                pdbPath,
                resolver,
                ctx),
            context);
    }

    static ResolvedTypeDefinition? ResolveDefinition(
        ResolvedAssemblyReference start,
        ApiType type,
        IAssemblyReferenceResolver resolver,
        Pipeline.MetadataContext? context)
    {
        MetadataTypeDefinitionName? name = type.DefinitionName;
        if (name is null)
        {
            string metadataName = type.MetadataName ?? type.Name;
            if (metadataName.Contains('+', StringComparison.Ordinal)
                || MetadataTypeDefinitionName.Create(
                    type.Namespace ?? "",
                    [metadataName])
                    is not MetadataTypeDefinitionNameResult.Valid valid)
            {
                return null;
            }
            name = valid.Name;
        }

        var request = TypeResolutionRequest.FromAssembly(
            start,
            AssemblyResolutionScope.Any,
            name);
        TypeResolutionOutcome outcome;
        if (context is not null)
        {
            outcome = context.Resolve(start, request);
        }
        else
        {
            using var catalog = new TypeResolutionCatalog();
            var policy = new AssemblyReferenceBindingPolicy(resolver);
            using TypeResolutionContext resolutionContext =
                catalog.CreateContext(
                    policy,
                    [start],
                    [request]);
            outcome = resolutionContext.Resolve(request);
        }

        return outcome is TypeResolutionOutcome.Resolved resolved
            ? resolved.Definition
            : null;
    }

    static string? ComposeCore(
        ApiType type,
        string dllPath,
        string? pdbPath,
        Func<ResolvedTypeDefinition?> locateType,
        Func<ResolvedTypeDefinition, Pipeline.MetadataContext?, Pipeline.MetadataSource> openPipelineSource,
        Pipeline.MetadataContext? context,
        Pipeline.PrinterOptions? printerOptions)
    {
        if (type.Kind is "delegate")
            return null;

        try
        {
            if (locateType() is not { } definition)
                return null;

            Stream? stream = null;
            PEReader? peReader = null;
            try
            {
                stream = definition.Assembly.Assembly.OpenRead();
                peReader = new PEReader(stream);
                MetadataReader reader = peReader.GetMetadataReader();

                if (!definition.Address.TryResolve(
                        reader,
                        out TypeDefinitionHandle typeHandle))
                    return null;

                    // Bodies are decompiled from the same on-disk assembly the
                    // forwarder resolved to. The same resolver resolves cross-assembly
                    // type facts (value-type-ness of a bare token) during import. A
                    // shared context (when a batch caller supplies one) opens each
                    // referenced assembly once across many composed types.
                    using var pipelineSource = openPipelineSource(
                        definition,
                        context);
                    var union = TryUnionDeclaration(reader, typeHandle, type);

                    var sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(type.Namespace))
                    {
                        sb.AppendLf($"namespace {type.Namespace};");
                        sb.AppendLf();
                    }

                    // The printer renders every type with its simple name, so there is
                    // no namespace prefix for HoistUsings to strip into a directive. The
                    // bodies' namespaces are collected straight from the typed IR
                    // instead and seeded into the using block; attribute namespaces
                    // join them so the short attribute names resolve.
                    var bodyNamespaces = new SortedSet<string>(StringComparer.Ordinal);

                    if (union is not null)
                        AddTypeNamespaces(bodyNamespaces, union.CaseTypes);

                    var typeDef = reader.GetTypeDefinition(typeHandle);
                    foreach (var attribute in LayoutAttributes(type, typeDef, bodyNamespaces))
                        sb.AppendLf($"[{attribute}]");

                    foreach (var attribute in AttributeReader.RenderAttributes(reader, typeDef.GetCustomAttributes(), bodyNamespaces,
                                 union is null ? null : name => name == KnownAttributeNames.UnionAttribute))
                        sb.AppendLf($"[{attribute}]");

                    sb.AppendLf(TypeDeclaration(type, union));
                    sb.AppendLf("{");

                    bool any = union is not null;
                    if (type.Kind == "enum")
                    {
                        ComposeEnumValues(sb, type, ref any);
                    }
                    else
                    {
                        ComposeFields(sb, reader, typeHandle, bodyNamespaces,
                            CollectFieldInitializers(pipelineSource, reader, typeHandle), ref any);
                        ComposeMembers(sb, type, pipelineSource, reader, typeHandle, union, bodyNamespaces, ref any, printerOptions: printerOptions);
                    }

                    sb.AppendLf("}");
                    if (!any)
                        return null;
                    return HoistUsings(sb.ToString().TrimEnd(), reader, type.Namespace, bodyNamespaces);
            }
            finally
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Degrade honestly: the section renders the reason instead of
            // silently disappearing.
            return $"// {DiagnosticIds.InternalError}: type source unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    static MemberRenderResult ComposeMemberCore(
        ApiType type,
        ApiMember member,
        Func<ResolvedTypeDefinition?> locateType,
        Func<ResolvedTypeDefinition, Pipeline.MetadataContext?, Pipeline.MetadataSource> openPipelineSource,
        Pipeline.MetadataContext? context,
        Pipeline.PrinterOptions? printerOptions)
    {
        if (type.Kind is "delegate")
            return new MemberRenderResult(MemberBodyProductionStatus.Absent, Text: null, []);

        try
        {
            if (locateType() is not { } definition)
                return new MemberRenderResult(MemberBodyProductionStatus.Absent, Text: null, []);

            Stream? stream = null;
            PEReader? peReader = null;
            try
            {
                stream = definition.Assembly.Assembly.OpenRead();
                peReader = new PEReader(stream);
                MetadataReader reader = peReader.GetMetadataReader();

                if (!definition.Address.TryResolve(
                        reader,
                        out TypeDefinitionHandle typeHandle))
                    return new MemberRenderResult(MemberBodyProductionStatus.Absent, Text: null, []);

                using var pipelineSource = openPipelineSource(
                    definition,
                    context);
                var union = TryUnionDeclaration(reader, typeHandle, type);

                // The same body/attribute namespaces the whole-type listing
                // collects for this member — a wrapping consumer emits them as
                // using directives.
                var bodyNamespaces = new SortedSet<string>(StringComparer.Ordinal);
                var sb = new StringBuilder();
                bool any = false;
                ComposeMembers(sb, type, pipelineSource, reader, typeHandle, union, bodyNamespaces, ref any, only: member, printerOptions: printerOptions);

                if (!any)
                    return new MemberRenderResult(MemberBodyProductionStatus.Absent, Text: null, bodyNamespaces.ToArray());

                // Shorten qualified names and normalize newlines exactly as the
                // whole-type Project listing does, so the per-member text is
                // byte-identical to this member's segment there. The harvested
                // imports (body namespaces + qualified prefixes) are returned for
                // the wrapping consumer to emit; directives are not prepended.
                var imports = new SortedSet<string>(bodyNamespaces, StringComparer.Ordinal);
                string text = ShortenQualifiedNames(sb.ToString(), reader, imports);
                return new MemberRenderResult(MemberBodyProductionStatus.Complete, text, imports.ToArray());
            }
            finally
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Degrade honestly, matching ComposeCore: the failure reason is the
            // rendered text instead of silently disappearing.
            return new MemberRenderResult(
                MemberBodyProductionStatus.Failed,
                $"// {DiagnosticIds.InternalError}: member source unavailable: {ex.GetType().Name}: {ex.Message}",
                []);
        }
    }

    /// <summary>
    /// Batch form of <see cref="ComposeMemberCore"/>: opens the assembly and
    /// builds its type maps once for the whole type, then renders each member
    /// through the same single-member composition (<see cref="ComposeMembers"/>
    /// with <c>only</c> plus <see cref="ShortenQualifiedNames"/>) so every entry
    /// is byte-identical to the per-member <see cref="ProduceMember"/> path. Bodies
    /// decode against the shared open <see cref="Pipeline.MetadataSource"/>, so the
    /// per-member setup cost is paid once rather than once per member.
    /// </summary>
    static IReadOnlyDictionary<ApiMember, MemberRenderResult> ComposeMembersBatch(
        ApiType type,
        Func<ResolvedTypeDefinition?> locateType,
        Func<ResolvedTypeDefinition, Pipeline.MetadataContext?, Pipeline.MetadataSource> openPipelineSource,
        Pipeline.MetadataContext? context)
    {
        var results = new Dictionary<ApiMember, MemberRenderResult>(ReferenceEqualityComparer.Instance);
        if (type.Kind is "delegate")
            return results;

        try
        {
            if (locateType() is not { } definition)
                return results;

            Stream? stream = null;
            PEReader? peReader = null;
            try
            {
                stream = definition.Assembly.Assembly.OpenRead();
                peReader = new PEReader(stream);
                MetadataReader reader = peReader.GetMetadataReader();

                if (!definition.Address.TryResolve(
                        reader,
                        out TypeDefinitionHandle typeHandle))
                    return results;

                using var pipelineSource = openPipelineSource(
                    definition,
                    context);
                var union = TryUnionDeclaration(reader, typeHandle, type);

                foreach (var member in type.Members)
                {
                    var bodyNamespaces = new SortedSet<string>(StringComparer.Ordinal);
                    var sb = new StringBuilder();
                    bool any = false;
                    ComposeMembers(sb, type, pipelineSource, reader, typeHandle, union, bodyNamespaces, ref any, only: member);

                    if (!any)
                    {
                        results[member] = new MemberRenderResult(MemberBodyProductionStatus.Absent, Text: null, bodyNamespaces.ToArray());
                        continue;
                    }

                    var imports = new SortedSet<string>(bodyNamespaces, StringComparer.Ordinal);
                    string text = ShortenQualifiedNames(sb.ToString(), reader, imports);
                    results[member] = new MemberRenderResult(MemberBodyProductionStatus.Complete, text, imports.ToArray());
                }

                return results;
            }
            finally
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }
        catch
        {
            // Degrade honestly: return whatever composed so far; the caller falls
            // back to its own rendering for any member missing from the map.
            return results;
        }
    }

    sealed record UnionDeclarationInfo(
        IReadOnlyList<string> CaseTypes,
        HashSet<int> HiddenMethodTokens,
        IReadOnlyList<ApiMember> ExplicitConstructors);

    static string TypeDeclaration(ApiType type, UnionDeclarationInfo? union = null)
    {
        var sb = new StringBuilder("public ");
        if (union is not null)
        {
            if (type.IsReadOnly) sb.Append("readonly ");
            sb.Append("union ");
            sb.Append(DisplayName(type));
            sb.Append('(');
            sb.Append(string.Join(", ", union.CaseTypes.Select(caseType =>
                EscapeKnownIdentifiers(Shorten(caseType), type.TypeParameters.Select(p => p.Name)))));
            sb.Append(')');
            var unionBases = type.Interfaces
                .Where(iface => !IsUnionInterface(iface))
                .Select(iface => EscapeKnownIdentifiers(iface, type.TypeParameters.Select(p => p.Name)))
                .ToList();
            if (unionBases.Count > 0)
                sb.Append($" : {string.Join(", ", unionBases)}");
            AppendTypeParameterConstraints(sb, type.TypeParameters);
            return sb.ToString();
        }

        return DefaultDeclarationFormatter.FormatTypeDeclaration(type);
    }

    static CSharpFormatter CreateDeclarationFormatter(
        bool terminateMemberDeclaration = false)
        => new(new CSharpFormatOptions
        {
            IncludeCustomAttributes = false,
            IncludeObsoleteAttribute = false,
            OmitInterfaceMemberModifiers = true,
            TerminateMemberDeclaration = terminateMemberDeclaration
        });

    static string DisplayName(ApiType type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        name = EscapeQualifiedIdentifier(name);
        if (type.TypeParameters.Count > 0)
            name += $"<{string.Join(", ", type.TypeParameters.Select(TypeParameterDisplayName))}>";
        return name;
    }

    static void AppendTypeParameterConstraints(StringBuilder sb, IReadOnlyList<TypeParameter> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        {
            if (typeParameter.Constraints.Count > 0)
                sb.Append($" where {ContainedIdentifier(typeParameter.Name)} : {CSharpFormatter.FormatTypeParameterConstraints(typeParameter, typeParameters.Select(p => p.Name))}");
        }
    }

    static void ComposeEnumValues(StringBuilder sb, ApiType type, ref bool any)
    {
        foreach (var member in type.Members)
        {
            if (member.Kind != "field" || member.EnumValue is null)
                continue;
            sb.AppendLf($"    {ContainedIdentifier(member.Name)} = {member.EnumValueLiteral ?? member.EnumValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            any = true;
        }
    }

    static IEnumerable<string> LayoutAttributes(ApiType type, TypeDefinition typeDef, SortedSet<string> namespaces)
    {
        var layoutKind = typeDef.Attributes & TypeAttributes.LayoutMask;
        var layout = typeDef.GetLayout();
        string? kind = layoutKind switch
        {
            TypeAttributes.ExplicitLayout => "LayoutKind.Explicit",
            TypeAttributes.SequentialLayout when type.Kind == "class" || layout.Size > 0 || layout.PackingSize > 0 => "LayoutKind.Sequential",
            TypeAttributes.AutoLayout when type.Kind == "struct" => "LayoutKind.Auto",
            _ => null,
        };
        if (kind is null)
            yield break;

        namespaces.Add("System.Runtime.InteropServices");
        var arguments = new List<string> { kind };

        if (layout.Size > 0)
            arguments.Add($"Size = {layout.Size}");
        if (layout.PackingSize > 0)
            arguments.Add($"Pack = {layout.PackingSize}");

        yield return $"StructLayout({string.Join(", ", arguments)})";
    }

    static IEnumerable<string> FieldLayoutAttributes(FieldDefinition field, SortedSet<string> namespaces)
    {
        int offset = field.GetOffset();
        if (offset < 0)
            yield break;

        namespaces.Add("System.Runtime.InteropServices");
        yield return $"FieldOffset({offset})";
    }

    static void ComposeFields(StringBuilder sb, MetadataReader reader, TypeDefinitionHandle typeHandle,
        SortedSet<string> namespaces, IReadOnlyDictionary<string, string> fieldInitializers, ref bool any)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var genericContext = GenericContext.ForType(reader, typeDef);
        bool wrote = false;

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string name = reader.GetString(field.Name);
            // A C# 12 primary-constructor capture field, <param>P, is referenced by
            // instance members under the parameter's source name, so it must be
            // declared (unlike auto-property backing fields, which the property
            // declaration covers). Emit it as an ordinary field named for the
            // parameter; skip the other compiler-generated <...> fields.
            string? captureName = Pipeline.CSharpNaming.PrimaryConstructorCaptureName(name);
            if (name.Contains('<') && captureName is null)
                continue; // compiler-generated backing fields
            string displayName = captureName ?? name;

            string access = (field.Attributes & FieldAttributes.FieldAccessMask) switch
            {
                FieldAttributes.Public => "public",
                FieldAttributes.Family => "protected",
                FieldAttributes.Assembly => "internal",
                FieldAttributes.FamORAssem => "protected internal",
                FieldAttributes.FamANDAssem => "private protected",
                _ => "private",
            };
            var fixedBuffer = TypeShellProducer.FixedBufferField(reader, field);
            string fieldType;
            try
            {
                fieldType = GuardedSignatureText.FieldText(reader, field, genericContext);
            }
            catch (Exception ex)
            {
                sb.AppendLf($"    // field {reader.GetString(field.Name)}: {DiagnosticIds.InternalError}: signature undecodable ({ex.GetType().Name})");
                any = true;
                continue;
            }

            foreach (var attribute in FieldLayoutAttributes(field, namespaces))
                sb.AppendLf($"    [{attribute}]");
            foreach (var attribute in AttributeReader.RenderAttributes(reader, fieldHandle, namespaces))
                sb.AppendLf($"    [{attribute}]");

            var decl = new StringBuilder($"    {access} ");
            if (fixedBuffer is not null || fieldType.Contains('*', StringComparison.Ordinal))
                decl.Append("unsafe ");
            if (field.Attributes.HasFlag(FieldAttributes.Literal))
                decl.Append("const ");
            else
            {
                if (field.Attributes.HasFlag(FieldAttributes.Static))
                    decl.Append("static ");
                if (field.Attributes.HasFlag(FieldAttributes.InitOnly))
                    decl.Append("readonly ");
            }
            // A field initializer (this.f = value) is lifted out of the
            // constructor body by the printer; render it back on the declaration.
            // const fields carry their value in metadata, not a ctor store. A
            // primary-constructor capture field renders under the parameter's
            // source name (displayName), and is assigned in the constructor body,
            // so it never carries a lifted initializer.
            string typeAndName = fixedBuffer is null
                ? $"{EscapeKnownIdentifiers(Shorten(fieldType), genericContext.TypeParameters)} {ContainedIdentifier(displayName)}"
                : fixedBuffer.DeclarationSignature(ContainedIdentifier(displayName));
            decl.Append(!field.Attributes.HasFlag(FieldAttributes.Literal)
                    && fixedBuffer is null
                    && fieldInitializers.TryGetValue(name, out var initializer)
                ? $"{typeAndName} = {initializer};"
                : $"{typeAndName};");
            sb.AppendLf(decl.ToString());
            wrote = true;
            any = true;
        }

        if (wrote)
            sb.AppendLf();
    }

    static void ComposeMembers(
        StringBuilder sb, ApiType type, Pipeline.MetadataSource pipelineSource,
        MetadataReader reader, TypeDefinitionHandle typeHandle, UnionDeclarationInfo? union,
        SortedSet<string> bodyNamespaces, ref bool any, ApiMember? only = null, Pipeline.PrinterOptions? printerOptions = null)
    {
        // Per-name running overload index — the same positional pairing the
        // member command uses for Name:N — used only when a member carries no
        // explicit raw-metadata index.
        var overloadIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        bool first = true;

        var members = union is { ExplicitConstructors.Count: > 0 }
            ? type.Members.Concat(union.ExplicitConstructors)
                .OrderBy(member => member.MetadataToken ?? int.MaxValue)
                .ToList()
            : type.Members;

        foreach (var member in members)
        {
            if (union is not null && IsHiddenUnionMember(member, union))
            {
                // Hidden union case constructors still occupy metadata overload
                // slots. Keep fallback overload counting aligned for any
                // original public members that are not synthesized below.
                if (member.Kind is "constructor" or "method" or "operator" or "explicit-interface-implementation" or "finalizer")
                    overloadIndex[member.Name] = overloadIndex.GetValueOrDefault(member.Name) + 1;
                continue;
            }

            // Single-member render (only is not null): advance the same overload
            // counting a full pass would so the target's fallback index matches,
            // but emit only the requested member. 'first' stays true, so the
            // target renders with no leading blank-line separator.
            if (only is not null && !ReferenceEquals(member, only))
            {
                if (member.Kind is "constructor" or "method" or "operator" or "explicit-interface-implementation" or "finalizer")
                    overloadIndex[member.Name] = overloadIndex.GetValueOrDefault(member.Name) + 1;
                continue;
            }

            switch (member.Kind)
            {
                case "constructor" or "method" or "operator" or "explicit-interface-implementation" or "finalizer":
                {
                    int runningIndex = overloadIndex.GetValueOrDefault(member.Name);
                    overloadIndex[member.Name] = runningIndex + 1;
                    // The replacement importer counts every metadata overload,
                    // so prefer the member's own raw index when the extractor
                    // recorded one; the running count is the fallback.
                    int index = member.DeclaringOverloadIndex is { } declaringIndex
                        ? declaringIndex - 1
                        : runningIndex;

                    if (!first) sb.AppendLf();
                    first = false;
                    any = true;

                    bool publicOnly = member.Kind is not ("explicit-interface-implementation" or "finalizer");
                    bool bodyPublicOnly = publicOnly
                        && !(member.Kind == "constructor" && member.DeclaringOverloadIndex is not null)
                        && member.Accessibility is null;
                    // Resolve the member's metadata handle once (validated
                    // against this reader) and address both its attributes and
                    // its body by it, so neither drifts onto a different overload.
                    // A non-validating token resolves the legacy name+ordinal
                    // selector to its concrete handle before any projection.
                    var memberHandle = ResolveMemberHandle(reader, typeHandle, member)
                        ?? Pipeline.IrImporter.ResolveMethodHandle(
                            reader,
                            member.DeclaringType ?? type.FullName,
                            member.Name,
                            index,
                            bodyPublicOnly);
                    var attributes = memberHandle is { } attrHandle
                        ? AttributeReader.RenderMethodAttributes(reader, attrHandle, bodyNamespaces)
                        : AttributeReader.RenderMethodAttributes(reader, typeHandle, member.Name, index, publicOnly, bodyNamespaces);
                    foreach (var attribute in attributes)
                        sb.AppendLf($"    [{attribute}]");

                    string? constructorChain = null;
                    bool requiresUnsafeContext = false;
                    bool bodyIsSingleExpressionBody = false;
                    bool bodyIsDestructor = false;
                    string? body = member.IsAbstract
                        ? null
                        : DecompileBody(pipelineSource, memberHandle, type.FullName, member, index, bodyNamespaces, out constructorChain, out requiresUnsafeContext, out bodyIsSingleExpressionBody, out bodyIsDestructor, printerOptions);

                    // An explicit interface property implementation surfaces
                    // as its accessor method (Iface.get_X). Render the
                    // property form the source writes: 'bool Iface.X => ...;'.
                    if (member.Kind == "explicit-interface-implementation"
                        && ExplicitPropertyName(member.Name) is { } propertyPath
                        && body is not null)
                    {
                        // The signature's leading token is the accessor's
                        // return type ('bool Iface.get_X()').
                        string accessorReturn = member.ReturnType
                            ?? (member.Signature is { } sig && sig.IndexOf(' ') is var sp and > 0
                                ? sig[..sp]
                                : "object");
                        string unsafeModifier = (member.IsUnsafe || requiresUnsafeContext) ? "unsafe " : "";
                        string head = $"{unsafeModifier}{EscapeKnownIdentifiers(accessorReturn, type.TypeParameters.Select(p => p.Name))} {propertyPath}";
                        if (member.Name.Contains(".set_", StringComparison.Ordinal))
                        {
                            sb.AppendLf($"    {head}");
                            sb.AppendLf("    {");
                            CSharpMemberLayout.Append(sb, "set", body, 8, WrapExpressionBodyArrow(printerOptions));
                            sb.AppendLf("    }");
                        }
                        else if (bodyIsSingleExpressionBody || CSharpExpressionBody.FromSingleStatement(body) is not null)
                        {
                            CSharpMemberLayout.Append(sb, head, body, 4, WrapExpressionBodyArrow(printerOptions), bodyIsSingleExpressionBody, DisableSignatureWrapping(printerOptions));
                        }
                        else
                        {
                            sb.AppendLf($"    {head}");
                            sb.AppendLf("    {");
                            CSharpMemberLayout.Append(sb, "get", body, 8, WrapExpressionBodyArrow(printerOptions));
                            sb.AppendLf("    }");
                        }
                        break;
                    }

                    var bodyShape = body is null
                        ? null
                        : new CSharpBlockBody(body)
                        {
                            RequiresAsyncModifier = memberHandle is { } asyncHandle
                                && TypeShellProducer.RequiresAsyncBodyModifier(reader, asyncHandle),
                            RequiresUnsafeModifier = requiresUnsafeContext,
                            // Only spell '~Type()' when the destructor pass actually
                            // recovered the canonical try/finally { base.Finalize(); }
                            // scaffold. A Finalize override whose body did not match
                            // keeps the literal 'void Finalize()' so recompiling does
                            // not silently re-inject the mandatory base call.
                            SuppressDestructorSyntax = member.IsFinalizer && !bodyIsDestructor
                        };
                    var declaration = bodyShape is null
                        ? DefaultDeclarationFormatter.FormatMember(type, member)
                        : DefaultDeclarationFormatter.FormatMemberWithBody(type, member, bodyShape);
                    AppendMember(sb, declaration, body, WrapExpressionBodyArrow(printerOptions), constructorChain, bodyIsSingleExpressionBody, DisableSignatureWrapping(printerOptions));
                    break;
                }

                case "property":
                {
                    if (!first) sb.AppendLf();
                    first = false;
                    any = true;
                    foreach (var attribute in AttributeReader.RenderPropertyAttributes(
                        reader, typeHandle, member.Name, bodyNamespaces))
                        sb.AppendLf($"    [{attribute}]");
                    ComposeProperty(sb, pipelineSource, reader, typeHandle, type, member, bodyNamespaces, printerOptions);
                    break;
                }

                case "event":
                {
                    if (!first) sb.AppendLf();
                    first = false;
                    any = true;
                    foreach (var attribute in AttributeReader.RenderEventAttributes(
                        reader, typeHandle, member.Name, bodyNamespaces))
                        sb.AppendLf($"    [{attribute}]");
                    string declaration = TerminatedDeclarationFormatter.FormatMember(type, member);
                    sb.AppendLf($"    {declaration}");
                    break;
                }
            }

            if (only is not null)
                return;
        }
    }

    static bool IsHiddenUnionMember(ApiMember member, UnionDeclarationInfo union)
        => member.MetadataToken is { } token
            && union.HiddenMethodTokens.Contains(token)
            && member.DeclaringOverloadIndex is null
            || member.Kind == "property" && IsUnionValuePropertyName(member.Name);

    static UnionDeclarationInfo? TryUnionDeclaration(MetadataReader reader, TypeDefinitionHandle typeHandle, ApiType type)
    {
        if (type.Kind != "struct" || type.IsByRefLike)
            return null;

        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!AttributeReader.HasAttribute(reader, typeDef.GetCustomAttributes(), KnownAttributeNames.UnionAttribute))
            return null;

        if (!type.Interfaces.Any(IsUnionInterface))
            return null;

        var genericContext = GenericContext.ForType(reader, typeDef);
        bool hasObjectValueGetter = false;
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            string propertyName = reader.GetString(property.Name);
            if (!IsUnionValuePropertyName(propertyName))
                continue;

            if (property.GetAccessors().Getter.IsNil)
                return null;

            try
            {
                var signature = GuardedSignatureText.PropertyText(reader, property, genericContext);
                hasObjectValueGetter = signature.ReturnType is "object" or "System.Object";
            }
            catch
            {
                return null;
            }
            break;
        }
        if (!hasObjectValueGetter)
            return null;

        var caseTypes = new List<string>();
        var hiddenMethodTokens = new HashSet<int>();
        var explicitConstructors = new List<ApiMember>();
        int constructorIndex = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != ".ctor")
                continue;
            if ((method.Attributes & MethodAttributes.Static) != 0)
                continue;

            constructorIndex++;
            var access = method.Attributes & MethodAttributes.MemberAccessMask;
            MethodSignature<string> signature;
            try
            {
                signature = GuardedSignatureText.MethodText(reader, method, GenericContext.ForMethod(reader, typeDef, method));
            }
            catch
            {
                return null;
            }
            int token = MetadataTokens.GetToken(methodHandle);
            if (access == MethodAttributes.Public && signature.ParameterTypes.Length == 1)
            {
                caseTypes.Add(signature.ParameterTypes[0]);
                hiddenMethodTokens.Add(token);
            }
            else if (Accessibility(access) is { } accessibility)
            {
                explicitConstructors.Add(new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = ConstructorSignature(reader, method, signature),
                    MetadataToken = token,
                    Accessibility = accessibility,
                    DeclaringOverloadIndex = constructorIndex
                });
            }
            else
            {
                hiddenMethodTokens.Add(token);
                explicitConstructors.Add(new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = ConstructorSignature(reader, method, signature),
                    MetadataToken = token,
                    DeclaringOverloadIndex = constructorIndex
                });
            }
        }

        if (caseTypes.Count == 0)
            return null;

        return new UnionDeclarationInfo(caseTypes, hiddenMethodTokens, explicitConstructors);
    }

    static string ConstructorSignature(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> signature)
    {
        var parameterHandles = method.GetParameters();
        var parameters = new List<string>();
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            string? name = null;
            foreach (var parameterHandle in parameterHandles)
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber == i + 1)
                {
                    name = reader.GetString(parameter.Name);
                    break;
                }
            }

            parameters.Add($"{signature.ParameterTypes[i]} {ContainedIdentifier(string.IsNullOrEmpty(name) ? $"arg{i}" : name)}");
        }

        return $"{signature.ReturnType} .ctor({string.Join(", ", parameters)})";
    }

    static string? Accessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        MethodAttributes.Public => null,
        _ => null
    };

    static bool IsUnionInterface(string interfaceName)
        => interfaceName == "System.Runtime.CompilerServices.IUnion";

    static bool IsUnionValuePropertyName(string propertyName)
        => propertyName == "Value"
            || propertyName == "System.Runtime.CompilerServices.IUnion.Value"
            || propertyName == "IUnion.Value";

    static void AddTypeNamespaces(SortedSet<string> namespaces, IEnumerable<string> typeNames)
    {
        foreach (var typeName in typeNames)
        {
            for (int i = 0; i < typeName.Length;)
            {
                if (!char.IsLetter(typeName[i]) && typeName[i] != '_')
                {
                    i++;
                    continue;
                }

                int start = i++;
                while (i < typeName.Length
                    && (char.IsLetterOrDigit(typeName[i]) || typeName[i] is '_' or '.'))
                    i++;

                string token = typeName[start..i].TrimEnd('.');
                int lastDot = token.LastIndexOf('.');
                if (lastDot > 0)
                    namespaces.Add(token[..lastDot]);
            }
        }
    }

    /// <summary>
    /// 'Iface.get_X' / 'Iface.set_X' → 'Iface.X'; null for non-accessor
    /// names (including indexer accessors, which keep the method form).
    /// </summary>
    static string? ExplicitPropertyName(string name)
    {
        foreach (var marker in (string[])[".get_", ".set_"])
        {
            int at = name.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
                continue;
            string propName = name[(at + marker.Length)..];
            if (propName.Length == 0 || propName is "Item" or "Chars")
                return null;
            return $"{EscapeQualifiedName(name[..at])}.{ContainedIdentifier(propName)}";
        }
        return null;
    }

    static void AppendMember(StringBuilder sb, string signature, string? body, bool wrapExpressionBodyArrow, string? constructorChain = null, bool bodyIsSingleExpressionBody = false, bool disableSignatureWrapping = false)
    {
        // An explicit base(...)/this(...) chain renders as a signature
        // initializer (the printer lifted it out of the body).
        string head = constructorChain is null ? signature : $"{signature} : {constructorChain}";
        CSharpMemberLayout.Append(sb, head, body, 4, wrapExpressionBodyArrow, bodyIsSingleExpressionBody, disableSignatureWrapping);
    }

    static string TypeParameterDisplayName(TypeParameter typeParameter)
        => typeParameter.Variance is { } variance
            ? $"{variance} {ContainedIdentifier(typeParameter.Name)}"
            : ContainedIdentifier(typeParameter.Name);

    static string EscapeKnownIdentifiers(string text, IEnumerable<string> rawNames)
    {
        var names = rawNames.Where(name => EscapeIdentifier(name) != name).ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                int start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;
                string token = text[start..i];
                sb.Append(names.Contains(token) ? EscapeIdentifier(token) : token);
                continue;
            }
            sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    static string EscapeQualifiedIdentifier(string name)
        => string.Join("+", name.Split('+').Select(ContainedIdentifier));

    static string EscapeQualifiedName(string name)
        => string.Join(".", name.Split('.').Select(part => string.Join("+", part.Split('+').Select(ContainedIdentifier))));

    static string EscapeIdentifier(string name) => Pipeline.CSharpNaming.EscapeIdentifier(name);

    /// <summary>The spelling for a metadata name entering emitted declaration text:
    /// unlike <see cref="EscapeIdentifier"/> this folds an unspellable name to
    /// identifier characters, so it cannot break out of the surrounding code fence
    /// (issue #3319). Gated by <c>UntrustedIdentifierPresentationTests</c>.</summary>
    static string ContainedIdentifier(string name) => Pipeline.CSharpNaming.ContainedIdentifier(name);

    /// <summary>An accessor that just passes through the auto-property backing field — `return this.Name;` or `this.Name = value;`.</summary>
    static bool IsTrivialAutoAccessor(string keyword, string? body, string name)
    {
        string escapedName = ContainedIdentifier(name);
        return keyword == "get"
            ? body?.Trim() == $"return this.{escapedName};"
            : body?.Trim() == $"this.{escapedName} = value;";
    }

    static void ComposeProperty(
        StringBuilder sb, Pipeline.MetadataSource pipelineSource,
        MetadataReader reader, TypeDefinitionHandle typeHandle, ApiType type, ApiMember member,
        SortedSet<string> bodyNamespaces, Pipeline.PrinterOptions? printerOptions)
    {
        string typeFullName = type.FullName;
        string signature = DefaultDeclarationFormatter.FormatMember(type, member);
        int accessorList = signature.IndexOf('{');
        string head = accessorList >= 0 ? signature[..accessorList].TrimEnd() : signature;
        bool requiresUnsafeContext = member.IsUnsafe || signature.Contains('*', StringComparison.Ordinal);

        var getterHandle = ResolveAccessorHandle(reader, typeHandle, member.GetterToken, $"get_{member.Name}");
        var setterHandle = ResolveAccessorHandle(reader, typeHandle, member.SetterToken, $"set_{member.Name}");

        var accessors = new List<(string Keyword, string? Body, bool RequiresUnsafeContext, bool SingleReturnExpression)>();
        if (accessorList >= 0)
        {
            string list = signature[accessorList..];
            if (list.Contains("get;", StringComparison.Ordinal))
                accessors.Add(("get", DecompileAccessor(pipelineSource, getterHandle, typeFullName, $"get_{member.Name}", bodyNamespaces, out var getRequiresUnsafe, out var getSingleReturn, printerOptions), getRequiresUnsafe, getSingleReturn));
            if (list.Contains("set;", StringComparison.Ordinal))
                accessors.Add(("set", DecompileAccessor(pipelineSource, setterHandle, typeFullName, $"set_{member.Name}", bodyNamespaces, out var setRequiresUnsafe, out var setSingleReturn, printerOptions), setRequiresUnsafe, setSingleReturn));
            if (list.Contains("init;", StringComparison.Ordinal))
                accessors.Add(("init", DecompileAccessor(pipelineSource, setterHandle, typeFullName, $"set_{member.Name}", bodyNamespaces, out var initRequiresUnsafe, out var initSingleReturn, printerOptions), initRequiresUnsafe, initSingleReturn));
        }

        if (!requiresUnsafeContext && accessors.Any(a => a.RequiresUnsafeContext))
        {
            signature = DefaultDeclarationFormatter.FormatMemberWithBody(
                type,
                member,
                new CSharpPropertyBody(null, null) { RequiresUnsafeModifier = true });
            accessorList = signature.IndexOf('{');
            head = accessorList >= 0 ? signature[..accessorList].TrimEnd() : signature;
        }

        if (accessors.Count == 0 || member.IsAbstract || accessors.All(a => a.Body is null))
        {
            sb.AppendLf(accessorList >= 0 ? $"    {head} {signature[accessorList..]}" : $"    {head}");
            return;
        }

        // Auto-property: every accessor is the compiler's trivial backing-field
        // passthrough (the body printer de-mangled <Name>k__BackingField to
        // this.Name). Render `{ get; set; }` with no bodies — decompiling them
        // would recurse (a getter that returns the property itself).
        if (accessors.All(a => IsTrivialAutoAccessor(a.Keyword, a.Body, member.Name)))
        {
            sb.AppendLf($"    {head} {{ {string.Join(" ", accessors.Select(a => $"{a.Keyword};"))} }}");
            return;
        }

        // Expression bodies per the style oracle
        // (csharp_style_expression_bodied_properties/accessors = true):
        // a lone getter returning one expression is 'head => expr;', and any
        // single-statement accessor is 'get/set => ...;'. A lone getter whose
        // body is one multi-line 'return <expr>;' also folds to an expression
        // body (a raised switch return, issue #3088; a wrapped single expression,
        // issue #3084), gated on the printer's typed single-return signal.
        if (accessors is [("get", { } loneGet, _, var loneGetSingleReturn)]
            && (loneGetSingleReturn || CSharpExpressionBody.FromSingleStatement(loneGet) is not null))
        {
            CSharpMemberLayout.Append(sb, head, loneGet, 4, WrapExpressionBodyArrow(printerOptions), loneGetSingleReturn, DisableSignatureWrapping(printerOptions));
            return;
        }

        sb.AppendLf($"    {head}");
        sb.AppendLf("    {");
        for (int i = 0; i < accessors.Count; i++)
        {
            var (keyword, body, _, singleReturn) = accessors[i];
            if (i > 0) sb.AppendLf();
            CSharpMemberLayout.Append(sb, keyword, body, 8, WrapExpressionBodyArrow(printerOptions), singleReturn);
        }
        sb.AppendLf("    }");
    }

    static bool WrapExpressionBodyArrow(Pipeline.PrinterOptions? printerOptions)
        => (printerOptions ?? Pipeline.PrinterOptions.Default).WrapExpressionBodyArrow;

    static bool DisableSignatureWrapping(Pipeline.PrinterOptions? printerOptions)
        => (printerOptions ?? Pipeline.PrinterOptions.Default).DisableOneLinerWrapping;

    /// <summary>
    /// The expression of a single-statement body suitable for '=>':
    /// 'return X;' yields X; a lone statement yields itself without ';'.
    /// </summary>
    /// <summary>
    /// Gathers field initializers (<c>this.f = value</c> stores the printer lifts
    /// out of a constructor body to the field declarations) so
    /// <see cref="ComposeFields"/> can render them. Instance constructors are
    /// enumerated straight from metadata — not from <see cref="ApiType.Members"/>,
    /// which omits non-public constructors, so a factory type whose only
    /// constructor is private still recovers its initializers. Each constructor is
    /// imported only to read <see cref="DecompilerResult.FieldInitializers"/>;
    /// <see cref="ComposeMembers"/> renders the (now initializer-free) bodies
    /// separately. Initializers are identical across base-chaining constructors, so
    /// the first one seen for a field wins. The static constructor (<c>.cctor</c>)
    /// is skipped: its stores are not lifted (no base chain, no <c>this</c>).
    /// </summary>
    static Dictionary<string, string> CollectFieldInitializers(
        Pipeline.MetadataSource pipelineSource,
        MetadataReader reader, TypeDefinitionHandle typeHandle)
    {
        var initializers = new Dictionary<string, string>(StringComparer.Ordinal);
        var typeDef = reader.GetTypeDefinition(typeHandle);

        // Address each constructor by its metadata handle directly — the
        // canonical same-reader addressing (see docs/design/member-body-substrate.md).
        foreach (var methodHandle in typeDef.GetMethods())
        {
            if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) != ".ctor")
                continue;

            var function = Pipeline.IrImporter.Import(pipelineSource, methodHandle);
            if (function is null)
                continue;

            var result = Pipeline.CSharpPrinter.PrintRaised(
                function, importMethodBody: method => Pipeline.IrImporter.Import(pipelineSource, method),
                typesProvablyDisjoint: pipelineSource.AreProvablyDisjoint);
            foreach (var (field, value) in result.FieldInitializers)
                initializers.TryAdd(field, value);
        }

        return initializers;
    }

    static string? DecompileBody(
        Pipeline.MetadataSource pipelineSource, MethodDefinitionHandle? memberHandle,
        string typeFullName, ApiMember member, int overloadIndex,
        SortedSet<string> bodyNamespaces, out string? constructorChain, out bool requiresUnsafeContext,
        out bool bodyIsSingleExpressionBody, out bool bodyIsDestructor, Pipeline.PrinterOptions? printerOptions)
    {
        // Prefer the member's own metadata handle — the canonical same-reader
        // addressing (see docs/design/member-body-substrate.md). The caller has
        // already validated the token against this reader (a method of this type
        // with this name); a stale token from a type-forwarded surface resolves
        // to null and falls back to the name+ordinal path rather than
        // mis-addressing.
        if (memberHandle is { } methodHandle)
            return DecompileFunction(pipelineSource,
                Pipeline.IrImporter.Import(pipelineSource, methodHandle),
                bodyNamespaces, out constructorChain, out requiresUnsafeContext, out bodyIsSingleExpressionBody, out bodyIsDestructor, printerOptions);

        // Public-only overload counting, except explicit interface
        // implementations (non-public by nature) — matching the API surface
        // ordering the running index is built from.
        return DecompileMethod(pipelineSource, member.DeclaringType ?? typeFullName, member.Name, overloadIndex,
            publicOnly: member.Kind != "explicit-interface-implementation"
                && !(member.Kind == "constructor" && member.DeclaringOverloadIndex is not null)
                && member.Accessibility is null,
            bodyNamespaces, out constructorChain, out requiresUnsafeContext, out bodyIsSingleExpressionBody, out bodyIsDestructor, printerOptions);
    }

    /// <summary>
    /// Resolves a member to its <see cref="MethodDefinitionHandle"/> in
    /// <paramref name="reader"/>, or null when the member carries no metadata
    /// token or the token does not validate to a method of
    /// <paramref name="typeHandle"/> with the member's name (e.g. a token
    /// carried over from a type-forwarded surface). A null result asks the
    /// caller to fall back to name+ordinal addressing.
    /// </summary>
    static MethodDefinitionHandle? ResolveMemberHandle(MetadataReader reader, TypeDefinitionHandle typeHandle, ApiMember member)
    {
        if (ResolveMethodHandle(reader, typeHandle, member.MetadataToken) is not { } handle)
            return null;
        if (reader.GetString(reader.GetMethodDefinition(handle).Name) != member.Name)
            return null;
        return handle;
    }

    /// <summary>
    /// Resolves a raw metadata token to a <see cref="MethodDefinitionHandle"/>
    /// that belongs to <paramref name="typeHandle"/> in
    /// <paramref name="reader"/>, or null when the token is absent, is not a
    /// method definition, or does not belong to the type (e.g. carried over from
    /// a type-forwarded surface). The shared validation behind member and
    /// accessor handle addressing.
    /// </summary>
    static MethodDefinitionHandle? ResolveMethodHandle(MetadataReader reader, TypeDefinitionHandle typeHandle, int? token)
    {
        if (token is not { } value)
            return null;
        var handle = MetadataTokens.EntityHandle(value);
        if (handle.Kind != HandleKind.MethodDefinition)
            return null;
        var methodHandle = (MethodDefinitionHandle)handle;
        MethodDefinition method;
        try
        {
            method = reader.GetMethodDefinition(methodHandle);
        }
        catch
        {
            return null;
        }
        return method.GetDeclaringType() == typeHandle ? methodHandle : null;
    }

    /// <summary>
    /// Resolves a property accessor token to its <see cref="MethodDefinitionHandle"/>,
    /// applying the same rigor as <see cref="ResolveMemberHandle"/>: the token must
    /// resolve to a method of <paramref name="typeHandle"/> whose name equals the
    /// expected accessor name (e.g. <c>get_Item</c>). A stale token carried over
    /// from a type-forwarded or round-tripped surface that lands on a different
    /// method of the same type — a private helper or a sibling property's accessor —
    /// is rejected, asking the caller to fall back to name+ordinal addressing rather
    /// than decompiling an unrelated body as the accessor.
    /// </summary>
    static MethodDefinitionHandle? ResolveAccessorHandle(MetadataReader reader, TypeDefinitionHandle typeHandle, int? token, string accessorName)
    {
        if (ResolveMethodHandle(reader, typeHandle, token) is not { } handle)
            return null;
        if (reader.GetString(reader.GetMethodDefinition(handle).Name) != accessorName)
            return null;
        return handle;
    }

    static string? DecompileAccessor(
        Pipeline.MetadataSource pipelineSource, MethodDefinitionHandle? accessorHandle,
        string typeFullName, string accessorName,
        SortedSet<string> bodyNamespaces, out bool requiresUnsafeContext,
        out bool bodyIsSingleExpressionBody, Pipeline.PrinterOptions? printerOptions)
        // Prefer the accessor's own handle (fixes indexer get_Item/set_Item
        // drift, where name+index:0 always selects the first indexer's
        // accessor). Fall back to the by-name path — accessors are non-public
        // special-name methods, counted across all visibilities — when no valid
        // handle is available.
        => accessorHandle is { } handle
            ? DecompileFunction(pipelineSource,
                Pipeline.IrImporter.Import(pipelineSource, handle),
                bodyNamespaces, out _, out requiresUnsafeContext, out bodyIsSingleExpressionBody, out _, printerOptions)
            : DecompileMethod(pipelineSource, typeFullName, accessorName, overloadIndex: 0,
            publicOnly: false, bodyNamespaces, out _, out requiresUnsafeContext, out bodyIsSingleExpressionBody, out _, printerOptions);

    /// <summary>
    /// Imports one method to typed IR, runs the raising passes, and prints the
    /// body. A null import means no IL body
    /// (abstract/extern) — nothing to render, not an error. PrintRaised never
    /// throws; an import or pass failure surfaces as an honest diagnostic. The
    /// types the body references contribute their namespaces to the listing's
    /// using block (the printer renders simple names).
    /// </summary>
    static string? DecompileMethod(
        Pipeline.MetadataSource pipelineSource, string typeFullName, string methodName, int overloadIndex,
        bool publicOnly, SortedSet<string> bodyNamespaces, out string? constructorChain, out bool requiresUnsafeContext,
        out bool bodyIsSingleExpressionBody, out bool bodyIsDestructor, Pipeline.PrinterOptions? printerOptions)
        => DecompileFunction(pipelineSource,
            Pipeline.IrImporter.Import(pipelineSource, typeFullName, methodName, overloadIndex, publicOnly),
            bodyNamespaces, out constructorChain, out requiresUnsafeContext, out bodyIsSingleExpressionBody, out bodyIsDestructor, printerOptions);

    /// <summary>
    /// Runs the raising passes and prints an already-imported function. A null
    /// function means no IL body (abstract/extern) — nothing to render, not an
    /// error. The two addressing front doors (handle-direct and name+ordinal)
    /// share this body so they render identically.
    /// </summary>
    static string? DecompileFunction(
        Pipeline.MetadataSource pipelineSource, Pipeline.IrFunction? function,
        SortedSet<string> bodyNamespaces, out string? constructorChain, out bool requiresUnsafeContext,
        out bool bodyIsSingleExpressionBody, out bool bodyIsDestructor, Pipeline.PrinterOptions? printerOptions)
    {
        constructorChain = null;
        requiresUnsafeContext = false;
        bodyIsSingleExpressionBody = false;
        bodyIsDestructor = false;
        if (function is null)
            return null;
        CollectNamespaces(function, bodyNamespaces);
        var result = Pipeline.CSharpPrinter.PrintRaised(
            function, importMethodBody: method => Pipeline.IrImporter.Import(pipelineSource, method), printerOptions,
            typesProvablyDisjoint: pipelineSource.AreProvablyDisjoint);
        constructorChain = result.ConstructorChain;
        requiresUnsafeContext = result.RequiresUnsafeBodyModifier;
        bodyIsSingleExpressionBody = result.BodyIsSingleExpressionBody;
        bodyIsDestructor = result.BodyIsDestructor;
        return result.Output?.TrimEnd() ?? DiagnosticComment(result);
    }

    /// <summary>
    /// Unions the namespaces of every definition type the function references
    /// — the same descendant walk the importer uses to resolve type shapes —
    /// into the listing's using set. The printer emits simple names, so any
    /// referenced type needs its namespace imported (or it would not bind).
    /// Over-collection is harmless; an unused using is only a style nit, while
    /// a missing one would not compile.
    /// </summary>
    static void CollectNamespaces(Pipeline.IrFunction function, SortedSet<string> namespaces)
    {
        void Add(Pipeline.TypeRef? type)
        {
            switch (type?.Kind)
            {
                case Pipeline.TypeRefKind.Definition:
                    if (type.Namespace.Length > 0)
                        namespaces.Add(type.Namespace);
                    break;
                case Pipeline.TypeRefKind.GenericInstance:
                    Add(type.ElementType);
                    foreach (var argument in type.TypeArguments)
                        Add(argument);
                    break;
                case Pipeline.TypeRefKind.SzArray or Pipeline.TypeRefKind.Array
                    or Pipeline.TypeRefKind.ByRef or Pipeline.TypeRefKind.Pointer or Pipeline.TypeRefKind.Pinned:
                    Add(type.ElementType);
                    break;
            }
        }

        // Prepend the function node itself: its DirectTypes carry the local,
        // parameter, and return types that no descendant surfaces (a declared
        // local the printer renders but the body never loads by value).
        foreach (var node in function.Descendants.Prepend(function))
        {
            foreach (var type in node.DirectTypes)
                Add(type);
            if (node is Pipeline.IrExpression expression)
                Add(expression.ResultType);
        }
    }

    static string DiagnosticComment(DecompilerResult result)
        => string.Join("\n", result.Diagnostics.Select(d => $"// {d}"));

    /// <summary>
    /// Shortens qualified type names against the assembly's own metadata
    /// (TypeDefs + TypeRefs give the real namespace/type tables — no
    /// guessing about what is a namespace) and hoists the namespaces used
    /// into a using block. Ambiguous short names stay qualified; string
    /// literal contents are never rewritten; namespaces covered by implicit
    /// usings and the type's own namespace are shortened without a using.
    /// </summary>
    static string HoistUsings(string listing, MetadataReader reader, string? ownNamespace, SortedSet<string> seedNamespaces)
    {
        // The IR-collected body namespaces seed the set; text shortening of
        // the declaration lines adds any it harvests from qualified prefixes.
        var usings = new SortedSet<string>(seedNamespaces, StringComparer.Ordinal);
        string result = ShortenQualifiedNames(listing, reader, usings);

        // Implicit usings (and the type's own namespace) need no directive.
        usings.Remove(ownNamespace ?? "");
        foreach (var implicitNs in (string[])
            ["System", "System.Collections.Generic", "System.IO", "System.Linq",
             "System.Net.Http", "System.Threading", "System.Threading.Tasks"])
        {
            usings.Remove(implicitNs);
        }

        if (usings.Count == 0)
            return result;

        // dotnet/runtime style: using directives precede the namespace. The
        // harvested namespaces are raw metadata strings with no keyword escapes,
        // so a segment that is a C# keyword (e.g. "event" in System.event.Models)
        // must be @-escaped or the directive is invalid C# — the same escape the
        // sibling CSharpTypePrinter applies to its own usings.
        string directives = string.Join('\n', usings.Select(ns => $"using {CSharpFormatter.EscapeNamespace(ns)};"));
        return $"{directives}\n\n{result}";
    }

    /// <summary>
    /// Shortens qualified type names in <paramref name="listing"/> to simple
    /// names, harvesting the namespaces that need importing into
    /// <paramref name="usings"/>, and normalizes to one deterministic <c>\n</c>
    /// newline per line. Shared by the whole-type <see cref="HoistUsings"/> (which
    /// then prepends the directives) and the per-member
    /// <see cref="ComposeMemberCore"/> (whose caller emits the imports), so a
    /// single member renders byte-identically to its whole-type listing segment.
    /// </summary>
    static string ShortenQualifiedNames(string listing, MetadataReader reader, SortedSet<string> usings)
    {
        // Namespace → simple type names (arity-stripped), from real metadata.
        var nsToNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Register(string ns, string name)
        {
            if (ns.Length == 0)
                return;
            int tick = name.IndexOf('`');
            bool generic = tick >= 0;
            if (generic)
                name = name[..tick];
            if (!nsToNames.TryGetValue(ns, out var names))
                nsToNames[ns] = names = new HashSet<string>(StringComparer.Ordinal);
            names.Add(generic ? name + "<" : name);
        }
        foreach (var h in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(h);
            Register(reader.GetString(td.Namespace), reader.GetString(td.Name));
        }
        foreach (var h in reader.TypeReferences)
        {
            var tr = reader.GetTypeReference(h);
            Register(reader.GetString(tr.Namespace), reader.GetString(tr.Name));
        }

        // A short name imported from two namespaces would be ambiguous —
        // but generic and non-generic names are distinct in C#, so owners
        // are counted per (name, arity-kind). Registration tracked the kind
        // via the metadata arity suffix.
        var shortNameOwners = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, names) in nsToNames)
            foreach (var n in names)
                shortNameOwners[n] = shortNameOwners.GetValueOrDefault(n) + 1;

        // The emitter strips "System." eagerly, so qualified occurrences may
        // appear either in full or with the System. prefix already removed —
        // register both spellings for each namespace.
        var prefixes = new List<(string Text, string Namespace)>();
        foreach (var ns in nsToNames.Keys)
        {
            prefixes.Add((ns, ns));
            if (ns.StartsWith("System.", StringComparison.Ordinal))
                prefixes.Add((ns[7..], ns));
        }
        // Longest first so System.Collections.Generic wins over System.Collections.
        prefixes.Sort((a, b) => b.Text.Length.CompareTo(a.Text.Length));

        var output = new StringBuilder(listing.Length);
        foreach (var rawLine in listing.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            // Never rewrite literal contents: shorten only the code spans
            // outside string/char literals, honoring backslash escapes.
            ShortenLine(line, output, prefixes, nsToNames, shortNameOwners, usings);
            // Generated source uses one deterministic newline on every host.
            output.Append('\n');
        }

        return output.ToString().TrimEnd();
    }

    /// <summary>
    /// Shortens qualified names in the code spans of one line, copying string
    /// (<c>"…"</c>) and character (<c>'…'</c>) literals verbatim. The scan honors
    /// backslash escapes, so an escaped quote (<c>\"</c>) inside a literal — or a
    /// quote character constant (<c>'"'</c>) — does not end the literal early and
    /// expose its contents to shortening, which would corrupt the constant (a
    /// naive split on <c>"</c> flips its in-literal parity). Interpolated strings
    /// (<c>$"…"</c>) are modeled structurally by <see cref="CopyInterpolatedString"/>:
    /// their literal text (with <c>{{</c>/<c>}}</c> brace escapes) and each hole's
    /// format clause are copied verbatim, while a hole's expression is scanned as
    /// code — so a qualified name is shortened inside a hole but a nested string
    /// literal inside a hole (<c>$"{Echo("System.String")}"</c>) is preserved
    /// rather than mis-segmented and corrupted. The decompiler never emits
    /// verbatim (<c>@"…"</c>) or raw (<c>"""…"""</c>) literals — every control
    /// character and quote is backslash-escaped and no literal spans lines — so a
    /// backslash-aware single-line scan is sufficient.
    /// </summary>
    static void ShortenLine(
        string line,
        StringBuilder output,
        List<(string Text, string Namespace)> prefixes,
        Dictionary<string, HashSet<string>> nsToNames,
        Dictionary<string, int> shortNameOwners,
        SortedSet<string> usings)
    {
        int i = 0;
        int codeStart = 0;
        while (i < line.Length)
        {
            char c = line[i];
            // An interpolated string ($"…") interleaves literal text with code
            // holes; scan it structurally so holes shorten but their literal
            // text and any nested literals are preserved.
            if (c == '$' && i + 1 < line.Length && line[i + 1] == '"')
            {
                output.Append(ShortenSegment(line[codeStart..i], prefixes, nsToNames, shortNameOwners, usings));
                CopyInterpolatedString(line, ref i, output, prefixes, nsToNames, shortNameOwners, usings);
                codeStart = i;
                continue;
            }
            if (c is '"' or '\'')
            {
                // Flush the shortened code span preceding this literal, then copy
                // the literal verbatim (honoring backslash escapes).
                output.Append(ShortenSegment(line[codeStart..i], prefixes, nsToNames, shortNameOwners, usings));
                CopyLiteral(line, ref i, output);
                codeStart = i;
                continue;
            }
            i++;
        }
        if (codeStart < line.Length)
            output.Append(ShortenSegment(line[codeStart..], prefixes, nsToNames, shortNameOwners, usings));
    }

    /// <summary>
    /// Copies a string (<c>"…"</c>) or character (<c>'…'</c>) literal starting at
    /// <paramref name="i"/> verbatim, honoring backslash escapes so an escaped
    /// delimiter (<c>\"</c>) does not terminate it early. Advances
    /// <paramref name="i"/> past the closing delimiter (or to the end of the line
    /// if the literal is unterminated).
    /// </summary>
    static void CopyLiteral(string line, ref int i, StringBuilder output)
    {
        char delimiter = line[i];
        output.Append(delimiter);
        i++;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                output.Append(c).Append(line[i + 1]);
                i += 2;
                continue;
            }
            output.Append(c);
            i++;
            if (c == delimiter)
                break;
        }
    }

    /// <summary>
    /// Copies an interpolated string (<c>$"…"</c>) starting at <paramref name="i"/>
    /// (the <c>$</c>), advancing past the closing quote. Literal text — including
    /// <c>{{</c>/<c>}}</c> brace escapes — is copied verbatim, and each hole is
    /// handed to <see cref="ShortenHole"/> so its code is shortened while its
    /// nested literals and format clause are preserved. Only non-verbatim
    /// interpolated strings are emitted, so a backslash escapes the next character
    /// within the literal-text runs.
    /// </summary>
    static void CopyInterpolatedString(
        string line,
        ref int i,
        StringBuilder output,
        List<(string Text, string Namespace)> prefixes,
        Dictionary<string, HashSet<string>> nsToNames,
        Dictionary<string, int> shortNameOwners,
        SortedSet<string> usings)
    {
        output.Append('$').Append('"');
        i += 2;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                output.Append(c).Append(line[i + 1]);
                i += 2;
                continue;
            }
            if (c == '{')
            {
                // '{{' is a literal open-brace escape, not a hole.
                if (i + 1 < line.Length && line[i + 1] == '{')
                {
                    output.Append("{{");
                    i += 2;
                    continue;
                }
                ShortenHole(line, ref i, output, prefixes, nsToNames, shortNameOwners, usings);
                continue;
            }
            if (c == '}')
            {
                // '}}' is a literal close-brace escape.
                if (i + 1 < line.Length && line[i + 1] == '}')
                {
                    output.Append("}}");
                    i += 2;
                    continue;
                }
                output.Append(c);
                i++;
                continue;
            }
            output.Append(c);
            i++;
            if (c == '"')
                return;
        }
    }

    /// <summary>
    /// Scans one interpolation hole (<c>{expression[,alignment][:format]}</c>)
    /// starting at the opening <c>{</c> in <paramref name="i"/>, advancing past
    /// the matching <c>}</c>. The expression is shortened as code — skipping
    /// nested string/char literals and nested interpolated strings — while the
    /// format clause after an unnested <c>:</c> is literal text copied verbatim.
    /// Brace/paren/bracket nesting is tracked so an object-initializer brace or a
    /// parenthesized conditional's <c>:</c> inside the hole neither closes the
    /// hole nor masquerades as the format separator; the alias qualifier
    /// <c>::</c> is likewise not a format separator. Braces never appear in a
    /// format spec (<c>MemberIdentity</c> keeps such handlers lowered), so the
    /// first <c>}</c> after the format <c>:</c> closes the hole.
    /// </summary>
    static void ShortenHole(
        string line,
        ref int i,
        StringBuilder output,
        List<(string Text, string Namespace)> prefixes,
        Dictionary<string, HashSet<string>> nsToNames,
        Dictionary<string, int> shortNameOwners,
        SortedSet<string> usings)
    {
        output.Append('{');
        i++;
        int codeStart = i;
        int depth = 0;
        while (i < line.Length)
        {
            char c = line[i];
            // A nested interpolated string owns its own literal/hole scanning.
            if (c == '$' && i + 1 < line.Length && line[i + 1] == '"')
            {
                output.Append(ShortenSegment(line[codeStart..i], prefixes, nsToNames, shortNameOwners, usings));
                CopyInterpolatedString(line, ref i, output, prefixes, nsToNames, shortNameOwners, usings);
                codeStart = i;
                continue;
            }
            if (c is '"' or '\'')
            {
                output.Append(ShortenSegment(line[codeStart..i], prefixes, nsToNames, shortNameOwners, usings));
                CopyLiteral(line, ref i, output);
                codeStart = i;
                continue;
            }
            if (c is '(' or '[' or '{')
            {
                depth++;
                i++;
                continue;
            }
            if (c is ')' or ']')
            {
                if (depth > 0)
                    depth--;
                i++;
                continue;
            }
            if (c == '}')
            {
                if (depth == 0)
                {
                    // The hole closes: flush its remaining code, then copy '}'.
                    output.Append(ShortenSegment(line[codeStart..i], prefixes, nsToNames, shortNameOwners, usings));
                    output.Append('}');
                    i++;
                    return;
                }
                depth--;
                i++;
                continue;
            }
            if (c == ':' && depth == 0)
            {
                // '::' is the alias qualifier (global::), not a format separator.
                if (i + 1 < line.Length && line[i + 1] == ':')
                {
                    i += 2;
                    continue;
                }
                // The format clause is literal text up to the hole's '}'.
                output.Append(ShortenSegment(line[codeStart..i], prefixes, nsToNames, shortNameOwners, usings));
                output.Append(':');
                i++;
                while (i < line.Length && line[i] != '}')
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        output.Append(line[i]).Append(line[i + 1]);
                        i += 2;
                        continue;
                    }
                    output.Append(line[i]);
                    i++;
                }
                if (i < line.Length)
                {
                    output.Append('}');
                    i++;
                }
                return;
            }
            i++;
        }
        // Unterminated hole (malformed output): flush whatever remains as code.
        if (codeStart < line.Length)
            output.Append(ShortenSegment(line[codeStart..], prefixes, nsToNames, shortNameOwners, usings));
    }

    static string ShortenSegment(
        string segment,
        List<(string Text, string Namespace)> prefixes,
        Dictionary<string, HashSet<string>> nsToNames,
        Dictionary<string, int> shortNameOwners,
        SortedSet<string> usings)
    {
        foreach (var (text, ns) in prefixes)
        {
            int searchFrom = 0;
            while (true)
            {
                int at = segment.IndexOf(text + ".", searchFrom, StringComparison.Ordinal);
                if (at < 0)
                    break;
                searchFrom = at + 1;

                // Word boundary before the prefix.
                if (at > 0 && (char.IsLetterOrDigit(segment[at - 1]) || segment[at - 1] is '_' or '.'))
                    continue;
                // An alias-qualified name must keep its full path; shortening
                // any part of it re-introduces the shadowing collision the
                // global:: was emitted to avoid and does not bind (CS0400 /
                // CS0234). Only the System.-stripped prefix spelling can match
                // mid-chain (e.g. "event.Models" inside
                // global::System.@event.Models.X, after "System.@"), so a check
                // of the characters immediately before the match is not enough:
                // walk back over the whole qualified run (identifier chars, '.',
                // and the '@' keyword escape) to the token that roots it. If that
                // token is the two-char '::' alias qualifier, the match belongs
                // to an alias-rooted chain — decline.
                int root = at;
                while (root > 0 && (char.IsLetterOrDigit(segment[root - 1]) || segment[root - 1] is '_' or '.' or '@'))
                    root--;
                if (root >= 2 && segment[root - 1] == ':' && segment[root - 2] == ':')
                    continue;

                // The identifier after the prefix must be a type from this
                // namespace, fully present (next char ends the identifier).
                int nameStart = at + text.Length + 1;
                int nameEnd = nameStart;
                while (nameEnd < segment.Length && (char.IsLetterOrDigit(segment[nameEnd]) || segment[nameEnd] == '_'))
                    nameEnd++;
                if (nameEnd == nameStart)
                    continue;
                string name = segment[nameStart..nameEnd];
                bool generic = nameEnd < segment.Length && segment[nameEnd] == '<';
                string key = generic ? name + "<" : name;
                if (!nsToNames[ns].Contains(key))
                    continue;
                // Ambiguity: a (name, arity-kind) owned by more than one
                // namespace stays qualified.
                if (shortNameOwners.GetValueOrDefault(key) > 1)
                    continue;

                segment = segment[..at] + segment[nameStart..];
                usings.Add(ns);
                searchFrom = at;
            }
        }
        return segment;
    }

    static string Shorten(string typeName) =>
        typeName.StartsWith("System.", StringComparison.Ordinal) && typeName.IndexOf('.', 7) < 0
            ? typeName[7..]
            : typeName;
}
