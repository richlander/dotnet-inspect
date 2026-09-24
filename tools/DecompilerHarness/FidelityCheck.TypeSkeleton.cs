using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

using CSharpText;
using DotnetInspector.Services;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The semantic-fidelity check (validity is <see cref="ValidityCheck"/>;
/// completeness is the <c>--gaps</c> floor).
/// It closes the loop named in docs/decompiler.md: decompile → recompile →
/// compare IL. A decompiled body that compiles and reads plausibly but recompiles
/// to a different contract body changed the measured program shape
/// (docs/decompiler-taste.md), invisible to the validity check.
///
/// Unlike <see cref="ValidityCheck"/>'s per-method <c>__Shell</c> — which cannot
/// see the declaring type's fields, so any <c>this.field</c> reference fails to
/// bind as noise — this recompiles each member inside a reconstructed shape of
/// its REAL declaring type: the type declaration, every field, every sibling and
/// nested member as a throwing stub, and the one target member's real decompiled
/// body. The C# analog of the IL round-trip suite's full-skeleton scaffold
/// (IlasmScaffold.BuildCompilationUnit). Fields in scope mean a dropped or
/// mis-bound field access surfaces as a body diff, not a compile error.
/// </summary>
static partial class FidelityCheck
{

    // ---- Type-skeleton emission (the C# analog of IlasmScaffold) ----

    /// <summary>
    /// Emits a compilation unit for the WHOLE module: every top-level type
    /// reconstructed with all fields and all members as throwing stubs, except
    /// the one <paramref name="target"/> method (in type <paramref name="targetType"/>),
    /// which carries its real decompiled <paramref name="targetBody"/>. Nested
    /// types are emitted recursively. The C# analog of the IL round-trip suite's
    /// full-skeleton scaffold (IlasmScaffold.BuildCompilationUnit): with every
    /// sibling type and every internal member present (and public), the target
    /// body's references to same-assembly types and members all bind — so a
    /// dropped or mis-bound access surfaces as a true opcode diff, not CS0234.
    /// Sibling stubs whose signatures expose inaccessible referenced types are
    /// skipped because C# cannot spell those signatures from the compile-back
    /// assembly.
    /// </summary>
    /// <summary>The real decompiled body (and optional ctor chain) for one target method.</summary>
    public readonly record struct TargetBody(
        string Body,
        string? Chain,
        bool RequiresAsync,
        PrimaryConstructorShape? PrimaryConstructor = null,
        IReadOnlySet<string>? RequiredNamespaces = null,
        // The product's whole-member render (signature + body) for a
        // migrated target, replacing the harness's self-spelled signature. Null
        // keeps the legacy EmitMethod path. WholeMemberNamespaces are the imports
        // the render shortened against, hoisted into the compile-back unit usings.
        string? WholeMember = null,
        IReadOnlySet<string>? WholeMemberNamespaces = null);

    public sealed record PrimaryConstructorShape(
        string Parameters,
        IReadOnlyList<(string Field, string Value)> FieldInitializers);

    readonly record struct BuiltUnit(
        string Source,
        IReadOnlySet<MethodDefinitionHandle> ProductWholeMembers);

    /// <summary>Single-method unit — the per-method fallback path when a grouped build fails.</summary>
    static BuiltUnit BuildUnit(MetadataReader reader, MethodDefinitionHandle target, TargetBody targetBody,
        IReadOnlyList<(string Field, string Value)> targetFieldInits,
        SignatureSpellability accessibility,
        IReadOnlySet<TypeDefinitionHandle>? includeRoots = null)
    {
        var targets = new Dictionary<MethodDefinitionHandle, TargetBody> { [target] = targetBody };
        var fieldInitType = reader.GetMethodDefinition(target).GetDeclaringType();
        return BuildUnit(reader, targets, targetFieldInits, fieldInitType, accessibility, includeRoots, targetBody.RequiredNamespaces);
    }

    /// <summary>
    /// Grouped unit — every <paramref name="targets"/> method carries its real
    /// decompiled body in one compilation, so a whole type's methods recompile
    /// together (a sibling method's body never affects another's emitted IL — only
    /// signatures and fields do — so grouping is opcode-equivalent to the
    /// per-method build, for far fewer compiler invocations). Field initializers
    /// apply to <paramref name="fieldInitType"/> (the type whose constructor lifted
    /// them); they only change constructor IL, so non-constructor targets are
    /// indifferent to them.
    /// </summary>
    static BuiltUnit BuildUnit(MetadataReader reader, IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        IReadOnlyList<(string Field, string Value)> fieldInits, TypeDefinitionHandle fieldInitType,
        SignatureSpellability accessibility, IReadOnlySet<TypeDefinitionHandle>? includeRoots = null,
        IReadOnlySet<string>? isolatedTargetNamespaces = null)
    {
        var sb = new StringBuilder();
        var productWholeMembers = new HashSet<MethodDefinitionHandle>();
        sb.AppendLine("#pragma warning disable");
        // The product printer spells framework types by their short name
        // (`List<T>`, `PEReader`, `AssemblyReferenceHandle`), assuming the standard
        // decompiler-output using set. The skeleton imports the same namespaces so
        // those short names bind instead of failing CS0246 and poisoning the
        // whole-module compile. Kept conservative — only widely-assumed, low-
        // collision namespaces — so a body's short name resolves without
        // introducing CS0104 ambiguity.
        var usings = new SortedSet<string>(SkeletonUsings, StringComparer.Ordinal);
        if (isolatedTargetNamespaces is not null)
            foreach (var ns in isolatedTargetNamespaces)
                usings.Add(ns);
        // A target rendered by the product (ProduceMember) shortens
        // qualified type names against the decompiler's assumed imports; add the
        // namespaces it harvested so those short names bind in the compile-back unit.
        foreach (var t in targets.Values)
            if (t.WholeMemberNamespaces is { } wholeMemberNamespaces)
                foreach (var ns in wholeMemberNamespaces)
                    usings.Add(ns);
        foreach (var ns in usings)
            sb.AppendLine($"using {EscapeNamespace(ns)};");
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.GetDeclaringType().IsNil)
                continue; // nested types are emitted by their enclosing type
            if (includeRoots is not null && !includeRoots.Contains(typeHandle))
                continue; // cluster mode: emit only the reconstruction-closure roots
            string name = reader.GetString(typeDef.Name);
            if (name.Contains('<') || name == "<Module>")
                continue; // compiler-generated / module pseudo-type
            if (IsCompilerEmbeddedAttributeType(reader, typeDef))
                continue;
            string ns = reader.GetString(typeDef.Namespace);
            if (ns.Length > 0)
            {
                sb.AppendLine($"namespace {EscapeNamespace(ns)}");
                sb.AppendLine("{");
                EmitType(
                    reader,
                    typeHandle,
                    targets,
                    fieldInits,
                    fieldInitType,
                    accessibility,
                    productWholeMembers,
                    sb,
                    1);
                sb.AppendLine("}");
            }
            else
            {
                EmitType(
                    reader,
                    typeHandle,
                    targets,
                    fieldInits,
                    fieldInitType,
                    accessibility,
                    productWholeMembers,
                    sb,
                    0);
            }
        }
        return new BuiltUnit(sb.ToString(), productWholeMembers);
    }

    /// <summary>
    /// A <c>: Base</c> clause for a class whose base is a non-generic type in
    /// this assembly (so its constructors are visible to a lifted
    /// <c>: base(args)</c> initializer). Object and value-type bases need no
    /// clause; generic bases (TypeSpec) and out-of-assembly bases are skipped
    /// except for framework bases the skeleton must preserve for C# semantics.
    /// <see cref="System.Attribute"/> keeps reconstructed custom-attribute types
    /// usable, and <see cref="System.Exception"/> preserves constructor chains.
    /// </summary>
    static string BaseClause(MetadataReader reader, TypeDefinition typeDef, TypeKind kind)
    {
        if (kind != TypeKind.Class || typeDef.BaseType.IsNil)
            return "";
        if (FullName(reader, typeDef) == "Aspire.Hosting.ApplicationModel.ResourceAnnotationCollection")
            return " : System.Collections.ObjectModel.Collection<Aspire.Hosting.ApplicationModel.IResourceAnnotation>";
        if (typeDef.BaseType.Kind == HandleKind.TypeDefinition)
        {
            var baseDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType);
            if (baseDef.GetGenericParameters().Count != 0)
                return "";
        }
        else if (typeDef.BaseType.Kind == HandleKind.TypeSpecification)
        {
            return GenericBaseClause(reader, typeDef.BaseType);
        }
        else if (typeDef.BaseType.Kind != HandleKind.TypeReference
            || BaseTypeName(reader, typeDef.BaseType) is not ("System.Attribute" or "System.Exception"))
        {
            return ""; // most TypeReference bases — not reliably spellable
        }

        string baseName = BaseTypeName(reader, typeDef.BaseType);
        if (baseName is "System.Object")
            return "";
        return $" : global::{Clean(baseName)}";
    }

    static string GenericBaseClause(MetadataReader reader, EntityHandle handle)
    {
        TypeRef type;
        try { type = TypeRefDecoder.Instance.GetTypeFromSpecification(reader, GenericScope.Empty, (TypeSpecificationHandle)handle, 0); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return ""; }

        if (type is not
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType:
                {
                    Kind: TypeRefKind.Definition
                } definition
            }
            || definition.Assembly != CanonicalAssemblyName(reader)
            || ContainsGenericParameter(type)
            || ContainsUnsupportedType(type))
        {
            return "";
        }

        return $" : {Clean(FullyQualifiedTypeName(type))}";
    }

    static bool ContainsGenericParameter(TypeRef type)
        => type.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
           || (type.ElementType is { } element && ContainsGenericParameter(element))
           || type.TypeArguments.Any(ContainsGenericParameter);

    static bool ContainsUnsupportedType(TypeRef type)
        => type.Kind == TypeRefKind.Unsupported
           || (type.ElementType is { } element && ContainsUnsupportedType(element))
           || type.TypeArguments.Any(ContainsUnsupportedType);

    static string InterfaceClause(MetadataReader reader, TypeDefinition typeDef, TypeKind kind, SignatureSpellability accessibility)
    {
        if (kind != TypeKind.Class)
            return "";

        var interfaces = new List<string>();
        foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            if (SameAssemblyNonGenericInterfaceName(reader, implementation.Interface) is { } name
                && IsSafeClassInterfaceName(name)
                && InterfaceMembersSatisfied(reader, typeDef, implementation.Interface, accessibility))
            {
                interfaces.Add(Clean(name));
                continue;
            }

            if (ProtobufSelfMessageInterfaceName(reader, typeDef, implementation.Interface) is { } protobufName)
                interfaces.Add(Clean(protobufName));
        }

        return interfaces.Count == 0 ? "" : string.Join(", ", interfaces.Distinct(StringComparer.Ordinal));
    }

    static string CombineInheritance(string baseClause, string interfaceClause)
    {
        if (interfaceClause.Length == 0)
            return baseClause;
        if (baseClause.Length == 0)
            return $" : {interfaceClause}";
        return $"{baseClause}, {interfaceClause}";
    }

    static bool IsSafeClassInterfaceName(string name)
        => name == "IResource"
           || name.EndsWith(".IResource", StringComparison.Ordinal)
           || name == "IResourceAnnotation"
           || name.EndsWith(".IResourceAnnotation", StringComparison.Ordinal)
           || name == "Aspire.Hosting.Dcp.Model.IKubernetesStaticMetadata";

    static string? SameAssemblyNonGenericInterfaceName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind != HandleKind.TypeDefinition)
            return null;
        var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
        if ((definition.Attributes & TypeAttributes.Interface) == 0
            || definition.GetDeclaringType().IsNil == false
            || definition.GetGenericParameters().Count != 0)
        {
            return null;
        }

        return FullName(reader, definition);
    }

    static string? ProtobufSelfMessageInterfaceName(MetadataReader reader, TypeDefinition typeDef, EntityHandle handle)
    {
        if (handle.Kind != HandleKind.TypeSpecification || !HasProtobufMessageMembers(reader, typeDef))
            return null;

        TypeRef type;
        try { type = TypeRefDecoder.Instance.GetTypeFromSpecification(reader, GenericScope.Empty, (TypeSpecificationHandle)handle, 0); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }

        if (type is not
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType:
                {
                    Kind: TypeRefKind.Definition,
                    Assembly: "Google.Protobuf",
                    Namespace: "Google.Protobuf",
                    Name: "IMessage`1"
                },
                TypeArguments: [{ } argument]
            }
            || !IsThisType(reader, typeDef, argument))
        {
            return null;
        }

        return FullyQualifiedTypeName(type);
    }

    static bool HasProtobufMessageMembers(MetadataReader reader, TypeDefinition typeDef)
        => HierarchyHasProperty(reader, typeDef, "Descriptor")
           && HierarchyHasMethod(reader, typeDef, "WriteTo")
           && HierarchyHasMethod(reader, typeDef, "CalculateSize")
           && HierarchyHasMethod(reader, typeDef, "MergeFrom");

    static bool IsThisType(MetadataReader reader, TypeDefinition typeDef, TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
            return false;
        var expected = TypeDefinitionName(reader, typeDef);
        return type.Assembly == CanonicalAssemblyName(reader)
            && type.Namespace == expected.Namespace
            && type.Name == expected.Name;
    }

    static (string Namespace, string Name) TypeDefinitionName(MetadataReader reader, TypeDefinition typeDef)
    {
        if (!typeDef.IsNested)
            return (reader.GetString(typeDef.Namespace), reader.GetString(typeDef.Name));
        var declaring = TypeDefinitionName(reader, reader.GetTypeDefinition(typeDef.GetDeclaringType()));
        return (declaring.Namespace, $"{declaring.Name}+{reader.GetString(typeDef.Name)}");
    }

    static string CanonicalAssemblyName(MetadataReader reader)
        => reader.IsAssembly
            ? TypeRefDecoder.CanonicalSelf(reader)
            : "";

    static string FullyQualifiedTypeName(TypeRef type) => type.Kind switch
    {
        TypeRefKind.Definition => FullyQualifiedDefinitionName(type),
        TypeRefKind.GenericInstance => FullyQualifiedGenericName(type),
        _ => type.ToDisplayString(),
    };

    static string FullyQualifiedDefinitionName(TypeRef type)
    {
        string name = StripArity(type.Name).Replace('+', '.');
        return type.Namespace.Length == 0 ? name : $"{type.Namespace}.{name}";
    }

    static string FullyQualifiedGenericName(TypeRef type)
    {
        var definition = type.ElementType!;
        string name = StripArity(definition.Name).Replace('+', '.');
        string qualified = definition.Namespace.Length == 0 ? name : $"{definition.Namespace}.{name}";
        return $"{qualified}<{string.Join(", ", type.TypeArguments.Select(FullyQualifiedTypeName))}>";
    }

    static bool InterfaceMembersSatisfied(MetadataReader reader, TypeDefinition typeDef, EntityHandle interfaceHandle, SignatureSpellability accessibility)
    {
        if (interfaceHandle.Kind != HandleKind.TypeDefinition)
            return false;
        var interfaceDef = reader.GetTypeDefinition((TypeDefinitionHandle)interfaceHandle);
        foreach (var inheritedHandle in interfaceDef.GetInterfaceImplementations())
        {
            var inherited = reader.GetInterfaceImplementation(inheritedHandle);
            if (!InterfaceMembersSatisfied(reader, typeDef, inherited.Interface, accessibility))
                return false;
        }

        foreach (var propertyHandle in interfaceDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            if (!accessibility.CanSpellProperty(reader, property, GenericContext.ForType(reader, interfaceDef)))
                continue;
            if (!HierarchyHasProperty(reader, typeDef, reader.GetString(property.Name)))
                return false;
        }

        var accessorNames = InterfaceAccessorNames(reader, interfaceDef);
        foreach (var methodHandle in interfaceDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            string name = reader.GetString(method.Name);
            if (accessorNames.Contains(name))
                continue;
            if (!accessibility.CanSpellMethod(reader, method, GenericContext.ForMethod(reader, interfaceDef, method)))
                continue;
            if (name.Contains('.') || !HierarchyHasMethod(reader, typeDef, name))
                return false;
        }

        return true;
    }

    static HashSet<string> InterfaceAccessorNames(MetadataReader reader, TypeDefinition interfaceDef)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propertyHandle in interfaceDef.GetProperties())
        {
            var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
            if (!accessors.Getter.IsNil)
                names.Add(reader.GetString(reader.GetMethodDefinition(accessors.Getter).Name));
            if (!accessors.Setter.IsNil)
                names.Add(reader.GetString(reader.GetMethodDefinition(accessors.Setter).Name));
        }
        return names;
    }

    static bool HierarchyHasProperty(MetadataReader reader, TypeDefinition typeDef, string propertyName)
    {
        for (var current = typeDef; ; )
        {
            foreach (var propertyHandle in current.GetProperties())
                if (reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name) == propertyName)
                    return true;
            if (current.BaseType.IsNil || current.BaseType.Kind != HandleKind.TypeDefinition)
                return false;
            current = reader.GetTypeDefinition((TypeDefinitionHandle)current.BaseType);
        }
    }

    static bool HierarchyHasMethod(MetadataReader reader, TypeDefinition typeDef, string methodName)
    {
        for (var current = typeDef; ; )
        {
            foreach (var methodHandle in current.GetMethods())
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return true;
            if (current.BaseType.IsNil || current.BaseType.Kind != HandleKind.TypeDefinition)
                return false;
            current = reader.GetTypeDefinition((TypeDefinitionHandle)current.BaseType);
        }
    }

    static void EmitType(MetadataReader reader, TypeDefinitionHandle typeHandle,
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        IReadOnlyList<(string Field, string Value)> fieldInits, TypeDefinitionHandle fieldInitType,
        SignatureSpellability accessibility,
        ISet<MethodDefinitionHandle> productWholeMembers,
        StringBuilder sb,
        int indent)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var kind = ShapeOf(reader, typeDef);
        string pad = new(' ', indent * 4);
        string name = StripArity(reader.GetString(typeDef.Name));
        var typeContext = GenericContext.ForType(reader, typeDef);

        if (kind == TypeKind.Enum)
        {
            EmitEnum(reader, typeDef, name, sb, pad);
            return;
        }

        string genParams = GenericParamList(reader, typeDef.GetGenericParameters(), InheritedGenericArity(reader, typeDef));
        string whereClauses = WhereClauses(reader, typeDef.GetGenericParameters(), InheritedGenericArity(reader, typeDef));

        if (kind == TypeKind.Delegate)
        {
            EmitDelegate(reader, typeDef, name, genParams, whereClauses, typeContext, sb, pad);
            return;
        }

        if (kind == TypeKind.Interface)
        {
            EmitInterface(reader, typeHandle, name, genParams, whereClauses, accessibility, sb, pad, indent);
            return;
        }

        // Byref-like stubs can legally contain Span<T> fields only when emitted
        // as ref structs; otherwise the whole compile-back unit becomes invalid.
        string keyword = kind == TypeKind.Struct
            ? (IsByRefLike(reader, typeDef) ? "ref struct" : "struct")
            : IsStaticClass(typeDef) ? "static class" : "class";
        string baseClause = BaseClause(reader, typeDef, kind);
        string interfaceClause = InterfaceClause(reader, typeDef, kind, accessibility);
        string inheritanceClause = CombineInheritance(baseClause, interfaceClause);
        bool implementsProtobufMessage = interfaceClause.Contains("Google.Protobuf.IMessage<", StringComparison.Ordinal);
        bool implementsKubernetesStaticMetadata = interfaceClause.Contains("Aspire.Hosting.Dcp.Model.IKubernetesStaticMetadata", StringComparison.Ordinal);
        // An [InlineArray(N)] struct must carry the attribute for its span
        // conversions (e.g. `(Span<T>)place`) to bind; the bare reconstructed
        // struct otherwise has no such conversion and the body fails to recompile.
        if (kind == TypeKind.Struct && InlineArrayAttributeText(reader, typeDef) is { } inlineArrayAttr)
            sb.AppendLine($"{pad}{inlineArrayAttr}");
        var primaryConstructorTarget = targets
            .FirstOrDefault(target =>
                target.Value.PrimaryConstructor is not null
                && reader.GetMethodDefinition(target.Key).GetDeclaringType() == typeHandle);
        var primaryConstructor = primaryConstructorTarget.Value.PrimaryConstructor;
        string primaryParameters = primaryConstructor is null ? "" : $"({primaryConstructor.Parameters})";
        string unsafeModifier = TypeHasAwaitTarget(reader, typeHandle, targets) ? "" : "unsafe ";
        sb.AppendLine($"{pad}public {unsafeModifier}{keyword} {Identifier(name)}{genParams}{primaryParameters}{inheritanceClause}{whereClauses}");
        sb.AppendLine($"{pad}{{");
        if (implementsProtobufMessage)
            sb.AppendLine($"{pad}    Google.Protobuf.Reflection.MessageDescriptor Google.Protobuf.IMessage.Descriptor => throw null;");
        if (implementsKubernetesStaticMetadata)
            sb.AppendLine($"{pad}    string Aspire.Hosting.Dcp.Model.IKubernetesStaticMetadata.ObjectKind => throw null;");

        // Field initializers lifted from a target ctor apply to this type's
        // fields only when this is the type that lifted them.
        var thisFieldInits = typeHandle == fieldInitType ? fieldInits : [];
        if (primaryConstructor is not null)
            thisFieldInits = [.. thisFieldInits, .. primaryConstructor.FieldInitializers];

        foreach (var fh in typeDef.GetFields())
            EmitField(reader, fh, typeContext, thisFieldInits, accessibility, sb, pad + "    ");

        // Reconstruct pure-stub properties as property syntax so a body's `obj.X`
        // binds — the dominant cluster bail (CS1061) once namespace + ctor stubs
        // land. A property whose accessor is a target is left to the method loop
        // unchanged, so the target's own emission and opcode comparison are
        // untouched; the replaced accessor handles are skipped below. Structs are
        // restricted to compiler auto-properties: a non-auto mutable stub on a
        // readonly receiver can introduce a defensive copy and change opcodes, but
        // an auto-property preserves the field-backed accessor shape.
        var stubPropertyAccessors = new HashSet<MethodDefinitionHandle>();
        var orderedTargetProperties = new Dictionary<MethodDefinitionHandle, PropertyDefinitionHandle>();
        if (kind is TypeKind.Class or TypeKind.Struct)
            EmitStubProperties(reader, typeDef, targets, accessibility, thisFieldInits, stubPropertyAccessors,
                orderedTargetProperties, sb, pad + "    ",
                requireAutoProperty: kind == TypeKind.Struct);
        var orderedTargetEvents = new Dictionary<MethodDefinitionHandle, EventDefinitionHandle>();
        IndexTargetEvents(reader, typeDef, targets, orderedTargetEvents);

        foreach (var mh in typeDef.GetMethods())
        {
            if (stubPropertyAccessors.Contains(mh))
                continue; // emitted as a property accessor above
            if (orderedTargetEvents.TryGetValue(mh, out var targetEvent))
            {
                var accessors = reader.GetEventDefinition(targetEvent).GetAccessors();
                if (mh == (!accessors.Adder.IsNil ? accessors.Adder : accessors.Remover))
                {
                    if (TryGetProductWholeEvent(accessors, targets, out var wholeEvent, out var targetAccessors))
                    {
                        EmitPrerenderedMember(wholeEvent, sb, pad + "    ");
                        foreach (var targetAccessor in targetAccessors)
                            productWholeMembers.Add(targetAccessor);
                    }
                }
                continue; // emitted once, at its first accessor's metadata position
            }
            if (orderedTargetProperties.TryGetValue(mh, out var targetProperty))
            {
                var accessors = reader.GetPropertyDefinition(targetProperty).GetAccessors();
                if (mh == (!accessors.Getter.IsNil ? accessors.Getter : accessors.Setter))
                {
                    if (TryGetProductWholeProperty(accessors, targets, out var wholeProperty, out var targetAccessors))
                    {
                        EmitPrerenderedMember(wholeProperty, sb, pad + "    ");
                        foreach (var targetAccessor in targetAccessors)
                            productWholeMembers.Add(targetAccessor);
                    }
                    else
                    {
                        EmitTargetProperty(reader, typeDef, targetProperty, targets, accessibility, sb, pad + "    ");
                    }
                }
                continue; // emitted once, at its first accessor's metadata position
            }
            if (mh == primaryConstructorTarget.Key)
                continue; // emitted as the type's primary constructor header
            var hasTarget = targets.TryGetValue(mh, out var target);
            if (hasTarget && target.WholeMember is { } wholeMember)
            {
                string methodName = reader.GetString(reader.GetMethodDefinition(mh).Name);
                if (methodName != ".ctor"
                    || TryForcePublicConstructorAccessibility(wholeMember, out wholeMember))
                {
                    EmitPrerenderedMember(wholeMember, sb, pad + "    ");
                    productWholeMembers.Add(mh);
                    continue;
                }
            }

            EmitMethod(reader, typeHandle, mh,
                hasTarget ? target.Body : null,
                hasTarget ? target.Chain : null,
                hasTarget && target.RequiresAsync,
                accessibility,
                sb, pad + "    ");
        }

        // A reconstructed class whose base type has no parameterless constructor
        // makes every derived stub's compiler-synthesized `base()` call fail
        // (CS1729/CS7036) — the dominant non-pathological cluster bail once
        // namespace inclusion lands. Inject a throwing parameterless stub when the
        // type has none of its own so the implicit chain binds (transitively, as
        // every reconstructed class lacking one gets the same stub). Emitted AFTER
        // the real methods so it takes the last `.ctor` ordinal: the recompiled
        // lookup matches constructors by name + overload ordinal (not signature),
        // so a synthetic ctor emitted earlier would shift a real ctor target's
        // ordinal and compare it against this throwing stub. Last-ordinal keeps the
        // real ctors at 0..k-1 and the synthetic at k (never requested), so it
        // cannot change a target's fidelity.
        if (keyword == "class"
            && primaryConstructor is null
            && !IsStaticClass(typeDef)
            && !HasParameterlessInstanceCtor(reader, typeDef))
            sb.AppendLine($"{pad}    public {Identifier(name)}() {{ throw null; }}");

        foreach (var nested in typeDef.GetNestedTypes())
        {
            var nestedDef = reader.GetTypeDefinition(nested);
            if (reader.GetString(nestedDef.Name).Contains('<')
                || IsCompilerEmbeddedAttributeType(reader, nestedDef))
                continue; // compiler-generated (display class, iterator) — not valid C#
            EmitType(
                reader,
                nested,
                targets,
                fieldInits,
                fieldInitType,
                accessibility,
                productWholeMembers,
                sb,
                indent + 1);
        }

        static bool TypeHasAwaitTarget(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets)
        {
            foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
                if (targets.TryGetValue(methodHandle, out var target)
                    && target.RequiresAsync)
                {
                    return true;
                }
            return false;
        }

        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// A sibling delegate type, reconstructed from its <c>Invoke</c> signature so
    /// references to it (and <c>new D(...)</c> / invocation) bind in a target body.
    /// </summary>
    static void EmitDelegate(MetadataReader reader, TypeDefinition typeDef, string name,
        string genParams, string whereClauses, GenericContext typeContext, StringBuilder sb, string pad)
    {
        string ret = "void", parameters = "";
        foreach (var mh in typeDef.GetMethods())
        {
            var m = reader.GetMethodDefinition(mh);
            if (reader.GetString(m.Name) != "Invoke")
                continue;
            try
            {
                var sig = m.DecodeSignature(SignatureDecoder.Instance, typeContext);
                ret = Clean(sig.ReturnType);
                parameters = Parameters(reader, m, sig);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
            break;
        }
        sb.AppendLine($"{pad}public unsafe delegate {ret} {Identifier(name)}{genParams}({parameters}){whereClauses};");
    }

    /// <summary>
    /// A sibling interface, reconstructed with its method and property signatures
    /// (and nested types) so member access through it binds. Members are emitted
    /// without bodies or accessibility, as the interface form requires.
    /// </summary>
    static void EmitInterface(MetadataReader reader, TypeDefinitionHandle typeHandle,
        string name, string genParams, string whereClauses, SignatureSpellability accessibility, StringBuilder sb, string pad, int indent)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        string interfaceBaseClause = InterfaceBaseClause(reader, typeDef);
        sb.AppendLine($"{pad}public unsafe interface {Identifier(name)}{genParams}{interfaceBaseClause}{whereClauses}");
        sb.AppendLine($"{pad}{{");
        string inner = pad + "    ";

        var accessors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ph in typeDef.GetProperties())
        {
            var prop = reader.GetPropertyDefinition(ph);
            var pa = prop.GetAccessors();
            if (!pa.Getter.IsNil) accessors.Add(reader.GetString(reader.GetMethodDefinition(pa.Getter).Name));
            if (!pa.Setter.IsNil) accessors.Add(reader.GetString(reader.GetMethodDefinition(pa.Setter).Name));
            string pname = reader.GetString(prop.Name);
            if (pname.Contains('<') || pname.Contains('.'))
                continue; // indexer / explicit impl — skip
            try
            {
                if (!accessibility.CanSpellProperty(reader, prop, GenericContext.ForType(reader, typeDef)))
                    continue;
                var sig = prop.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                string body = (!pa.Getter.IsNil ? " get;" : "") + (!pa.Setter.IsNil ? " set;" : "");
                sb.AppendLine($"{inner}{Clean(sig.ReturnType)} {Identifier(pname)} {{{body} }}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }

        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            string mn = reader.GetString(method.Name);
            if (mn.Contains('<') || mn.Contains('.') || accessors.Contains(mn))
                continue; // accessor, static-abstract op, or explicit impl
            var context = GenericContext.ForMethod(reader, typeDef, method);
            if (!accessibility.CanSpellMethod(reader, method, context))
                continue;
            MethodSignature<string> sig;
            try { sig = method.DecodeSignature(SignatureDecoder.Instance, context); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { continue; }
            string mGen = GenericParamList(reader, method.GetGenericParameters());
            string mWhere = WhereClauses(reader, method.GetGenericParameters());
            sb.AppendLine($"{inner}{Clean(sig.ReturnType)} {Identifier(mn)}{mGen}({Parameters(reader, method, sig)}){mWhere};");
        }

        foreach (var nested in typeDef.GetNestedTypes())
        {
            var nestedDef = reader.GetTypeDefinition(nested);
            if (reader.GetString(nestedDef.Name).Contains('<')
                || IsCompilerEmbeddedAttributeType(reader, nestedDef))
                continue;
            EmitType(
                reader,
                nested,
                NoTargets,
                [],
                default,
                accessibility,
                new HashSet<MethodDefinitionHandle>(),
                sb,
                indent + 1);
        }

        sb.AppendLine($"{pad}}}");
    }

    static string InterfaceBaseClause(MetadataReader reader, TypeDefinition typeDef)
    {
        var interfaces = new List<string>();
        if (FullName(reader, typeDef) == "Aspire.Hosting.ApplicationModel.IResourceCollection")
            interfaces.Add("System.Collections.Generic.IList<Aspire.Hosting.ApplicationModel.IResource>");
        foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
        {
            var implementation = reader.GetInterfaceImplementation(implementationHandle);
            if (SameAssemblyNonGenericInterfaceName(reader, implementation.Interface) is { } name)
                interfaces.Add(Clean(name));
        }

        return interfaces.Count == 0
            ? ""
            : $" : {string.Join(", ", interfaces.Distinct(StringComparer.Ordinal))}";
    }

    static readonly IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> NoTargets =
        new Dictionary<MethodDefinitionHandle, TargetBody>();

    /// <summary>
    /// Reconstructs a class/struct's properties as property syntax so a body's
    /// <c>obj.X</c> binds — the dominant cluster bail (<c>CS1061</c>) once
    /// namespace inclusion and ctor stubs land, because the method loop otherwise
    /// emits a property's <c>get_X</c>/<c>set_X</c> as plain methods that a
    /// property access cannot resolve to. Scoped to pure-stub properties: a
    /// property whose getter or setter is a target is normally skipped (left to
    /// the method loop), unless the metadata has a compiler auto-property backing
    /// field: in that case, emitting the auto-property lets the generated accessor
    /// and constructor assignment round-trip through normal C# syntax. The replaced
    /// accessor method names are added to <paramref name="skipAccessors"/> so the
    /// caller does not also emit them as methods. Stub accessors throw;
    /// over-permissive accessibility on a stub is safe because the body never runs
    /// and only needs to bind.
    /// </summary>
    static void EmitStubProperties(MetadataReader reader, TypeDefinition typeDef,
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        SignatureSpellability accessibility, IReadOnlyList<(string Field, string Value)> fieldInits,
        HashSet<MethodDefinitionHandle> skipAccessors,
        Dictionary<MethodDefinitionHandle, PropertyDefinitionHandle> orderedTargetProperties,
        StringBuilder sb, string pad,
        bool requireAutoProperty = false)
    {
        var typeContext = GenericContext.ForType(reader, typeDef);
        foreach (var ph in typeDef.GetProperties())
        {
            var prop = reader.GetPropertyDefinition(ph);
            var pa = prop.GetAccessors();
            string pname = reader.GetString(prop.Name);
            if (pname.Contains('<') || pname.Contains('.'))
                continue; // compiler-generated / explicit interface impl
            if (!requireAutoProperty
                && TryGetProductWholeProperty(pa, targets, out _, out _))
            {
                if (!pa.Getter.IsNil) orderedTargetProperties[pa.Getter] = ph;
                if (!pa.Setter.IsNil) orderedTargetProperties[pa.Setter] = ph;
                continue;
            }
            try
            {
                if (!accessibility.CanSpellProperty(reader, prop, typeContext))
                    continue;
                var sig = prop.DecodeSignature(SignatureDecoder.Instance, typeContext);
                if (sig.ParameterTypes.Length > 0)
                    continue; // indexer — needs this[...] syntax
                string ret = Clean(sig.ReturnType);
                if (ret.Contains('&'))
                    continue; // ref-return property
                bool hasGet = !pa.Getter.IsNil && CanEmitAccessor(reader, typeDef, pa.Getter, accessibility);
                bool hasSet = !pa.Setter.IsNil && CanEmitAccessor(reader, typeDef, pa.Setter, accessibility);
                if (!hasGet && !hasSet)
                    continue;
                var accessorMethod = reader.GetMethodDefinition(pa.Getter.IsNil ? pa.Setter : pa.Getter);
                bool isStatic = accessorMethod.Attributes.HasFlag(MethodAttributes.Static);
                // Preserve the accessor's call kind at a `?.X` site (receiver known
                // non-null): a non-virtual *or final* virtual getter compiles to
                // `call`, a non-final virtual getter to `callvirt`. Emit `virtual`
                // only for a non-final virtual accessor so the stub keeps the same
                // call kind; a non-virtual stub of a virtual property would
                // otherwise change the target's opcodes. `virtual` (not `override`)
                // is enough because the comparison is by opcode, not token, and the
                // hiding warning is suppressed.
                bool emitVirtual = accessorMethod.Attributes.HasFlag(MethodAttributes.Virtual)
                    && !accessorMethod.Attributes.HasFlag(MethodAttributes.Final);
                string modifier = isStatic ? "static " : (emitVirtual ? "virtual " : "");
                string unsafeMod = RequiresUnsafeSignature(ret) ? "unsafe " : "";
                bool isAutoProperty = hasGet
                    && AccessorsAreCompilerGenerated(reader, pa)
                    && HasAutoPropertyBackingField(reader, typeDef, pname, ret, isStatic);
                if (isAutoProperty
                    && TryGetProductWholeProperty(pa, targets, out _, out _))
                {
                    if (!pa.Getter.IsNil) orderedTargetProperties[pa.Getter] = ph;
                    if (!pa.Setter.IsNil) orderedTargetProperties[pa.Setter] = ph;
                    continue;
                }
                bool accessorIsTarget = (!pa.Getter.IsNil && targets.ContainsKey(pa.Getter))
                    || (!pa.Setter.IsNil && targets.ContainsKey(pa.Setter));
                if (accessorIsTarget && !isAutoProperty)
                {
                    bool requiresMethodFallback = requireAutoProperty
                        || (!pa.Getter.IsNil && targets.TryGetValue(pa.Getter, out var getter) && getter.RequiresAsync)
                        || (!pa.Setter.IsNil && targets.TryGetValue(pa.Setter, out var setter) && setter.RequiresAsync);
                    if (requiresMethodFallback)
                        continue;
                    if (!pa.Getter.IsNil) orderedTargetProperties[pa.Getter] = ph;
                    if (!pa.Setter.IsNil) orderedTargetProperties[pa.Setter] = ph;
                    continue;
                }
                if (requireAutoProperty && !isAutoProperty)
                    continue;
                if (isAutoProperty)
                {
                    string initializer = fieldInits.FirstOrDefault(init => init.Field == pname).Value is { } value
                        ? $" = {value};"
                        : "";
                    string autoBody = " get;" + (hasSet ? " set;" : "");
                    sb.AppendLine($"{pad}public {modifier}{unsafeMod}{ret} {Identifier(pname)} {{{autoBody} }}{initializer}");
                    if (!pa.Getter.IsNil) skipAccessors.Add(pa.Getter);
                    if (!pa.Setter.IsNil) skipAccessors.Add(pa.Setter);
                    continue;
                }
                string body = (hasGet ? " get => throw null;" : "") + (hasSet ? " set => throw null;" : "");
                sb.AppendLine($"{pad}public {modifier}{unsafeMod}{ret} {Identifier(pname)} {{{body} }}");
                if (!pa.Getter.IsNil) skipAccessors.Add(pa.Getter);
                if (!pa.Setter.IsNil) skipAccessors.Add(pa.Setter);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
    }

    static bool TryGetProductWholeProperty(
        PropertyAccessors accessors,
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        out string wholeProperty,
        out IReadOnlyList<MethodDefinitionHandle> targetAccessors)
    {
        var handles = new List<MethodDefinitionHandle>(2);
        var renders = new List<string>(2);
        Add(accessors.Getter);
        Add(accessors.Setter);

        wholeProperty = "";
        targetAccessors = handles;
        if (handles.Count == 0
            || renders.Count != handles.Count
            || !renders.All(render => string.Equals(render, renders[0], StringComparison.Ordinal)))
        {
            return false;
        }

        wholeProperty = renders[0];
        return true;

        void Add(MethodDefinitionHandle handle)
        {
            if (handle.IsNil || !targets.TryGetValue(handle, out var target))
                return;
            handles.Add(handle);
            if (!target.RequiresAsync && target.WholeMember is { } render)
                renders.Add(render);
        }
    }

    static void IndexTargetEvents(
        MetadataReader reader,
        TypeDefinition typeDef,
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        Dictionary<MethodDefinitionHandle, EventDefinitionHandle> orderedTargetEvents)
    {
        foreach (var eventHandle in typeDef.GetEvents())
        {
            var eventDefinition = reader.GetEventDefinition(eventHandle);
            var accessors = eventDefinition.GetAccessors();
            if (!TryGetProductWholeEvent(accessors, targets, out _, out _))
                continue;
            if (!accessors.Adder.IsNil)
                orderedTargetEvents[accessors.Adder] = eventHandle;
            if (!accessors.Remover.IsNil)
                orderedTargetEvents[accessors.Remover] = eventHandle;
        }
    }

    static bool TryGetProductWholeEvent(
        EventAccessors accessors,
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        out string wholeEvent,
        out IReadOnlyList<MethodDefinitionHandle> targetAccessors)
    {
        var handles = new List<MethodDefinitionHandle>(2);
        var renders = new List<string>(2);
        Add(accessors.Adder);
        Add(accessors.Remover);

        wholeEvent = "";
        targetAccessors = handles;
        if (handles.Count == 0
            || renders.Count != handles.Count
            || !renders.All(render => string.Equals(render, renders[0], StringComparison.Ordinal)))
        {
            return false;
        }

        wholeEvent = renders[0];
        return true;

        void Add(MethodDefinitionHandle handle)
        {
            if (handle.IsNil || !targets.TryGetValue(handle, out var target))
                return;
            handles.Add(handle);
            if (!target.RequiresAsync && target.WholeMember is { } render)
                renders.Add(render);
        }
    }

    static void EmitTargetProperty(MetadataReader reader, TypeDefinition typeDef, PropertyDefinitionHandle ph,
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        SignatureSpellability accessibility, StringBuilder sb, string pad)
    {
        var prop = reader.GetPropertyDefinition(ph);
        var pa = prop.GetAccessors();
        var context = GenericContext.ForType(reader, typeDef);
        var sig = prop.DecodeSignature(SignatureDecoder.Instance, context);
        string ret = Clean(sig.ReturnType);
        var accessorMethod = reader.GetMethodDefinition(pa.Getter.IsNil ? pa.Setter : pa.Getter);
        bool isStatic = accessorMethod.Attributes.HasFlag(MethodAttributes.Static);
        bool emitVirtual = accessorMethod.Attributes.HasFlag(MethodAttributes.Virtual)
            && !accessorMethod.Attributes.HasFlag(MethodAttributes.Final);
        string modifier = isStatic ? "static " : (emitVirtual ? "virtual " : "");
        string unsafeMod = RequiresUnsafeSignature(ret) ? "unsafe " : "";
        bool hasGet = !pa.Getter.IsNil && CanEmitAccessor(reader, typeDef, pa.Getter, accessibility);
        bool hasSet = !pa.Setter.IsNil && CanEmitAccessor(reader, typeDef, pa.Setter, accessibility);
        string getterBody = !pa.Getter.IsNil && targets.TryGetValue(pa.Getter, out var getterTarget)
            ? $" get {{\n{getterTarget.Body}\n{pad}}}"
            : (hasGet ? " get => throw null;" : "");
        string setterBody = !pa.Setter.IsNil && targets.TryGetValue(pa.Setter, out var setterTarget)
            ? $" set {{\n{setterTarget.Body}\n{pad}}}"
            : (hasSet ? " set => throw null;" : "");
        sb.AppendLine($"{pad}public {modifier}{unsafeMod}{ret} {Identifier(reader.GetString(prop.Name))} {{{getterBody}{setterBody} }}");
    }

    static bool HasAutoPropertyBackingField(
        MetadataReader reader,
        TypeDefinition typeDef,
        string propertyName,
        string propertyType,
        bool isStaticProperty)
    {
        string backingName = $"<{propertyName}>k__BackingField";
        var context = GenericContext.ForType(reader, typeDef);
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (reader.GetString(field.Name) != backingName)
                continue;
            if (field.Attributes.HasFlag(FieldAttributes.Static) != isStaticProperty)
                continue;
            if (!HasCompilerGeneratedAttribute(reader, field.GetCustomAttributes()))
                return false;
            try
            {
                return Clean(field.DecodeSignature(SignatureDecoder.Instance, context)) == propertyType;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }
        return false;
    }

    static string? AutoPropertyNameForBackingField(MetadataReader reader, TypeDefinition typeDef, string fieldName)
    {
        if (!fieldName.StartsWith('<') || !fieldName.EndsWith(">k__BackingField", StringComparison.Ordinal))
            return null;

        string propertyName = fieldName[1..^">k__BackingField".Length];
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            if (reader.GetString(property.Name) == propertyName)
                return propertyName;
        }
        return null;
    }

    static bool AccessorsAreCompilerGenerated(MetadataReader reader, PropertyAccessors accessors)
    {
        if (!accessors.Getter.IsNil
            && !HasCompilerGeneratedAttribute(reader, reader.GetMethodDefinition(accessors.Getter).GetCustomAttributes()))
            return false;
        if (!accessors.Setter.IsNil
            && !HasCompilerGeneratedAttribute(reader, reader.GetMethodDefinition(accessors.Setter).GetCustomAttributes()))
            return false;
        return true;
    }

    static bool HasCompilerGeneratedAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
            if (AttributeTypeFullName(reader, reader.GetCustomAttribute(attributeHandle)) == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                return true;
        return false;
    }

    static bool CanEmitAccessor(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle handle, SignatureSpellability accessibility)
    {
        var method = reader.GetMethodDefinition(handle);
        return accessibility.CanSpellMethod(reader, method, GenericContext.ForMethod(reader, typeDef, method));
    }


    /// <summary>
    /// The namespaces the whole-module skeleton imports so the product printer's
    /// short type names bind. This mirrors the using set the decompiler output
    /// assumes (the same family <see cref="ValidityCheck"/> uses) plus the
    /// metadata namespaces that dominate the changed-method corpus
    /// (<c>System.Reflection.Metadata</c> handle/struct types, <c>PEReader</c>,
    /// immutable collections). Conservative on purpose: every entry is a
    /// widely-assumed, low-collision namespace, so adding it resolves short names
    /// without risking CS0104 ambiguity.
    /// </summary>
    static readonly string[] SkeletonUsings =
    [
        "System",
        "System.Buffers",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Collections.Generic",
        "System.Collections.Immutable",
        "System.Globalization",
        "System.IO",
        "System.Linq",
        "System.Numerics",
        "System.Reflection",
        "System.Reflection.Metadata",
        "System.Reflection.PortableExecutable",
        "System.Runtime.CompilerServices",
        "System.Runtime.InteropServices",
        "System.Text",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    static void EmitEnum(MetadataReader reader, TypeDefinition typeDef, string name, StringBuilder sb, string pad)
    {
        string underlying = "int";
        var members = new List<string>();
        foreach (var fh in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fh);
            string fname = reader.GetString(field.Name);
            if (fname == "value__")
            {
                underlying = field.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                continue;
            }
            if (fname.Contains('<'))
                continue;
            string? value = ConstantText(reader, field.GetDefaultValue());
            members.Add(value is null ? Identifier(fname) : $"{Identifier(fname)} = {value}");
        }
        sb.AppendLine($"{pad}public enum {Identifier(name)} : {underlying}");
        sb.AppendLine($"{pad}{{");
        foreach (var m in members)
            sb.AppendLine($"{pad}    {m},");
        sb.AppendLine($"{pad}}}");
    }

    static void EmitField(MetadataReader reader, FieldDefinitionHandle fh, GenericContext context,
        IReadOnlyList<(string Field, string Value)> fieldInits, SignatureSpellability accessibility, StringBuilder sb, string pad)
    {
        var field = reader.GetFieldDefinition(fh);
        string name = reader.GetString(field.Name);
        if (name.Contains('<'))
            return; // compiler-generated backing field
        string type;
        try { type = field.DecodeSignature(SignatureDecoder.Instance, context); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
        if (!accessibility.CanSpellField(reader, field, context)
            && !fieldInits.Any(init => string.Equals(init.Field, name, StringComparison.Ordinal)))
            return;
        if (type.Contains(">e__FixedBuffer", StringComparison.Ordinal))
            return; // compiler-generated fixed-buffer backing type: <Name>e__FixedBuffer is not valid C#

        bool isConst = field.Attributes.HasFlag(FieldAttributes.Literal);
        bool isStatic = field.Attributes.HasFlag(FieldAttributes.Static);
        if (isConst)
        {
            string? value = ConstantText(reader, field.GetDefaultValue());
            if (value is null)
                return; // can't synthesize an initializer — drop it
            string constType = Clean(type);
            // A const of enum type stores its integer underlying value in
            // metadata, so `public const BindingFlags F = 20;` is CS0266. Cast
            // the literal to the (often cross-assembly, Unknown-shape) enum type —
            // a valid constant expression. C# const fields are only primitives,
            // strings (dropped above as a null TypeCode), or enums, so any
            // non-primitive const type is an enum that needs the cast.
            string constValue = IsPrimitiveTypeName(constType) ? value : $"({constType}){value}";
            sb.AppendLine($"{pad}public const {constType} {Identifier(name)} = {constValue};");
            return;
        }
        string? initializer = fieldInits.FirstOrDefault(fi => fi.Field == name).Value;
        string suffix = initializer is not null && !isStatic ? $" = {initializer}" : "";
        bool isVolatile = false;
        try { isVolatile = MetadataDeclarationQuery.IsVolatileField(reader, field, context); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* signature already decoded above; treat as non-volatile */ }
        string fieldType = Clean(type);
        string unsafeModifier = RequiresUnsafeSignature(fieldType) ? "unsafe " : "";
        sb.AppendLine($"{pad}public {unsafeModifier}{(isStatic ? "static " : "")}{(isVolatile ? "volatile " : "")}{fieldType} {Identifier(name)}{suffix};");
    }

    // ---- pr5a (#2996): product-owned target member render ----

    /// <summary>
    /// Per-assembly index from a method's metadata token to its extracted API
    /// model, so the product's whole-member render can be resolved from a raw
    /// <see cref="MethodDefinitionHandle"/> without extracting the API surface
    /// once per method. Keyed on the open <see cref="PEReader"/>, so it lives
    /// exactly as long as the reader does.
    /// </summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PEReader, Dictionary<int, (ApiType Type, ApiMember Member)>> TargetApiIndexCache = new();

    /// <summary>
    /// Per-assembly memo of the product's whole-member batch render
    /// (<see cref="MemberBodyProducer.ProduceMembers"/>), computed once per
    /// <see cref="ApiType"/> so a type's assembly is opened and its type maps
    /// built once for all its members rather than once per member. Keyed on the
    /// open <see cref="PEReader"/> so it shares the reader's lifetime.
    /// </summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PEReader, System.Collections.Concurrent.ConcurrentDictionary<ApiType, IReadOnlyDictionary<ApiMember, MemberRenderResult>>> TargetMemberRenderCache = new();

    /// <summary>
    /// The corpus <see cref="MetadataContext"/> the harness opened a source with,
    /// so the product's whole-member render resolves cross-assembly type facts
    /// through the same resolver the harness's own body decode used (dependency
    /// opens are shared and cross-assembly shapes match). Registered by each
    /// evaluate entry point right after it opens the source.
    /// </summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MetadataSource, MetadataContext> SourceContextCache = new();

    static void RegisterSourceContext(MetadataSource source, MetadataContext context)
        => SourceContextCache.AddOrUpdate(source, context);

    static Dictionary<int, (ApiType Type, ApiMember Member)> TargetApiIndex(PEReader pe)
        => TargetApiIndexCache.GetValue(pe, static p =>
        {
            try
            {
                return CreateTargetApiIndex(p);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Honest degradation: targets fall back to the harness signature path.
                return [];
            }
        });

    static Dictionary<int, (ApiType Type, ApiMember Member)> CreateTargetApiIndex(
        PEReader pe)
        => CreateTargetApiEvidence(pe).Index;

    static TargetApiEvidence CreateTargetApiEvidence(PEReader pe)
    {
        var index = new Dictionary<int, (ApiType Type, ApiMember Member)>();
        var accessorTokens = new HashSet<int>();
        // includeAll: the harness evaluates non-public methods too, so
        // index the whole surface — otherwise internal/private targets
        // silently miss the migration and retain the legacy signature
        // emitter this change replaces (#3062 review).
        foreach (var type in ApiSurfaceExtractor.Extract(pe, includeAll: true).Types)
        {
            foreach (var member in type.Members)
            {
                if (member.MetadataToken is { } token)
                {
                    if (member.Kind == "extension-method")
                        index.TryAdd(token, (type, member));
                    else
                        index[token] = (type, member);
                }
                if (member.Kind == "property")
                {
                    if (member.GetterToken is { } getterToken)
                    {
                        accessorTokens.Add(getterToken);
                        if (!member.Name.Contains(
                                '.',
                                StringComparison.Ordinal))
                        {
                            index.TryAdd(getterToken, (type, member));
                        }
                    }
                    if (member.SetterToken is { } setterToken)
                    {
                        accessorTokens.Add(setterToken);
                        if (!member.Name.Contains(
                                '.',
                                StringComparison.Ordinal))
                        {
                            index.TryAdd(setterToken, (type, member));
                        }
                    }
                }
                if (member.Kind == "event")
                {
                    if (member.AdderToken is { } adderToken)
                    {
                        accessorTokens.Add(adderToken);
                        index.TryAdd(adderToken, (type, member));
                    }
                    if (member.RemoverToken is { } removerToken)
                    {
                        accessorTokens.Add(removerToken);
                        index.TryAdd(removerToken, (type, member));
                    }
                }
            }
        }

        return new(index, accessorTokens);
    }

    sealed record TargetApiEvidence(
        Dictionary<int, (ApiType Type, ApiMember Member)> Index,
        IReadOnlySet<int> AccessorTokens);

    /// <summary>
    /// The product's whole-member render for a target method — the CSharp-owned
    /// signature (from Metadata's model) composed with the decompiler body —
    /// replacing the harness's self-spelled signature. Ordinary constructors,
    /// recovered destructors, properties, and custom events are safe to migrate
    /// because the scaffold already applies the
    /// decompiler's separately captured lifted field initializers to reconstructed
    /// fields, while property and event renders own both accessor declarations and bodies.
    /// Field-like and explicit-interface events, and finalizers that cannot be
    /// recovered as destructors, remain on their existing fallback paths.
    /// Non-essential custom attributes are omitted because the skeleton does not
    /// reproduce arbitrary attribute inheritance; compilation-required attributes
    /// such as <c>SkipLocalsInit</c> remain.
    /// A detected primary constructor remains type-header-owned and therefore
    /// declines the member render. Broad evaluation renders the whole type once
    /// and memoizes the batch; method-filtered evaluation uses the product's
    /// targeted member path so unselected siblings are not rendered.
    /// </summary>
    internal static (string Text, IReadOnlySet<string> Namespaces)? TryRenderTargetMember(
        PEReader pe,
        MetadataSource source,
        MethodDefinitionHandle mh,
        bool targeted,
        bool isPrimaryConstructor)
    {
        int token = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(mh);
        if (!TargetApiIndex(pe).TryGetValue(token, out var entry))
            return null;
        if (isPrimaryConstructor
            || entry.Member.Kind is not ("method" or "operator" or "constructor" or "finalizer" or "property" or "event"))
            return null;
        if (entry.Member.Kind == "property"
            && entry.Type.Kind == "struct"
            && !MemberBodyProducer.IsCompilerGeneratedAutoPropertyAccessor(
                source,
                entry.Member,
                mh))
        {
            return null;
        }
        if (entry.Member.Kind == "event" && entry.Member.IsOverride)
        {
            // The compile-back skeleton does not reconstruct non-target base events.
            // An override event therefore cannot bind until event stubs exist.
            return null;
        }

        var result = targeted
            ? RenderTargetMember(entry.Type, entry.Member, source)
            : RenderTypeMemberBatch(pe, entry.Type, entry.Member, source);
        if (result is null || !result.IsComplete || result.Text is null)
            return null;
        if (entry.Member.Kind == "constructor"
            && SyntaxFactory.ParseMemberDeclaration(result.Text)
                is not ConstructorDeclarationSyntax)
        {
            return null;
        }
        if (entry.Member.Kind == "finalizer"
            && SyntaxFactory.ParseMemberDeclaration(result.Text)
                is not DestructorDeclarationSyntax)
        {
            return null;
        }
        if (entry.Member.Kind == "property"
            && SyntaxFactory.ParseMemberDeclaration(result.Text)
                is not (PropertyDeclarationSyntax or IndexerDeclarationSyntax))
        {
            return null;
        }
        if (entry.Member.Kind == "event"
            && SyntaxFactory.ParseMemberDeclaration(result.Text)
                is not EventDeclarationSyntax)
        {
            return null;
        }
        return (result.Text, new HashSet<string>(result.Namespaces, StringComparer.Ordinal));
    }

    static MemberRenderResult? RenderTargetMember(ApiType type, ApiMember member, MetadataSource source)
    {
        try
        {
            return SourceContextCache.TryGetValue(source, out var context)
                ? MemberBodyProducer.ProduceMember(
                    type,
                    member,
                    source.Path,
                    pdbPath: null,
                    context.Resolver,
                    context,
                    attributeMode: MemberRenderAttributeMode.CompilationRequired)
                : MemberBodyProducer.ProduceMember(
                    type,
                    member,
                    source.Path,
                    pdbPath: null,
                    attributeMode: MemberRenderAttributeMode.CompilationRequired);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    static MemberRenderResult? RenderTypeMemberBatch(
        PEReader pe, ApiType type, ApiMember member, MetadataSource source)
    {
        var memo = TargetMemberRenderCache.GetOrCreateValue(pe);
        var rendered = memo.GetOrAdd(type, static (candidate, src) => RenderTypeMembers(candidate, src), source);
        return rendered.GetValueOrDefault(member);
    }

    /// <summary>
    /// Renders every member of a type once through the product's batch entry
    /// point, reusing the harness's corpus resolver/context (when the source was
    /// registered) so cross-assembly resolution matches the harness's own decode.
    /// </summary>
    static IReadOnlyDictionary<ApiMember, MemberRenderResult> RenderTypeMembers(ApiType type, MetadataSource source)
    {
        try
        {
            return SourceContextCache.TryGetValue(source, out var context)
                ? MemberBodyProducer.ProduceMembers(
                    type,
                    source.Path,
                    pdbPath: null,
                    context.Resolver,
                    context,
                    attributeMode: MemberRenderAttributeMode.CompilationRequired)
                : MemberBodyProducer.ProduceMembers(
                    type,
                    source.Path,
                    pdbPath: null,
                    attributeMode: MemberRenderAttributeMode.CompilationRequired);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Dictionary<ApiMember, MemberRenderResult>();
        }
    }

    /// <summary>
    /// Splices the product's whole-member text into the compile-back unit,
    /// re-indenting from the product's one-level (4-space) base to the member's
    /// position in the reconstructed type. Constructor accessibility is normalized
    /// to public, preserving the skeleton's same-assembly binding policy while the
    /// product continues to own the rest of the declaration. C# ignores
    /// indentation, so that part is cosmetic; only the token stream matters for
    /// the opcode comparison.
    /// </summary>
    static void EmitPrerenderedMember(
        string wholeMember,
        StringBuilder sb,
        string pad)
    {
        int shift = pad.Length - 4;
        string prefix = shift > 0 ? new string(' ', shift) : "";
        foreach (var line in wholeMember.Split('\n'))
        {
            if (line.Length == 0)
                sb.Append('\n');
            else
                sb.Append(prefix).Append(line).Append('\n');
        }
    }

    internal static bool TryForcePublicConstructorAccessibility(
        string wholeMember,
        out string normalized)
    {
        normalized = wholeMember;
        if (SyntaxFactory.ParseMemberDeclaration(wholeMember)
            is not ConstructorDeclarationSyntax constructor)
        {
            return false;
        }

        var accessibility = constructor.Modifiers
            .Where(token => token.IsKind(SyntaxKind.PublicKeyword)
                || token.IsKind(SyntaxKind.PrivateKeyword)
                || token.IsKind(SyntaxKind.ProtectedKeyword)
                || token.IsKind(SyntaxKind.InternalKeyword))
            .ToArray();
        if (accessibility.Length == 0)
            return false;

        var publicToken = SyntaxFactory.Token(
            accessibility[0].LeadingTrivia,
            SyntaxKind.PublicKeyword,
            accessibility[^1].TrailingTrivia);
        var remaining = constructor.Modifiers
            .Where(token => !accessibility.Contains(token))
            .ToArray();
        normalized = constructor
            .WithModifiers(SyntaxFactory.TokenList([publicToken, .. remaining]))
            .ToFullString();
        return true;
    }

    static void EmitMethod(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle mh, string? realBody, string? realChain, bool realRequiresAsync,
        SignatureSpellability accessibility, StringBuilder sb, string pad)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var method = reader.GetMethodDefinition(mh);
        string name = reader.GetString(method.Name);
        if (name.Contains('<') && name is not ".ctor" and not ".cctor")
            return; // compiler-generated
        // An explicit interface implementation carries the dotted interface-
        // qualified IL name (e.g. `System.IDisposable.Dispose`); a reconstructed
        // stub spelled `public Iface.Member(...)` is invalid C# (CS0106) and
        // poisons the whole-module compile. It is never invoked by name (only
        // through the interface), so the target never needs the stub to bind —
        // drop sibling explicit impls. The target itself (realBody set) is still
        // emitted so a changed explicit impl is not silently lost.
        if (realBody is null && name.Contains('.') && name is not ".ctor" and not ".cctor")
            return;
        var context = GenericContext.ForMethod(reader, typeDef, method);
        if (realBody is null && !accessibility.CanSpellMethod(reader, method, context))
            return;
        MethodSignature<string> sig;
        try { sig = method.DecodeSignature(SignatureDecoder.Instance, context); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return; }

        bool isStatic = method.Attributes.HasFlag(MethodAttributes.Static);
        bool isAbstractStub = method.RelativeVirtualAddress == 0
            && realBody is null
            && !isStatic
            && method.Attributes.HasFlag(MethodAttributes.Abstract);
        if (method.RelativeVirtualAddress == 0 && realBody is null && !isAbstractStub)
            return; // extern sibling — no body, and no C# stub shape we can safely infer
        string parameters = Parameters(reader, method, sig, IsExtensionMethod(reader, typeDef, method, sig));
        string body = realBody is null ? " throw null;" : "\n" + realBody + "\n" + pad;

        if (name is ".ctor")
        {
            // Instance constructor: emit as the type's ctor so signature codegen
            // (field inits, base call) matches; strip the IL name. A lifted
            // base(...)/this(...) chain prints as the signature initializer it is.
            string initializer = realChain is { Length: > 0 } ? $" : {realChain}" : "";
            string ctorUnsafeModifier = RequiresUnsafeSignature(parameters) ? "unsafe " : "";
            sb.AppendLine($"{pad}public {ctorUnsafeModifier}{Identifier(StripArity(reader.GetString(typeDef.Name)))}({parameters}){initializer} {{{body}}}");
            return;
        }
        // A finalizer (void Finalize() override) recovered as a destructor: emit
        // ~T() so the recompiled IL re-emits the try/finally + base.Finalize()
        // scaffold the decompiler stripped, making the round trip opcode-exact.
        if (name is "Finalize" && !isStatic && sig.ParameterTypes.Length == 0 && Clean(sig.ReturnType) == "void")
        {
            sb.AppendLine($"{pad}~{Identifier(StripArity(reader.GetString(typeDef.Name)))}() {{{body}}}");
            return;
        }
        if (name is ".cctor")
        {
            sb.AppendLine($"{pad}static {Identifier(StripArity(reader.GetString(typeDef.Name)))}() {{{body}}}");
            return;
        }

        string genParams = GenericParamList(reader, method.GetGenericParameters());
        string whereClauses = WhereClauses(reader, method.GetGenericParameters());
        string returnType = Clean(sig.ReturnType);
        string asyncModifier = realBody is not null && realRequiresAsync && CanBeAsync(returnType)
            ? "async "
            : "";
        string unsafeModifier = asyncModifier.Length == 0 ? "unsafe " : "";
        string slotModifier = StructObjectOverrideModifier(reader, typeDef, method, name, returnType, sig.ParameterTypes.Length);
        // A same-type call to a source-declarable new-slot virtual method binds as
        // callvirt only when the reconstructed sibling keeps that declaration.
        // Override-shaped methods need an override declaration to preserve their
        // symbolic target, so exclude them rather than inventing a new slot.
        bool emitClassVirtual = ShapeOf(reader, typeDef) == TypeKind.Class
            && IsSourceDeclarableClassVirtual(method);
        string instanceModifier = slotModifier.Length != 0
            ? slotModifier
            : (isAbstractStub || emitClassVirtual ? "virtual " : "");
        if (!IsStaticClass(typeDef)
            && name.StartsWith("op_", StringComparison.Ordinal)
            && OperatorDeclaration(name, returnType, parameters) is { } operatorDeclaration)
        {
            sb.AppendLine($"{pad}public {unsafeModifier}static {operatorDeclaration} {{{body}}}");
            return;
        }
        sb.AppendLine($"{pad}public {unsafeModifier}{(isStatic ? "static " : instanceModifier)}{asyncModifier}{returnType} {Identifier(name)}{genParams}({parameters}){whereClauses} {{{body}}}");
    }

    static bool IsSourceDeclarableClassVirtual(MethodDefinition method)
    {
        var attributes = method.Attributes;
        var access = attributes & MethodAttributes.MemberAccessMask;
        bool sourceDeclarableAccess = access is
            MethodAttributes.Public
            or MethodAttributes.Family
            or MethodAttributes.Assembly
            or MethodAttributes.FamANDAssem
            or MethodAttributes.FamORAssem;
        return sourceDeclarableAccess
            && attributes.HasFlag(MethodAttributes.Virtual)
            && !attributes.HasFlag(MethodAttributes.Abstract)
            && !attributes.HasFlag(MethodAttributes.Final)
            && attributes.HasFlag(MethodAttributes.NewSlot);
    }

    static string StructObjectOverrideModifier(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        string name,
        string returnType,
        int parameterCount)
    {
        if (ShapeOf(reader, typeDef) != TypeKind.Struct
            || method.Attributes.HasFlag(MethodAttributes.Static)
            || !method.Attributes.HasFlag(MethodAttributes.Virtual)
            || method.Attributes.HasFlag(MethodAttributes.NewSlot))
            return "";

        return (name, returnType, parameterCount) switch
        {
            ("ToString", "string", 0) => "override ",
            ("GetHashCode", "int", 0) => "override ",
            ("Equals", "bool", 1) => "override ",
            _ => "",
        };
    }

    static string? OperatorDeclaration(string name, string returnType, string parameters)
    {
        if (name.StartsWith("op_Checked", StringComparison.Ordinal)
            && OperatorNames.MapBinaryOrUnary(name["op_Checked".Length..]) is { } checkedSymbol)
            return $"{returnType} operator checked {checkedSymbol}({parameters})";

        return name switch
        {
            "op_Implicit" => $"implicit operator {returnType}({parameters})",
            "op_Explicit" => $"explicit operator {returnType}({parameters})",
            "op_CheckedExplicit" => $"explicit operator checked {returnType}({parameters})",
            _ => OperatorNames.FormatDisplayName(name) is { } display && display.StartsWith("operator ", StringComparison.Ordinal)
                ? $"{returnType} {display}({parameters})"
                : null,
        };
    }

    static bool CanBeAsync(string returnType)
        => returnType is "void" or "Task"
            || returnType.StartsWith("Task<", StringComparison.Ordinal)
            || returnType.EndsWith(".Task", StringComparison.Ordinal)
            || returnType.Contains(".Task<", StringComparison.Ordinal)
            || returnType is "ValueTask"
            || returnType.StartsWith("ValueTask<", StringComparison.Ordinal)
            || returnType.EndsWith(".ValueTask", StringComparison.Ordinal)
            || returnType.Contains(".ValueTask<", StringComparison.Ordinal);

    static bool RequiresUnsafeSignature(string typeText)
        => typeText.Contains('*', StringComparison.Ordinal);

    /// <summary>A static class — <c>abstract sealed</c> — cannot carry an instance constructor.</summary>
    static bool IsStaticClass(TypeDefinition typeDef)
        => (typeDef.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) == (TypeAttributes.Abstract | TypeAttributes.Sealed);

    /// <summary>
    /// Whether the type declares a parameterless instance constructor of its own,
    /// so the reconstructed stub does not need a synthetic one injected. Decode
    /// failures conservatively report <c>true</c> to avoid a duplicate-member emit.
    /// </summary>
    static bool HasParameterlessInstanceCtor(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            if (method.Attributes.HasFlag(MethodAttributes.Static) || reader.GetString(method.Name) != ".ctor")
                continue;
            try
            {
                if (method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method)).ParameterTypes.Length == 0)
                    return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return true; }
        }
        return false;
    }

    /// <summary>
    /// The C# spellings <see cref="Clean"/> produces for primitive types — the
    /// types a `const` field can hold directly without a cast. Any other const
    /// field type is an enum, whose integer literal must be cast to the enum type.
    /// </summary>
    static bool IsPrimitiveTypeName(string type) => type is
        "bool" or "char" or "sbyte" or "byte" or "short" or "ushort"
        or "int" or "uint" or "long" or "ulong" or "float" or "double"
        or "decimal" or "string" or "object" or "nint" or "nuint";

    static string Parameters(MetadataReader reader, MethodDefinition method, MethodSignature<string> sig, bool firstIsExtensionReceiver = false)
    {
        var names = new Dictionary<int, string>();
        var refKinds = new Dictionary<int, ByRefParameterInfo>();
        foreach (var ph in method.GetParameters())
        {
            var p = reader.GetParameter(ph);
            if (p.SequenceNumber >= 1)
            {
                names[p.SequenceNumber - 1] = reader.GetString(p.Name);
                refKinds[p.SequenceNumber - 1] = new ByRefParameterInfo(
                    p.Attributes,
                    HasIsReadOnlyAttribute(reader, p.GetCustomAttributes()));
            }
        }
        var parts = new List<string>();
        for (int i = 0; i < sig.ParameterTypes.Length; i++)
        {
            string name = names.TryGetValue(i, out var n) && n.Length > 0 ? n : $"arg{i}";
            string modifier = firstIsExtensionReceiver && i == 0 ? "this " : "";
            parts.Add($"{modifier}{ByRefKeyword(sig.ParameterTypes[i], refKinds.GetValueOrDefault(i))} {Identifier(name)}");
        }
        return string.Join(", ", parts);
    }

    static bool IsExtensionMethod(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method, MethodSignature<string> sig)
        => method.Attributes.HasFlag(MethodAttributes.Static)
           && IsStaticClass(typeDef)
           && typeDef.GetDeclaringType().IsNil
           && typeDef.GetGenericParameters().Count == 0
           && sig.ParameterTypes.Length > 0
           && CanSpellExtensionReceiver(sig.ParameterTypes[0])
           && AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes())
           && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());

    static bool CanSpellExtensionReceiver(string parameterType)
        => !parameterType.StartsWith("ref ", StringComparison.Ordinal)
           && !parameterType.Contains('*', StringComparison.Ordinal);

    /// <summary>
    /// Renders a parameter type, correcting the signature decoder's bare <c>ref</c>
    /// for a by-reference parameter to the C# direction keyword the metadata proves:
    /// <c>out</c> for out-only, <c>in</c> for csc's readonly-ref encoding
    /// (<see cref="ParameterAttributes.In"/> plus <c>IsReadOnlyAttribute</c>), and
    /// <c>ref</c> for plain ref or marshal-directional <c>[In]</c>/<c>[In,Out]</c>
    /// ref parameters.
    /// </summary>
    readonly record struct ByRefParameterInfo(ParameterAttributes Attributes, bool IsReadOnly);

    static string ByRefKeyword(string parameterType, ByRefParameterInfo parameter)
    {
        string type = Clean(parameterType);
        if (!type.StartsWith("ref ", StringComparison.Ordinal))
            return type;
        string rest = type["ref ".Length..];
        var attributes = parameter.Attributes;
        const ParameterAttributes inOut = ParameterAttributes.In | ParameterAttributes.Out;
        if ((attributes & inOut) == inOut)
            return type;
        if ((attributes & ParameterAttributes.Out) != 0)
            return $"out {rest}";
        if ((attributes & ParameterAttributes.In) != 0 && parameter.IsReadOnly)
            return $"in {rest}";
        return type;
    }

    static bool HasIsReadOnlyAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
            if (AttributeTypeFullName(reader, reader.GetCustomAttribute(attributeHandle)) == "System.Runtime.CompilerServices.IsReadOnlyAttribute")
                return true;
        return false;
    }

    // ---- Small helpers ----

    enum TypeKind { Class, Struct, Enum, Interface, Delegate }

    static TypeKind ShapeOf(MetadataReader reader, TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return TypeKind.Interface;
        if (typeDef.BaseType.IsNil)
            return TypeKind.Class;
        string baseName = BaseTypeName(reader, typeDef.BaseType);
        return baseName switch
        {
            "System.Enum" => TypeKind.Enum,
            "System.ValueType" => TypeKind.Struct,
            "System.MulticastDelegate" or "System.Delegate" => TypeKind.Delegate,
            _ => TypeKind.Class,
        };
    }

    static string BaseTypeName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.IsNil)
            return "";

        return handle.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)handle)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
            _ => "",
        };
    }

    static string FullName(MetadataReader reader, TypeReference t)
    {
        string ns = reader.GetString(t.Namespace);
        string n = reader.GetString(t.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }

    static string FullName(MetadataReader reader, TypeDefinition t)
    {
        if (t.IsNested)
        {
            var declaring = reader.GetTypeDefinition(t.GetDeclaringType());
            return $"{FullName(reader, declaring)}.{StripArity(reader.GetString(t.Name))}";
        }

        string ns = reader.GetString(t.Namespace);
        string n = reader.GetString(t.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }

    static string GenericParamList(MetadataReader reader, GenericParameterHandleCollection handles, int skip = 0)
    {
        if (handles.Count <= skip)
            return "";
        var names = handles.Skip(skip).Select(h => Identifier(reader.GetString(reader.GetGenericParameter(h).Name)));
        return "<" + string.Join(", ", names) + ">";
    }

    /// <summary>
    /// The number of generic parameters a nested type inherits from its enclosing
    /// type — its declaring type's full arity. A nested type's metadata generic
    /// parameter list is cumulative (enclosing + own), but a C# nested-type
    /// declaration may only restate its own parameters, so the inherited leading
    /// ones must be dropped (<c>ConsList&lt;T&gt;</c>'s nested <c>Enumerator</c> is
    /// <c>struct Enumerator</c>, never <c>struct Enumerator&lt;T&gt;</c>, which would
    /// shadow <c>T</c> and reject the in-scope <c>Enumerator</c> reference as CS0305).
    /// </summary>
    static int InheritedGenericArity(MetadataReader reader, TypeDefinition typeDef)
    {
        var declaring = typeDef.GetDeclaringType();
        return declaring.IsNil ? 0 : reader.GetTypeDefinition(declaring).GetGenericParameters().Count;
    }

    /// <summary>
    /// The C# <c>where</c> clauses for a generic parameter list, so a reconstructed
    /// type or method restates the constraints its real type arguments satisfy —
    /// without them a value-type argument is CS0453 and a constrained type
    /// argument is CS0314. Special constraints (<c>struct</c>/<c>class</c>/
    /// <c>new()</c>) are always emitted; a type constraint is emitted only when it
    /// is reliably spellable (a top-level definition or assembly-scoped reference),
    /// skipping nested, generic (TypeSpec), and parameter constraints so a dropped
    /// clause never miscompiles worse than today's no-constraint baseline.
    /// <paramref name="skip"/> drops a nested type's inherited leading parameters,
    /// matching <see cref="GenericParamList"/>.
    /// </summary>
    static string WhereClauses(MetadataReader reader, GenericParameterHandleCollection handles, int skip = 0)
    {
        if (handles.Count <= skip)
            return "";

        var clauses = new List<string>();
        var genericScope = ConstraintGenericScope(reader, handles);
        int index = 0;
        foreach (var handle in handles)
        {
            if (index++ < skip)
                continue;
            var parameter = reader.GetGenericParameter(handle);
            var attributes = parameter.Attributes;
            bool valueType = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            bool referenceType = (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
            bool defaultCtor = (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;

            var parts = new List<string>();
            var baseClasses = new List<string>();
            var interfaces = new List<string>();
            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                if (ConstraintTypeName(reader, constraint.Type, genericScope) is not { Name.Length: > 0 } spelled)
                    continue;
                // System.ValueType/Object are implied by struct or are the
                // universal base; an explicit clause is invalid or noise.
                if (spelled.Name is "System.Object" or "System.ValueType")
                    continue;
                (spelled.IsInterface ? interfaces : baseClasses).Add(spelled.Name);
            }

            // C# ordering: a single primary constraint first (struct, class, or a
            // base class — class is invalid alongside a base class, so suppress it
            // when one is present), then interface constraints, then new() last.
            if (valueType)
                parts.Add("struct");
            else if (referenceType && baseClasses.Count == 0)
                parts.Add("class");
            parts.AddRange(baseClasses);
            parts.AddRange(interfaces);

            // `struct` already implies a public parameterless constructor.
            if (defaultCtor && !valueType)
                parts.Add("new()");

            if (parts.Count > 0)
                clauses.Add($"where {Identifier(reader.GetString(parameter.Name))} : {string.Join(", ", parts)}");
        }

        return clauses.Count == 0 ? "" : " " + string.Join(" ", clauses);
    }

    static GenericScope ConstraintGenericScope(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        var names = GenericParameterNames(reader, handles);
        if (handles.Count == 0)
            return GenericScope.Empty;

        var parent = reader.GetGenericParameter(handles.First()).Parent;
        if (parent.Kind != HandleKind.MethodDefinition)
            return new GenericScope(names, []);

        var method = reader.GetMethodDefinition((MethodDefinitionHandle)parent);
        var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
        return new GenericScope(GenericParameterNames(reader, declaringType.GetGenericParameters()), names);
    }

    static ImmutableArray<string> GenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
        => handles
            .Select(handle => Identifier(reader.GetString(reader.GetGenericParameter(handle).Name)))
            .ToImmutableArray();

    /// <summary>
    /// A reliably spellable name for a generic-constraint type and whether it is
    /// an interface, or null to skip it. Only a top-level
    /// <see cref="TypeDefinition"/> or an assembly-scoped <see cref="TypeReference"/>
    /// is spelled; nested, generic-instance (TypeSpec), and generic-parameter
    /// constraints are skipped so the clause never names something the unit cannot
    /// bind. A cross-assembly reference's interface-ness is not visible from the
    /// target reader, so it is treated as an interface — almost all cross-assembly
    /// constraint types are interfaces, and a base-class constraint there does not
    /// carry the reference-type flag that would make `class, Base` invalid.
    /// </summary>
    static (string Name, bool IsInterface)? ConstraintTypeName(MetadataReader reader, EntityHandle handle, GenericScope genericScope)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                if (!definition.GetDeclaringType().IsNil)
                    return null;
                bool isInterface = (definition.Attributes & TypeAttributes.Interface) != 0;
                return (FullName(reader, definition), isInterface);
            case HandleKind.TypeReference:
                var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                return reference.ResolutionScope.Kind is HandleKind.AssemblyReference
                    or HandleKind.ModuleDefinition or HandleKind.ModuleReference
                    ? (FullName(reader, reference), true)
                    : null;
            case HandleKind.TypeSpecification:
                TypeRef type;
                try { type = TypeRefDecoder.Instance.GetTypeFromSpecification(reader, genericScope, (TypeSpecificationHandle)handle, 0); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
                return IsProtobufIMessageConstraint(type)
                    ? (Clean(FullyQualifiedTypeName(type)), true)
                    : null;
            default:
                return null;
        }
    }

    static bool IsProtobufIMessageConstraint(TypeRef type)
        => type is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType:
            {
                Kind: TypeRefKind.Definition,
                Assembly: "Google.Protobuf",
                Namespace: "Google.Protobuf",
                Name: "IMessage`1"
            },
            TypeArguments.Length: 1
        };

    /// <summary>C# keywords that can appear as IL identifiers get an @ escape.</summary>
    static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
        ? "@" + name : name;

    /// <summary>
    /// The <c>[InlineArray(N)]</c> attribute text for an inline-array struct, or
    /// null when the type does not carry it. The attribute has a single int32
    /// constructor argument (the buffer length); the blob is a positional-only
    /// custom attribute (prolog <c>0x0001</c>, the int32, no named arguments),
    /// read directly so the harness needs no attribute type provider.
    /// </summary>
    static string? InlineArrayAttributeText(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var ah in typeDef.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(ah);
            if (AttributeTypeName(reader, attribute) != "InlineArrayAttribute")
                continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.Length < 6 || blob.ReadUInt16() != 1)
                return null; // not the expected positional-int prolog
            return $"[System.Runtime.CompilerServices.InlineArray({blob.ReadInt32()})]";
        }
        return null;
    }

    static bool IsByRefLike(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var ah in typeDef.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(ah);
            if (AttributeTypeFullName(reader, attribute) == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Compiler-embedded attribute definitions have compiler-mandated source
    /// shapes. The skeleton's public/unsafe stubs violate those shapes (CS9271)
    /// and the target body never needs these private implementation attributes to
    /// bind, so omit them from compile-back units.
    /// </summary>
    static bool IsCompilerEmbeddedAttributeType(MetadataReader reader, TypeDefinition typeDef)
    {
        string ns = reader.GetString(typeDef.Namespace);
        string name = reader.GetString(typeDef.Name);
        if ((ns, name) is ("Microsoft.CodeAnalysis", "EmbeddedAttribute")
            or ("System.Runtime.CompilerServices", "NullableAttribute")
            or ("System.Runtime.CompilerServices", "NullableContextAttribute")
            or ("System.Runtime.CompilerServices", "RefSafetyRulesAttribute"))
        {
            return true;
        }

        foreach (var ah in typeDef.GetCustomAttributes())
            if (AttributeTypeFullName(reader, reader.GetCustomAttribute(ah)) == "Microsoft.CodeAnalysis.EmbeddedAttribute")
                return true;
        return false;
    }

    /// <summary>The unqualified name of a custom attribute's type (its constructor's declaring type).</summary>
    static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name),
                    HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent).Name),
                    _ => "",
                };
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name);
            default:
                return "";
        }
    }

    static string AttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)member.Parent)),
                    HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent)),
                    _ => "",
                };
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return FullName(reader, reader.GetTypeDefinition(method.GetDeclaringType()));
            default:
                return "";
        }
    }

    /// <summary>Drops the metadata generic-arity suffix (<c>Foo`1</c> → <c>Foo</c>).</summary>
    static string StripArity(string name)
        => string.Join("+", name.Split('+').Select(StripSegmentArity));

    static string StripSegmentArity(string name)
    {
        int tick = name.LastIndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>
    /// The lightweight <see cref="SignatureDecoder"/> can render some unresolved
    /// shapes as invalid C#: unresolved generic parameters as <c>!n</c>/<c>!!n</c>,
    /// and older function-pointer decoders as a bare <c>delegate*</c> (no
    /// signature). A single such member would sink the whole single-unit skeleton,
    /// so spellings are repaired to the nearest compiling form. The target body
    /// rarely touches these; when it does the diff is honestly attributable to the
    /// gap.
    /// </summary>
    static string Clean(string type)
    {
        if (type.Contains('!'))
            return "object";
        if (type == "delegate*")
            return "void*"; // a pointer-sized stand-in; calls through it are rare
        if (type.StartsWith("delegate*", StringComparison.Ordinal)
            || type.StartsWith("ref delegate*", StringComparison.Ordinal))
            return type;
        return EscapeTypeKeywords(type);
    }

    /// <summary>
    /// `@`-escapes keyword identifier segments in a decoded type name — a generic
    /// parameter, namespace, or type segment literally named `event`/`class`/etc.
    /// The Roslyn-free metadata signature decoder emits these bare, but the
    /// declaration side already escapes them (via <see cref="Identifier"/>), so an
    /// unescaped reference is invalid C# (`public event @return;`) that poisons the
    /// whole reconstructed unit. A standalone primitive type keyword (`int`,
    /// `void`, …) and the structural `ref` prefix are legitimate spellings and stay
    /// bare; the same word as a *qualified* segment (preceded by `.`, so it can only
    /// be a namespace/type identifier) is escaped to match its escaped declaration.
    /// A standalone generic argument literally named with a primitive keyword stays
    /// ambiguous at string level (the decoder yields the same text for the primitive
    /// and the identifier) — an astronomically-rare case this does not regress.
    /// </summary>
    static string EscapeTypeKeywords(string type)
    {
        if (type.Length == 0 || !type.Any(c => char.IsLetter(c) || c == '_'))
            return type;
        var sb = new StringBuilder(type.Length);
        int i = 0;
        while (i < type.Length)
        {
            char c = type[i];
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < type.Length && (char.IsLetterOrDigit(type[i]) || type[i] == '_'))
                    i++;
                string word = type[start..i];
                bool alreadyEscaped = start > 0 && type[start - 1] == '@';
                bool qualifiedSegment = start > 0 && type[start - 1] == '.';
                // A bare primitive/`ref` is a real type spelling/by-ref prefix; the
                // same word after a `.` can only be an identifier segment.
                bool bareSpelling = (word is "void" or "ref" || IsPrimitiveTypeName(word)) && !qualifiedSegment;
                if (!alreadyEscaped && !bareSpelling
                    && (SyntaxFacts.GetKeywordKind(word) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(word) != SyntaxKind.None))
                    sb.Append('@');
                sb.Append(word);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// `@`-escapes each dotted segment of a namespace so a reserved-keyword segment
    /// (`namespace Foo.event`) is legal C# and matches the escaped type references
    /// <see cref="EscapeTypeKeywords"/> produces.
    /// </summary>
    static string EscapeNamespace(string ns) => string.Join('.', ns.Split('.').Select(Identifier));

    static string? ConstantText(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil)
            return null;
        var constant = reader.GetConstant(handle);
        var blob = reader.GetBlobReader(constant.Value);
        try
        {
            return constant.TypeCode switch
            {
                ConstantTypeCode.Boolean => blob.ReadBoolean() ? "true" : "false",
                ConstantTypeCode.Char => $"(char){(int)blob.ReadChar()}",
                ConstantTypeCode.SByte => blob.ReadSByte().ToString(),
                ConstantTypeCode.Byte => blob.ReadByte().ToString(),
                ConstantTypeCode.Int16 => blob.ReadInt16().ToString(),
                ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(),
                ConstantTypeCode.Int32 => blob.ReadInt32().ToString(),
                ConstantTypeCode.UInt32 => blob.ReadUInt32() + "U",
                ConstantTypeCode.Int64 => blob.ReadInt64() + "L",
                ConstantTypeCode.UInt64 => blob.ReadUInt64() + "UL",
                ConstantTypeCode.Single => Invariant(blob.ReadSingle()) + "f",
                ConstantTypeCode.Double => Invariant(blob.ReadDouble()),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    static string Invariant(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

}
