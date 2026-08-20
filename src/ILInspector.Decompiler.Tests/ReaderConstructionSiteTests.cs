using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins every place in <c>ILInspector.Decompiler</c> that participates in
/// core-library identity trust: the methods that construct a
/// <see cref="PEReader"/>, and the methods that grant identity through
/// <c>CoreLibraryIdentityTrust</c>.
/// <para>
/// <see cref="PlantedCoreLibraryIdentityTests.EveryPublicFactory_ClassifiesTheReaderItCreates"/>
/// enumerates <em>factory signatures</em>, and three consecutive review rounds
/// on PR #4428 escaped it along a different cosmetic dimension each time: the
/// method name (round 4), the declared return type (round 5), then visibility
/// and <c>Task</c> wrapping (round 6, issue #4464). A signature has unboundedly
/// many such dimensions, so patching one per round does not converge.
/// </para>
/// <para>
/// This gate reads the compiled IL instead. A method that builds a reader
/// contains <c>newobj PEReader::.ctor</c> whatever it is called, however it is
/// declared, and whether or not its result is wrapped — so the observation
/// survives every escape above, and any future one of the same kind. Both
/// directions fail: an unpinned site that appears is an unreviewed way to
/// obtain a reader, and a pinned site that disappears is a stale entry.
/// </para>
/// <para>
/// Being listed here is not approval. <see cref="Grants"/> records whether a
/// site classifies the reader it creates, and most sites deliberately do not:
/// an unclassified reader loses core-library identity, which is the fail-closed
/// half of the design that <c>CoreLibraryIdentityTrust</c> documents. What the
/// pin buys is that adding either kind of site forces someone to say which it
/// is.
/// </para>
/// </summary>
public sealed class ReaderConstructionSiteTests
{
    /// <summary>
    /// Whether a trust-relevant site constructs a reader, grants core-library
    /// identity, or does both.
    /// </summary>
    [Flags]
    enum TrustRole
    {
        None = 0,
        ConstructsReader = 1,
        GrantsIdentity = 2,
    }

    /// <summary>
    /// Every trust-relevant method in <c>ILInspector.Decompiler</c>, with the
    /// role it plays and why. Update this table in the same commit that moves a
    /// site, and say in the reason which half of the design the new entry is
    /// on.
    /// </summary>
    static readonly ImmutableDictionary<string, (TrustRole Role, string Reason)> s_pinned =
        new Dictionary<string, (TrustRole, string)>(StringComparer.Ordinal)
        {
            ["Pipeline.MetadataContext.OpenDesignated"] =
                (TrustRole.GrantsIdentity,
                 "A raw path is an explicit caller designation, so the reader "
                 + "MetadataSource returns is trusted. This is the route that "
                 + "opens System.Private.CoreLib.dll from a dotnet/runtime "
                 + "build layout."),
            ["Pipeline.MetadataContext.OpenResolved"] =
                (TrustRole.GrantsIdentity,
                 "Discovery. Grants only when the acquisition entitles it, "
                 + "through CoreLibraryIdentityTrust.GrantIfEntitled."),
            ["Pipeline.OpenedAssembly.TryOpen"] =
                (TrustRole.ConstructsReader,
                 "Deliberately unclassified: a best-effort probe that carries "
                 + "no acquisition evidence, so anything opened through it "
                 + "fails closed and cannot mint core-library identity."),
            ["Pipeline.MetadataSource.OpenCore"] =
                (TrustRole.ConstructsReader | TrustRole.GrantsIdentity,
                 "Both OpenCore overloads land here. The raw-path overload "
                 + "grants unconditionally as an explicit designation; the "
                 + "ResolvedAssemblyReference overload defers to "
                 + "GrantIfEntitled because it was reached by discovery."),
            ["Pipeline.MetadataSource.OpenFromPrefetchedImage"] =
                (TrustRole.ConstructsReader | TrustRole.GrantsIdentity,
                 "Designation by path plus caller-supplied bytes; its one "
                 + "product caller, LibrarySections.ScanBodyShapes, passes the "
                 + "designated target's own path and image."),
            ["MemberBodyProducer.ComposeCore"] =
                (TrustRole.ConstructsReader,
                 "Deliberately unclassified: opens the assembly a located type "
                 + "came from to read method bodies. This is the sibling path "
                 + "from issue #4411, and it must not mint core-library identity."),
            ["MemberBodyProducer.ComposeMemberCore"] =
                (TrustRole.ConstructsReader,
                 "Deliberately unclassified, for the same reason as "
                 + "ComposeCore: a sibling opened to read one member's body."),
            ["MemberBodyProducer.ComposeMembersBatch"] =
                (TrustRole.ConstructsReader,
                 "Deliberately unclassified, for the same reason as "
                 + "ComposeCore: a sibling opened to read a batch of bodies."),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    [Fact]
    public void TrustRelevantSites_MatchThePin()
    {
        var observed = ScanTrustRelevantSites();

        Assert.NotEmpty(observed);

        var expected = s_pinned.ToImmutableDictionary(e => e.Key, e => e.Value.Role);
        var differences = new List<string>();

        foreach (string site in observed.Keys.Union(expected.Keys).Order(StringComparer.Ordinal))
        {
            observed.TryGetValue(site, out TrustRole actual);
            expected.TryGetValue(site, out TrustRole pinned);
            if (actual == pinned)
                continue;

            differences.Add(
                actual == TrustRole.None
                    ? $"  {site}: pinned as {pinned}, but no longer constructs a "
                      + "reader or grants identity. Remove the stale entry."
                    : pinned == TrustRole.None
                        ? $"  {site}: {actual}, but is not pinned. Add it to "
                          + "s_pinned and state whether it may mint core-library identity."
                        : $"  {site}: pinned as {pinned}, observed {actual}.");
        }

        Assert.True(
            differences.Count == 0,
            "The core-library trust surface of ILInspector.Decompiler no longer "
            + "matches its pin. Every method that constructs a PEReader or "
            + "grants core-library identity has to be listed, because an "
            + "unreviewed one is an unreviewed way to obtain a reader:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, differences));
    }

    /// <summary>
    /// Non-vacuity gate for <see cref="TrustRelevantSites_MatchThePin"/>. That
    /// test compares a scan against a table, and it would pass just as happily
    /// if the scanner silently observed nothing — a broken token comparison or
    /// a renamed trust type would empty both sides of a set difference. This
    /// asserts the scanner still finds the two things it looks for, so the
    /// pin cannot go quietly vacuous.
    /// </summary>
    [Fact]
    public void Scanner_ObservesBothConstructionAndGrantSites()
    {
        var observed = ScanTrustRelevantSites();

        Assert.Contains(
            observed,
            e => e.Value.HasFlag(TrustRole.ConstructsReader));
        Assert.Contains(
            observed,
            e => e.Value.HasFlag(TrustRole.GrantsIdentity));
    }

    /// <summary>
    /// Reads the compiled <c>ILInspector.Decompiler</c> assembly and reports
    /// every method whose IL constructs a <see cref="PEReader"/> or calls into
    /// <c>CoreLibraryIdentityTrust</c> to grant identity.
    /// </summary>
    static ImmutableDictionary<string, TrustRole> ScanTrustRelevantSites()
    {
        string assemblyPath = typeof(MetadataSource).Assembly.Location;
        Assert.True(
            File.Exists(assemblyPath),
            $"Cannot scan the decompiler assembly at '{assemblyPath}'.");

        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var sites = new Dictionary<string, TrustRole>(StringComparer.Ordinal);

        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            byte[] il = body.GetILBytes() ?? [];
            if (il.Length == 0)
                continue;

            TrustRole role = RoleOf(reader, il);
            if (role == TrustRole.None)
                continue;

            string name = SiteName(reader, method);

            // The trust type's own helpers call the grant they implement.
            // Reporting them would pin the mechanism as one of its own
            // consumers, which says nothing about where readers come from.
            if (name.StartsWith("Pipeline.CoreLibraryIdentityTrust.", StringComparison.Ordinal))
                continue;

            sites[name] = sites.TryGetValue(name, out TrustRole existing)
                ? existing | role
                : role;
        }

        return sites.ToImmutableDictionary(StringComparer.Ordinal);
    }

    static TrustRole RoleOf(MetadataReader reader, byte[] il)
    {
        TrustRole role = TrustRole.None;

        foreach (var instruction in InstructionDecoder.Decode(il))
        {
            if (instruction.Operand is not (OperandKind.InlineMethod or OperandKind.InlineTok))
                continue;

            var operand = MetadataTokens.EntityHandle((int)instruction.OperandValue);
            if (!TryDescribeMember(reader, operand, out string declaringType, out string member))
                continue;

            if (instruction.OpCode == ILOpCode.Newobj
                && declaringType == "System.Reflection.PortableExecutable.PEReader")
            {
                role |= TrustRole.ConstructsReader;
            }

            if (declaringType.EndsWith("CoreLibraryIdentityTrust", StringComparison.Ordinal)
                && member.StartsWith("Grant", StringComparison.Ordinal))
            {
                role |= TrustRole.GrantsIdentity;
            }
        }

        return role;
    }

    static bool TryDescribeMember(
        MetadataReader reader,
        EntityHandle handle,
        out string declaringType,
        out string member)
    {
        declaringType = "";
        member = "";

        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
            {
                var reference = reader.GetMemberReference((MemberReferenceHandle)handle);
                member = reader.GetString(reference.Name);
                declaringType = TypeNameOf(reader, reference.Parent);
                return true;
            }
            case HandleKind.MethodDefinition:
            {
                var definition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                member = reader.GetString(definition.Name);
                declaringType = FullName(reader, definition.GetDeclaringType());
                return true;
            }
            case HandleKind.MethodSpecification:
            {
                var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                return TryDescribeMember(reader, specification.Method, out declaringType, out member);
            }
            default:
                return false;
        }
    }

    static string TypeNameOf(MetadataReader reader, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeReference => Join(
                reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Namespace),
                reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name)),
            HandleKind.TypeDefinition => FullName(reader, (TypeDefinitionHandle)handle),
            _ => "",
        };

    static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        return type.IsNested
            ? $"{FullName(reader, type.GetDeclaringType())}+{name}"
            : Join(reader.GetString(type.Namespace), name);
    }

    static string Join(string @namespace, string name) =>
        string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";

    /// <summary>
    /// Names a site by declaring type and method, with the root namespace
    /// trimmed for readability. Compiler-generated members are attributed to
    /// the method they were lowered from, so an iterator or a lambda does not
    /// read as an unrelated site.
    /// </summary>
    static string SiteName(MetadataReader reader, MethodDefinition method)
    {
        string type = FullName(reader, method.GetDeclaringType());
        string name = reader.GetString(method.Name);

        const string RootNamespace = "ILInspector.Decompiler.";
        if (type.StartsWith(RootNamespace, StringComparison.Ordinal))
            type = type[RootNamespace.Length..];

        return $"{Unmangle(type)}.{Unmangle(name)}";
    }

    /// <summary>
    /// Recovers the authored name a compiler-generated name was lowered from.
    /// Roslyn spells these <c>&lt;Origin&gt;g__Local|1_0</c>,
    /// <c>&lt;Origin&gt;b__2_0</c>, or <c>&lt;Origin&gt;d__7</c>, all of which
    /// carry ordinals that shift when unrelated members are added nearby. The
    /// pin names the authored method so it does not churn on edits that have
    /// nothing to do with trust.
    /// </summary>
    static string Unmangle(string name)
    {
        if (!name.StartsWith('<'))
            return name;

        int end = name.IndexOf('>');
        if (end <= 1)
            return name;

        return name[1..end];
    }
}
