using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis;

/// <summary>
/// Whether an assembly could contain a <em>direct</em> caller of a member declared by a given type,
/// decided from its <c>TypeRef</c> table alone.
///
/// This is the narrower sibling of <see cref="CallerScopeFilter"/>, and the two answer genuinely
/// different questions because their consumers walk differently. A caller <em>graph</em> is
/// transitive, so it must select the reverse-reference closure over the scope — an assembly that
/// never names the target still belongs in the graph if it calls something that does. Inbound
/// <em>edges</em> are not transitive: the consumer scans each scope for call sites whose callee
/// matches the target and stops there. Nothing upstream is ever asked for, so nothing upstream
/// needs to be opened, and the closure that the graph requires is pure cost for the edge list.
///
/// The saving is the whole point. Reference closure over a framework-shaped scope selects nearly
/// everything — every assembly reaches the core library in one hop, so transitivity makes almost
/// any in-graph target select almost every candidate. Direct type reference does not: on a
/// 182-assembly framework scope, a target in <c>System.Collections.Immutable</c> goes from 169
/// selected candidates to 3. Each candidate ruled out here is a full method-body decode not paid.
///
/// <para><b>Why this is exactly as permissive as the matcher.</b>
/// <see cref="MemberPattern.MatchesCrossAssembly"/> requires the candidate callee's open declaring
/// type to be <see cref="TypeRef.Equals"/> to the target's, and that equality includes
/// <see cref="TypeRef.Assembly"/>. A cross-assembly callee reaches its declaring type either
/// through the <c>TypeRef</c> table directly or through a <c>TypeSpec</c> whose signature bottoms
/// out at a <c>TypeRef</c> — <see cref="GenericMemberIdentity.OpenDeclaringType"/> reduces a
/// constructed instantiation back to that row. So every declaring type a candidate can spell at a
/// call site is the decoding of one of the rows scanned here.</para>
///
/// <para>That equivalence is by construction rather than by argument: this scan decodes each row
/// with the same <see cref="TypeRefDecoder"/> the call-site decoding uses, so nested-type naming,
/// generic arity, module-scoped and nil resolution scopes, and core-library facade canonicalization
/// are not re-derived here and cannot drift from it.</para>
///
/// <para><b>Types the candidate defines itself.</b> A callee declared by a <c>TypeDef</c> is
/// decoded against the defining image's own name and appears in no <c>TypeRef</c> row, so an image
/// whose own identity equals the target's is kept without scanning. This is the
/// scope-contains-a-copy-of-the-target case.
///
/// That identity is asked of <see cref="TypeRefDecoder"/> rather than recomputed here, for the
/// same reason the row scan reuses it. Deriving it by hand got it wrong: an image with no assembly
/// manifest gives its definitions the <em>empty</em> assembly name, which
/// <see cref="MemberPattern.MatchesCrossAssembly"/> compares like any other, so a
/// <c>reader.IsAssembly</c> guard skipped the check entirely and ruled such a module out even for
/// a type it declares itself. Every definition in an image carries the same assembly identity, so
/// one row answers for all of them.</para>
///
/// <para><b>Undecidable is kept.</b> A row that cannot be read, or a target that is not a plain
/// type definition, yields <see cref="TypeReferenceState.Undecidable"/> and the candidate stays in
/// the scope. Ruling out on unread metadata would drop callers the matcher would have found.</para>
///
/// Wider type forwarding — a candidate reaching the target only through a facade outside the
/// core-library alias set — is modelled by <see cref="ForwardedTypeAliases"/>, which both this
/// filter and <see cref="MemberPattern.MatchesCrossAssembly"/> consult through the same
/// <see cref="ForwardedTypeAliases.DenotesSameType"/> call. Passing a null or empty alias set
/// gives exactly the identity-only behavior this filter had before #3419; passing one the matcher
/// does not also receive would make this filter narrower than the matcher, which is the one
/// arrangement that loses callers.
/// </summary>
public static class CallerScopeTypeFilter
{
    /// <summary>Whether an image can spell a given declaring type at a call site.</summary>
    public enum TypeReferenceState
    {
        /// <summary>The image names the type, or defines it. It stays in the scope.</summary>
        Names,

        /// <summary>
        /// Every reference was read and none names the type, so no call site in this image can
        /// produce a matching callee. The image is ruled out without decoding its bodies.
        /// </summary>
        DoesNotName,

        /// <summary>
        /// The question could not be decided. Kept, because an unread row could have been the one
        /// naming the target.
        /// </summary>
        Undecidable,
    }

    /// <summary>Classifies a candidate image with no forwarding aliases (identity comparison only).</summary>
    public static TypeReferenceState Classify(MetadataReader reader, TypeRef openDeclaringType)
        => Classify(reader, openDeclaringType, aliases: null);

    /// <summary>
    /// Classifies one candidate image against the open declaring type of the target member.
    /// <paramref name="openDeclaringType"/> must be the same value
    /// <see cref="MemberPattern.MatchesCrossAssembly"/> compares against — see
    /// <see cref="GenericMemberIdentity.OpenDeclaringType"/>.
    ///
    /// <paramref name="aliases"/> must be the same instance the matcher is given. The two are
    /// required to be equally permissive, and this is the seam where that is arranged rather than
    /// argued: a row is kept when it decodes to the target <em>or</em> to a facade spelling of it,
    /// which is precisely the disjunction the matcher applies.
    /// </summary>
    public static TypeReferenceState Classify(
        MetadataReader reader,
        TypeRef openDeclaringType,
        ForwardedTypeAliases? aliases)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(openDeclaringType);

        // Only a plain type definition has an assembly-qualified identity to filter on. Anything
        // else (an array, a pointer, an undecoded reference) is not a shape the matcher's declaring
        // type takes, so the filter declines to decide rather than guessing at it.
        if (openDeclaringType.Kind != TypeRefKind.Definition)
            return TypeReferenceState.Undecidable;

        try
        {
            // The identity that this image's own definitions carry is asked of the decoder rather
            // than recomputed here. Restating it is what made this branch disagree with the matcher
            // for module metadata: a definition in an image with no assembly manifest decodes to
            // the empty assembly name, which a hand-written `reader.IsAssembly` guard skips over
            // entirely while MatchesCrossAssembly happily compares it.
            foreach (var definitionHandle in reader.TypeDefinitions)
            {
                var own = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, definitionHandle, 0);
                if (own.Kind == TypeRefKind.Unsupported)
                    return TypeReferenceState.Undecidable;

                if (own.Assembly == openDeclaringType.Assembly)
                    return TypeReferenceState.Names;

                // Every definition in an image carries the same assembly identity, so one answers
                // for all of them.
                break;
            }

            foreach (var handle in reader.TypeReferences)
            {
                var decoded = TypeRefDecoder.Instance.GetTypeFromReference(reader, handle, 0);

                // A row that would not decode at a call site either cannot be ruled out on its
                // own contents, and the rows that did decode do not speak for it.
                if (decoded.Kind == TypeRefKind.Unsupported)
                    return TypeReferenceState.Undecidable;

                if (decoded.Equals(openDeclaringType))
                    return TypeReferenceState.Names;

                if (!ForwardedTypeAliases.DenotesSameType(decoded, openDeclaringType, aliases))
                    continue;

                // Matched only through an alias, so the strong-name identity behind the spelling is
                // checked here — the one place that still has it. A TypeRef records a bare assembly
                // name, so the matcher cannot tell two same-named assemblies apart; admitting this
                // row without the check would let a forwarder read from one of them fabricate a
                // caller that bound against the other.
                if (AliasedScopeIsTheEvidenceAssembly(reader, handle, aliases!))
                    return TypeReferenceState.Names;
            }
        }
        catch (BadImageFormatException)
        {
            return TypeReferenceState.Undecidable;
        }

        return TypeReferenceState.DoesNotName;
    }

    /// <summary>
    /// Whether the assembly reference a type-forwarded row resolves through really is the assembly
    /// that supplied the forwarder evidence, compared on public key token.
    ///
    /// <para>A row whose resolution scope is not an assembly reference — a nested type, or a
    /// module-scoped row — carries no identity to contradict, and is admitted. This check may only
    /// ever reject a spelling collision; it must never be the reason an ordinary candidate is ruled
    /// out, because that would make this filter narrower than the matcher.</para>
    /// </summary>
    static bool AliasedScopeIsTheEvidenceAssembly(
        MetadataReader reader,
        TypeReferenceHandle handle,
        ForwardedTypeAliases aliases)
    {
        var typeReference = reader.GetTypeReference(handle);
        if (typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return true;

        var reference = reader.GetAssemblyReference(
            (AssemblyReferenceHandle)typeReference.ResolutionScope);

        return aliases.EvidenceIdentityAgrees(
            reader.GetString(reference.Name),
            reader.GetBlobContent(reference.PublicKeyOrToken).AsSpan());
    }

    /// <summary>
    /// Classifies an image on disk. Opens the PE image for metadata only — no method bodies are
    /// decoded, which is the cost this filter exists to avoid paying for images that cannot match.
    /// An image that cannot be opened or carries no metadata is
    /// <see cref="TypeReferenceState.DoesNotName"/>: analysis opens the same path the same way, so
    /// it could not have contributed edges either.
    /// </summary>
    public static TypeReferenceState Classify(string path, TypeRef openDeclaringType)
        => Classify(path, openDeclaringType, aliases: null);

    /// <summary>
    /// Classifies an image on disk against the target type and the same
    /// <see cref="ForwardedTypeAliases"/> instance the matcher is given.
    /// </summary>
    public static TypeReferenceState Classify(
        string path,
        TypeRef openDeclaringType,
        ForwardedTypeAliases? aliases)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return TypeReferenceState.DoesNotName;

            return Classify(peReader.GetMetadataReader(), openDeclaringType, aliases);
        }
        catch (Exception ex) when (ex is BadImageFormatException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            return TypeReferenceState.DoesNotName;
        }
        catch
        {
            // An unanticipated failure says nothing about whether analysis could read the image.
            return TypeReferenceState.Undecidable;
        }
    }
}
