using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>
/// Produces C#-flavored type <em>shapes</em> — the skeletal facts a consumer needs
/// to render a type declaration and its members without decompiling method bodies,
/// loading the inspected assembly, or using Roslyn. It is the C#-spelling companion
/// to the metadata-spelling producer (<c>System.Int32</c> vs <c>int</c>), and stays
/// SRM-only and NativeAOT-friendly so consumers that only want skeletons never take
/// the decompiler dependency. The decompiler layer coordinates with this producer to
/// fill selected members with full bodies.
///
/// These are its first capabilities; type/member/stubbed-shell shaping migrates here
/// as the shell composition currently trapped in the ReturnToSender harness folds in.
/// </summary>
public static class TypeShellProducer
{
    /// <summary>
    /// True when the selected method's emitted body requires a C# <c>async</c>
    /// modifier. Runtime async is identified by <see cref="MethodImplAttributes.Async"/>;
    /// classic async by its state-machine attribute.
    ///
    /// This fact belongs only to full-body production. API skeletons deliberately
    /// omit <c>async</c> because it is not part of the callable surface. Async
    /// iterators are also withheld until their kickoff is reconstructed to a
    /// source iterator body; adding <c>async</c> to the raw state-machine-return
    /// expression would make otherwise-compilable output invalid.
    /// </summary>
    public static bool RequiresAsyncBodyModifier(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle)
    {
        if (methodHandle.IsNil)
            return false;

        var method = reader.GetMethodDefinition(methodHandle);
        var classification = MethodClassificationScanner.ClassifyAsyncMethod(reader, method);
        return classification == MethodClassification.RuntimeAsync
            || classification == MethodClassification.StateMachineAsync
                && AttributeReader.HasAttribute(
                    reader,
                    method.GetCustomAttributes(),
                    KnownAttributeNames.AsyncStateMachineAttribute);
    }

    /// <summary>
    /// Token-addressed form for an <see cref="ApiMember"/> extracted from the same
    /// reader. Non-MethodDef and invalid tokens fail closed.
    /// </summary>
    public static bool RequiresAsyncBodyModifier(
        MetadataReader reader,
        int metadataToken)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(metadataToken);
            return handle.Kind == HandleKind.MethodDefinition
                && RequiresAsyncBodyModifier(reader, (MethodDefinitionHandle)handle);
        }

        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reader-free form for a method selection produced by Metadata.
    /// </summary>
    public static bool RequiresAsyncBodyModifier(MethodBodySelection selection)
        => selection.AsyncClassification == MethodClassification.RuntimeAsync
            || selection.AsyncClassification == MethodClassification.StateMachineAsync
                && selection.HasAsyncStateMachineAttribute;

    /// <summary>
    /// True when a C# type display name cannot be represented on a skeletal type
    /// surface (function pointers, compiler-generated <c>&lt;&gt;</c> names, or
    /// anonymous/tuple <c>{</c> shapes). Consumers drop such members.
    ///
    /// Callers must pass an already-normalized C# display name; this method is a pure
    /// substring heuristic and does not itself normalize.
    /// </summary>
    public static bool IsUnsupportedSurfaceSignature(string signature)
        => signature.Contains("delegate*", StringComparison.Ordinal)
            || signature.Contains("@delegate*", StringComparison.Ordinal)
            || signature.Contains("<>", StringComparison.Ordinal)
            || signature.Contains('{', StringComparison.Ordinal);

    public static CSharpFixedBufferField? FixedBufferField(MetadataReader reader, FieldDefinition field)
        => FixedBufferMetadata.Read(reader, field.GetCustomAttributes()) is { } metadata
            ? new CSharpFixedBufferField(CSharpFormatter.CleanTypeDisplay(metadata.ElementTypeFullName), metadata.Length)
            : null;

    /// <summary>
    /// The base type name a skeletal type shape should reconstruct for
    /// <paramref name="typeDef"/>, or <see langword="null"/> when the base should
    /// be dropped (left to its compiler-implied default).
    ///
    /// Attributes always keep their <c>System.Attribute</c> base. Otherwise only a
    /// same-assembly (<see cref="HandleKind.TypeDefinition"/>) non-generic plain
    /// class base is reconstructed: external (TypeReference) and generic
    /// instantiation (TypeSpecification) bases are dropped because the shell cannot
    /// own their construction, and object-family / value-type / delegate bases are
    /// left implicit to avoid conflicts. <paramref name="isClass"/> is the caller's
    /// resolved kind (records/structs/enums/delegates pass <see langword="false"/>).
    /// </summary>
    public static string? ReconstructedBaseTypeName(MetadataReader reader, TypeDefinition typeDef, bool isClass)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return null;
        if (typeDef.BaseType.IsNil)
            return null;

        string? baseType;
        try
        {
            baseType = TypeResolver.GetTypeName(reader, typeDef.BaseType, GenericContext.ForType(reader, typeDef));
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        if (baseType is null)
            return null;
        // Attributes must derive from System.Attribute (existing behavior).
        if (baseType is "System.Attribute")
            return baseType;
        // Only reconstruct same-assembly base classes. External (TypeReference) and
        // generic-instantiation (TypeSpecification) bases are dropped: the shell
        // cannot own their construction, so an external base whose only constructor
        // is parameterized would make the derived stub's implicit `: base()` fail
        // (CS7036) where the baseline compiled without a base.
        if (typeDef.BaseType.Kind != HandleKind.TypeDefinition)
            return null;
        // Only plain classes reconstruct a real base class; records/structs/enums/
        // delegates keep their compiler-implied base to avoid primary-constructor
        // and value-type conflicts. A generic type's base can reference its own type
        // parameters, which the flat shell does not carry, so skip it.
        if (!isClass || typeDef.GetGenericParameters().Count != 0)
            return null;
        if (baseType is "System.Object" or "System.ValueType" or "System.Enum"
            or "System.Delegate" or "System.MulticastDelegate")
            return null;
        // No C# surface-representability gate here. That check belongs on the C#
        // display form (where `{`, `delegate*`, and generated `<>` are judged after
        // normalization), which is a caller concern: the harness applies it on the
        // Clean'd display name. Running the pure heuristic on the RAW metadata name
        // here would diverge from that normalized decision (e.g. wrongly dropping a
        // generated `<>`-named base). This method owns only the metadata-level gates.
        return baseType;
    }

    /// <summary>
    /// The C# display spelling of the base type a skeletal type shape should
    /// reconstruct for <paramref name="typeDef"/>, or <see langword="null"/> when no
    /// base is reconstructed (left implicit) or the reconstructed base cannot be
    /// represented on a C# surface.
    ///
    /// Combines the metadata-level base gate (<see cref="ReconstructedBaseTypeName"/>)
    /// with C# normalization (<see cref="CSharpFormatter.CleanTypeDisplay"/>) and the
    /// surface-representability gate (<see cref="IsUnsupportedSurfaceSignature"/>) so
    /// the seam owns the full base-type spelling decision end to end.
    /// </summary>
    public static string? ReconstructedBaseTypeDisplay(MetadataReader reader, TypeDefinition typeDef, bool isClass)
    {
        var baseType = ReconstructedBaseTypeName(reader, typeDef, isClass);
        if (baseType is null)
            return null;
        string display = CSharpFormatter.CleanTypeDisplay(baseType);
        return IsUnsupportedSurfaceSignature(display) ? null : display;
    }

    /// <summary>
    /// True when <paramref name="typeDef"/> is a C# <c>static class</c> — a type that
    /// is both <c>abstract</c> and <c>sealed</c> and is not an interface. Interfaces
    /// are also abstract, so they are excluded explicitly to avoid misreading an
    /// interface as a static class.
    /// </summary>
    public static bool IsStaticType(TypeDefinition typeDef)
        => (typeDef.Attributes & TypeAttributes.Abstract) != 0
            && (typeDef.Attributes & TypeAttributes.Sealed) != 0
            && (typeDef.Attributes & TypeAttributes.Interface) == 0;

    /// <summary>
    /// Composes a <see cref="CSharpTypePrintRequest"/> for a reconstructed type shell
    /// from a neutral <see cref="CSharpTypeShellSpec"/> and the type's own metadata.
    /// The consumer supplies member discovery, body policies, and the C# spelling of
    /// the type's name/base/interfaces; this seam owns assembling the
    /// <see cref="ApiType"/> — kind text, generic parameters, custom attributes, and
    /// abstract/sealed/static modifiers read straight from metadata — and recursing
    /// into nested shells. SRM-only, Roslyn-free, and NativeAOT-friendly.
    /// </summary>
    public static CSharpTypePrintRequest BuildPrintRequest(MetadataReader reader, CSharpTypeShellSpec spec)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(spec);

        var typeDef = reader.GetTypeDefinition(spec.Handle);
        var members = spec.MemberPolicies.Select(policy => policy.Member).ToList();
        var type = new ApiType
        {
            Namespace = spec.Namespace,
            Name = spec.MetadataName,
            MetadataName = spec.MetadataName,
            Kind = TypeKindText(spec.Kind),
            BaseType = ReconstructedBaseTypeDisplay(reader, typeDef, spec.Kind == CSharpTypeShellKind.Class),
            TypeParameters = MetadataDeclarationQuery.GetTypeParameters(reader, typeDef).ToList(),
            Interfaces = spec.InterfaceDisplayNames.ToList(),
            Members = members,
            Attributes = AttributeReader.RenderAttributes(reader, typeDef.GetCustomAttributes(), qualifyNames: true),
            IsAbstract = (typeDef.Attributes & TypeAttributes.Abstract) != 0
                && (typeDef.Attributes & TypeAttributes.Interface) == 0,
            IsSealed = (typeDef.Attributes & TypeAttributes.Sealed) != 0,
            IsStatic = IsStaticType(typeDef),
            EnumUnderlyingType = spec.Kind == CSharpTypeShellKind.Enum
                ? EnumUnderlyingType(reader, typeDef)
                : null,
        };

        return new CSharpTypePrintRequest(
            type,
            members: members,
            memberPolicyOverrides: spec.MemberPolicies,
            primaryConstructorParameters: spec.PrimaryConstructorParameters,
            nestedTypes: spec.NestedTypes
                .Select(nested => BuildPrintRequest(reader, nested))
                .ToList());
    }

    static string TypeKindText(CSharpTypeShellKind kind)
        => kind switch
        {
            CSharpTypeShellKind.Class => "class",
            CSharpTypeShellKind.Record => "record",
            CSharpTypeShellKind.Struct => "struct",
            CSharpTypeShellKind.Interface => "interface",
            CSharpTypeShellKind.Enum => "enum",
            CSharpTypeShellKind.Delegate => "delegate",
            _ => throw new NotSupportedException($"Unsupported C# type-shell kind '{kind}'."),
        };

    // An enum's underlying type is carried by its special `value__` instance field.
    // A reconstructed enum that omits a non-int base (e.g. `: long`) defaults to int
    // and fails to bind members whose constant values do not fit int (CS0266), so the
    // shell must reproduce it. The declaration writer suppresses the redundant `: int`.
    static string? EnumUnderlyingType(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (reader.GetString(field.Name) == "value__")
                return MetadataDeclarationQuery.GetField(reader, typeDef, field).ReturnType;
        }

        return null;
    }
}
