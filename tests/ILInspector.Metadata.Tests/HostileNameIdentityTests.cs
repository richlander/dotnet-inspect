using System.Text.Json;
using ILInspector.CSharp;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

#nullable enable

/// <summary>
/// Identity parity for members whose names carry a rendering hazard.
/// </summary>
/// <remarks>
/// Containment (issue #3319) respells a hostile name in the <em>display</em>
/// signature and leaves it raw in identity. That split has a sharp edge: the
/// raw-signature identity fallback, used whenever
/// <see cref="ApiMember.SignatureModel"/> is absent — which is always, after a
/// JSON round-trip, because it is <c>[JsonIgnore]</c> — locates the member name
/// by searching the display signature for the raw spelling. A respelling makes
/// that search miss, and the generic arity is silently dropped, so a
/// round-tripped <c>M&lt;T&gt;(int)</c> pairs as <c>M(int)</c> and no longer
/// matches the same member read live.
///
/// These names cannot come from a compiled fixture. C# admits Unicode category
/// Cf in identifiers, but ECMA-334 requires identifiers to be normalized with
/// formatting characters removed, so the compiler emits a clean metadata name.
/// The surface is therefore constructed directly.
/// </remarks>
public sealed class HostileNameIdentityTests
{
    private const string Hazard = "\u202E";

    /// <summary>
    /// The display spelling is written out literally rather than obtained by
    /// calling the product's containment, so this gate does not agree with a
    /// wrong answer from the code it is testing. All that matters to the claim
    /// is that the display spelling differs from the raw one.
    /// </summary>
    private static string Displayed(string name) => name.Replace(Hazard, "_");

    private static (ApiSurface Surface, ApiType Type, ApiMember Member) BuildSurface(string memberName, bool generic)
    {
        var typeParameters = generic
            ? new List<TypeParameter> { new() { Name = "T" } }
            : [];

        var member = new ApiMember
        {
            Name = memberName,
            Kind = "method",
            ReturnType = "int",
            Signature = generic
                ? $"int {Displayed(memberName)}<T>(int value)"
                : $"int {Displayed(memberName)}(int value)",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                CanonicalReturnType = "int",
                MemberName = generic ? $"{memberName}<T>" : memberName,
                TypeParameters = typeParameters,
                Parameters = [new ApiParameter { Name = "value", Type = "int" }],
            },
        };

        var type = new ApiType
        {
            Name = "Holder",
            Namespace = "Ns",
            Kind = "class",
            Members = [member],
        };

        var surface = new ApiSurface { Types = [type] };
        ApiMemberIdentity.PopulateCanonicalIdentities(surface);
        return (surface, type, member);
    }

    private static (ApiType Type, ApiMember Member) RoundTrip(ApiSurface surface, string memberName)
    {
        var json = JsonSerializer.Serialize(surface);
        var restored = JsonSerializer.Deserialize<ApiSurface>(json)!;
        var type = restored.Types.Single();
        return (type, type.Members.Single(m => m.Name == memberName));
    }

    [Fact]
    public void RoundTrip_HostileGenericMethodName_CanonicalIdentityMatchesLive()
    {
        var name = $"Generic{Hazard}Injected";
        var (surface, type, member) = BuildSurface(name, generic: true);

        var live = ApiMemberIdentity.GetCanonicalSignature(type, member);
        Assert.Contains("<T>", live, StringComparison.Ordinal);

        var (rtType, rtMember) = RoundTrip(surface, name);
        Assert.Null(rtMember.SignatureModel);

        Assert.Equal(live, ApiMemberIdentity.GetCanonicalSignature(rtType, rtMember));
    }

    [Fact]
    public void HostileGenericMethodName_PersistsCanonicalIdentity()
    {
        // The parity above must hold because identity was persisted while the
        // structural model was live, not because the text fallback happened to
        // agree. Without this, the test above could pass on a fallback that is
        // wrong in the same way on both sides.
        var (_, _, member) = BuildSurface($"Generic{Hazard}Injected", generic: true);
        Assert.False(string.IsNullOrEmpty(member.CanonicalSignature));
    }

    [Fact]
    public void CleanGenericMethodName_PersistsNothing()
    {
        // Churn guard: a clean name's display spelling equals its raw spelling,
        // so the fallback finds it and no CanonicalSignature is persisted. This
        // keeps the serialized form and digests of ordinary members unchanged.
        var (_, _, member) = BuildSurface("GenericClean", generic: true);
        Assert.Null(member.CanonicalSignature);
    }

    [Fact]
    public void RoundTrip_HostileNonGenericMethodName_CanonicalIdentityMatchesLive()
    {
        var name = $"Plain{Hazard}Injected";
        var (surface, type, member) = BuildSurface(name, generic: false);

        var live = ApiMemberIdentity.GetCanonicalSignature(type, member);
        var (rtType, rtMember) = RoundTrip(surface, name);

        Assert.Equal(live, ApiMemberIdentity.GetCanonicalSignature(rtType, rtMember));
    }

    [Fact]
    public void CanonicalIdentity_KeepsTheRawSpelling()
    {
        // Containment is a presentation concern. Identity must still carry the
        // raw metadata spelling, or two different hostile names could collapse
        // onto one identity.
        var name = $"Generic{Hazard}Injected";
        var (_, type, member) = BuildSurface(name, generic: true);

        Assert.Contains(Hazard, ApiMemberIdentity.GetCanonicalSignature(type, member), StringComparison.Ordinal);
    }

    /// <summary>
    /// Exhaustive linkage between respelling and identity: for every character in
    /// the BMP, if containment changes the displayed spelling of a name, then a
    /// member carrying that character must persist a canonical identity.
    /// </summary>
    /// <remarks>
    /// The two sides of this claim were originally two predicates —
    /// <c>ContainIdentifier</c> asked "line terminator OR rendering hazard" while
    /// the identity check asked only "rendering hazard" — and they drifted:
    /// U+2028 and U+2029 are line terminators that are not rendering hazards, so
    /// a name carrying one was respelled without persisting an identity and lost
    /// its generic arity across a JSON round-trip.
    ///
    /// This gate does not restate either predicate. It observes the *behavior*
    /// that matters — did the display spelling change? — and requires identity to
    /// follow it, so it holds for whatever the hazard set becomes next.
    /// </remarks>
    [Fact]
    public void EveryRespelledCharacter_PersistsCanonicalIdentity()
    {
        List<string> failures = [];

        for (int c = 0; c <= 0xFFFF; c++)
        {
            if (char.IsSurrogate((char)c))
                continue;

            var name = $"A{(char)c}B";
            var displayed = CSharpIdentifierCore.ContainIdentifier(name, _ => false);
            if (string.Equals(displayed, name, StringComparison.Ordinal))
                continue;

            var (surface, _, member) = BuildSurface(name, generic: true);
            ApiMemberIdentity.PopulateCanonicalIdentities(surface);

            if (member.CanonicalSignature is null)
                failures.Add($"U+{c:X4}");
        }

        Assert.True(
            failures.Count == 0,
            $"containment respells these characters but identity is not persisted for them: {string.Join(", ", failures)}");
    }
}
