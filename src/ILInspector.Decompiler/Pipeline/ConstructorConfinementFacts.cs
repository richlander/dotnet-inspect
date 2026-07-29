using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Proves the single metadata fact <see cref="MethodRef.ConstructorEffectFree"/>
/// consumed by <see cref="Passes.ObjectInitializerPass"/>: whether a same-assembly
/// parameterless constructor has no observable effect beyond allocating the fresh
/// instance, so <c>newobj T()</c> can be reordered past an enclosing call's
/// <c>this</c>-field receiver read when folding an object-initializer argument.
///
/// <para>The gate is deliberately the <em>narrowest defensible</em> shape rather than
/// a general constructor-effect/escape analysis. Adversarial review (issue #3459,
/// rounds 3–4) established that a broad "parameterless ctor is confined" assumption is
/// unsound: <c>newobj</c> also runs the type initializer (<c>.cctor</c>), a base or
/// self call on the fresh receiver can escape (<c>base()</c> registering the instance,
/// a property setter mutating a global), a ctor that reads a static triggers <em>that</em>
/// type's <c>.cctor</c>, and a byref argument can alias external storage. This proof
/// sidesteps every one of those by requiring the constructor body to be exactly:</para>
///
/// <code>
///   ldarg.0
///   call instance void [CoreLib]System.Object::.ctor()
///   ret
/// </code>
///
/// <para>A body of exactly that shape performs no field write, no static read/write,
/// no call other than the base <c>.ctor</c>, no branch, and carries no exception region
/// — so it cannot reach or mutate anything the receiver read observes. Chaining to the
/// declaring type's direct base ctor, together with the base resolving (by TYPED
/// core-library identity, not spelling — see <see cref="Stamp"/>) to the real
/// <c>System.Object</c>, proves the declaring type derives directly from <c>Object</c>
/// (a non-<c>Object</c> base would be constructed by a <c>call</c> to <em>its</em> ctor),
/// so no intermediate base <c>.cctor</c> exists; the only remaining type-initializer is
/// the declaring type's own <c>.cctor</c>, which the gate rejects by requiring the type
/// to declare none. A crafted assembly cannot substitute a side-effecting base by
/// planting a sibling type spelled <c>System.Object</c>: the base is identified by its
/// resolved <see cref="TypeRef.CoreLibrary"/> assembly, which a lookalike cannot forge.</para>
///
/// <para>The proof is <em>Roslyn-faithful</em>, not arbitrary-IL-sound: it assumes a
/// non-null <c>this</c> (a hand-crafted <c>call</c> to an instance method with a null
/// receiver could make the hoisted field read throw <c>NullReferenceException</c> before
/// the <c>newobj</c>). That matches the compiler-emitted IL the decompiler targets and is
/// documented on the consuming pass.</para>
/// </summary>
internal static class ConstructorConfinementFacts
{
    /// <summary>
    /// Stamps <see cref="MethodRef.ConstructorEffectFree"/> onto <paramref name="ctor"/>
    /// when <paramref name="handle"/> is a same-assembly parameterless instance
    /// constructor whose body proves the effect-free shape and whose declaring type
    /// declares no static constructor; returns the ctor unchanged otherwise.
    /// </summary>
    internal static MethodRef Stamp(MetadataSource source, MethodDefinitionHandle handle, MethodRef ctor)
    {
        try
        {
            // Only a parameterless instance ctor can match the trivial shape.
            if (!ctor.HasThis || !ctor.ParameterTypes.IsDefaultOrEmpty)
                return ctor;

            var reader = source.Reader;
            var method = reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.Static) != 0)
                return ctor;
            if (method.RelativeVirtualAddress == 0)
                return ctor;

            var declaringType = method.GetDeclaringType();
            if (DeclaresStaticConstructor(reader, declaringType))
                return ctor;

            // Anchor the direct-Object-derivation lemma on the pipeline's TYPED core-
            // library identity, not on the base-call token's namespace/name spelling.
            // A crafted assembly can name its own base type `System.Object` (a
            // cross-assembly MemberReference to a planted `[Evil]System.Object::.ctor`
            // passes a namespace+name check), so proving the ctor chains to a type
            // *spelled* Object does NOT prove it derives from the real Object. Resolving
            // the declaring type's actual base through the importer yields a TypeRef
            // whose Assembly is the resolved core library (TypeRef.CoreLibrary) only for
            // the genuine Object; a lookalike resolves to its own defining assembly and
            // is rejected. This is the "typed identity over display text" rule.
            if (!MemberIdentity.IsCoreLibraryType(source.ResolveBaseType(ctor.DeclaringType), "System", "Object"))
                return ctor;

            if (!BodyIsTrivialObjectCtorChain(source, method))
                return ctor;

            return ctor with { ConstructorEffectFree = true };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        {
            return ctor;
        }
    }

    /// <summary>
    /// Whether <paramref name="typeHandle"/> declares a static constructor, so
    /// <c>newobj</c> would trigger a type-initializer side effect the fold must not
    /// reorder across.
    /// </summary>
    static bool DeclaresStaticConstructor(MetadataReader reader, TypeDefinitionHandle typeHandle)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.Static) != 0
                && (method.Attributes & MethodAttributes.SpecialName) != 0
                && reader.StringComparer.Equals(method.Name, ".cctor"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the constructor body is exactly <c>ldarg.0; call System.Object::.ctor;
    /// ret</c> with no exception regions — the only shape proven to have no observable
    /// effect beyond the allocation.
    /// </summary>
    static bool BodyIsTrivialObjectCtorChain(MetadataSource source, MethodDefinition method)
    {
        var body = source.Pe.GetMethodBody(method.RelativeVirtualAddress);
        if (body.ExceptionRegions.Length != 0)
            return false;

        var il = body.GetILReader();

        // ldarg.0
        if (il.RemainingBytes == 0 || il.ReadByte() != 0x02)
            return false;

        // call <token>
        if (il.RemainingBytes == 0 || il.ReadByte() != 0x28)
            return false;
        if (il.RemainingBytes < 4)
            return false;
        var target = MetadataTokens.EntityHandle(il.ReadInt32());
        if (!IsObjectConstructor(source.Reader, target))
            return false;

        // ret, and nothing after it.
        if (il.RemainingBytes == 0 || il.ReadByte() != 0x2A)
            return false;

        return il.RemainingBytes == 0;
    }

    /// <summary>
    /// Whether <paramref name="target"/> is structurally a base-constructor chain to a
    /// type <em>spelled</em> <c>System.Object</c> — a cross-assembly
    /// <c>MemberReference</c> named <c>.ctor</c> whose parent <c>TypeReference</c> is
    /// namespace <c>System</c>, name <c>Object</c>. This is a NECESSARY shape check, not
    /// a sufficient identity proof: a crafted assembly can plant its own
    /// <c>System.Object</c> in a sibling assembly, and that cross-assembly lookalike
    /// passes this spelling check. The AUTHORITATIVE core-library identity is enforced
    /// in <see cref="Stamp"/> via <see cref="MetadataSource.ResolveBaseType"/> +
    /// <see cref="MemberIdentity.IsCoreLibraryType"/> (typed <see cref="TypeRef.CoreLibrary"/>
    /// identity a lookalike cannot forge). This check remains to confirm the trivial
    /// body's single call really is a base <c>.ctor</c> chain rather than an arbitrary
    /// method invocation of matching shape.
    /// </summary>
    static bool IsObjectConstructor(MetadataReader reader, EntityHandle target)
    {
        if (target.Kind != HandleKind.MemberReference)
            return false;

        var member = reader.GetMemberReference((MemberReferenceHandle)target);
        if (!reader.StringComparer.Equals(member.Name, ".ctor"))
            return false;
        if (member.Parent.Kind != HandleKind.TypeReference)
            return false;

        var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
        return reader.StringComparer.Equals(type.Name, "Object")
            && reader.StringComparer.Equals(type.Namespace, "System");
    }
}
