using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Type forwarding through facades outside <see cref="TypeRef"/>'s core-library alias set (#3419).
///
/// The canary is a real framework chain rather than a synthetic one, because the claim is about a
/// shape the .NET shared framework actually ships: <c>System.Private.Xml</c> defines
/// <c>System.Xml.XmlReader</c>, <c>System.Xml.ReaderWriter</c> forwards to it, and
/// <c>System.Xml</c> forwards to <c>System.Xml.ReaderWriter</c>. A compiler binding against the
/// reference pack emits a <c>TypeRef</c> naming the facade, never the definer.
/// </summary>
public class ForwardedTypeAliasesTests
{
    static string FrameworkDirectory => Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    static IEnumerable<string> FrameworkAssemblies => Directory.GetFiles(FrameworkDirectory, "*.dll");

    static TypeRef XmlReader => TypeRef.Definition("System.Private.Xml", "System.Xml", "XmlReader");

    [Fact]
    public void ForTarget_FollowsAMultiHopFrameworkFacadeChain()
    {
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);

        // One hop: the facade forwards straight to the definer.
        Assert.True(aliases.Includes("System.Xml.ReaderWriter"));

        // Two hops: System.Xml forwards to System.Xml.ReaderWriter, which forwards to the definer.
        // A single-hop implementation passes the assertion above and fails this one.
        Assert.True(aliases.Includes("System.Xml"));
    }

    /// <summary>
    /// The premise for every recovery test below: without aliases the facade spelling and the
    /// definition compare unequal, which is the defect. If this ever starts passing, the tests
    /// that assert recovery stop proving anything.
    /// </summary>
    [Fact]
    public void WithoutAliases_AFacadeSpellingDoesNotDenoteTheDefinition()
    {
        var facadeSpelling = TypeRef.Definition("System.Xml.ReaderWriter", "System.Xml", "XmlReader");

        Assert.False(facadeSpelling.Equals(XmlReader));
        Assert.False(ForwardedTypeAliases.DenotesSameType(facadeSpelling, XmlReader, aliases: null));
        Assert.False(ForwardedTypeAliases.DenotesSameType(
            facadeSpelling, XmlReader, ForwardedTypeAliases.None));
    }

    [Fact]
    public void WithAliases_AFacadeSpellingDenotesTheDefinition()
    {
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);
        var facadeSpelling = TypeRef.Definition("System.Xml.ReaderWriter", "System.Xml", "XmlReader");

        Assert.True(ForwardedTypeAliases.DenotesSameType(facadeSpelling, XmlReader, aliases));
    }

    /// <summary>
    /// An alias admits only the assembly spelling. A different type reached through the same facade
    /// must not match, or the alias set would turn a facade into a wildcard.
    /// </summary>
    [Fact]
    public void AnAliasDoesNotMatchADifferentTypeFromTheSameFacade()
    {
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);
        var otherType = TypeRef.Definition("System.Xml.ReaderWriter", "System.Xml", "XmlWriter");
        var otherNamespace = TypeRef.Definition("System.Xml.ReaderWriter", "Contoso", "XmlReader");

        Assert.False(ForwardedTypeAliases.DenotesSameType(otherType, XmlReader, aliases));
        Assert.False(ForwardedTypeAliases.DenotesSameType(otherNamespace, XmlReader, aliases));
    }

    [Fact]
    public void ForTarget_RecordsNothingForATypeNobodyForwards()
    {
        var target = TypeRef.Definition("Contoso.Lib", "Contoso", "NeverForwarded");

        var aliases = ForwardedTypeAliases.ForTarget(target, FrameworkAssemblies);

        Assert.True(aliases.IsEmpty);
        Assert.Same(ForwardedTypeAliases.None, aliases);
    }

    /// <summary>
    /// A forwarder chain that loops must terminate. Two facades forwarding this type to each other
    /// reach no definition, so neither is an alias.
    ///
    /// <para>This pins the answer, not the mechanism: removing the <c>visited</c> guard leaves this
    /// test green, because <see cref="ForwardedTypeAliases"/>'s hop bound already terminates the
    /// walk and already returns "no alias". The guard is the cheaper of two correct answers, and no
    /// result-based test can distinguish them. Named here so the guard is not later read as
    /// something this test defends.</para>
    /// </summary>
    [Fact]
    public void ForTarget_TerminatesOnAForwarderCycle()
    {
        string directory = NewTempDirectory();
        try
        {
            WriteForwarder(directory, "Loop.A", "Loop.B", "Contoso", "Widget");
            WriteForwarder(directory, "Loop.B", "Loop.A", "Contoso", "Widget");

            var aliases = ForwardedTypeAliases.ForTarget(
                TypeRef.Definition("Contoso.Definer", "Contoso", "Widget"),
                Directory.GetFiles(directory, "*.dll"));

            Assert.True(aliases.IsEmpty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The prefilter and the matcher must stay equally permissive, so this asserts both directions
    /// on one image: ruled out without aliases, kept with them. The image is synthetic because the
    /// row a compiler emits for this case is exactly one <c>TypeRef</c> naming the facade.
    /// </summary>
    [Fact]
    public void PrefilterKeepsAnImageThatNamesTheTargetOnlyThroughAFacade()
    {
        using var provider = BuildCallerNaming("System.Xml.ReaderWriter", "System.Xml", "XmlReader");
        var reader = provider.GetMetadataReader();
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);

        // Without aliases the prefilter rules the image out -- this is the behavior that silently
        // defeated a matcher-only fix.
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(reader, XmlReader));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(reader, XmlReader, aliases));
    }

    /// <summary>
    /// The negative control for the test above: aliases must not make the prefilter keep an image
    /// that names an unrelated type, or "keeps the right image" would be satisfied by a filter that
    /// keeps everything.
    /// </summary>
    [Fact]
    public void PrefilterStillRulesOutAnImageThatNamesADifferentType()
    {
        using var provider = BuildCallerNaming("System.Xml.ReaderWriter", "System.Xml", "XmlWriter");
        var reader = provider.GetMetadataReader();
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(reader, XmlReader, aliases));
    }

    [Fact]
    public void MatcherRecoversACallerThatNamesTheTargetThroughAFacade()
    {
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);
        var pattern = MemberPattern.Method(XmlReader, "Read");
        var facadeCallee = new MemberRef(
            TypeRef.Definition("System.Xml.ReaderWriter", "System.Xml", "XmlReader"),
            "Read",
            [],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method);

        Assert.False(pattern.MatchesCrossAssembly(facadeCallee));
        Assert.True(pattern.MatchesCrossAssembly(facadeCallee, aliases));
    }

    [Fact]
    public void MatcherStillDiscriminatesByMemberName()
    {
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);
        var pattern = MemberPattern.Method(XmlReader, "Read");
        var otherMember = new MemberRef(
            TypeRef.Definition("System.Xml.ReaderWriter", "System.Xml", "XmlReader"),
            "Close",
            [],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

        Assert.False(pattern.MatchesCrossAssembly(otherMember, aliases));
    }

    /// <summary>
    /// The soundness property the demand-driven walk turns on. Seeding only <c>System.Xml</c> — the
    /// one spelling a caller wrote — must still reach the definer through
    /// <c>System.Xml.ReaderWriter</c>, which is a seed of nothing and would never be opened by a
    /// walk that only probed its seeds.
    /// </summary>
    [Fact]
    public void ForTarget_WithSeeds_FollowsAChainThroughAnUnseededIntermediate()
    {
        var seeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System.Xml" };

        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies, seeds);

        Assert.True(aliases.Includes("System.Xml"));
    }

    /// <summary>
    /// Seeding is what keeps the walk off the hot path, so it must actually restrict: a spelling no
    /// caller could name is not probed and yields no alias.
    /// </summary>
    [Fact]
    public void ForTarget_WithSeeds_DoesNotProbeSpellingsNoCallerCanName()
    {
        var seeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Contoso.NotHere" };

        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies, seeds);

        Assert.True(aliases.IsEmpty);
    }

    /// <summary>
    /// Raw spellings stay uncanonicalized, and that distinction is load-bearing rather than
    /// cosmetic. <c>netstandard</c> carries a forwarder for this type and canonicalizes to
    /// <c>corelib</c>; <c>System.Runtime</c> canonicalizes to <c>corelib</c> too but carries no such
    /// forwarder. An assembly-level filter keyed on the canonical name could not tell them apart and
    /// would select every assembly that references any core-library facade — which is all of them.
    /// </summary>
    [Fact]
    public void RawSpellingsDistinguishTheFacadeThatActuallyForwards()
    {
        var aliases = ForwardedTypeAliases.ForTarget(XmlReader, FrameworkAssemblies);

        Assert.True(aliases.Includes("corelib"));
        Assert.True(aliases.IncludesRawSpelling("netstandard"));

        // The whole point: same canonical name, no forwarder, so it is not a raw spelling.
        Assert.False(aliases.IncludesRawSpelling("System.Runtime"));
        Assert.False(aliases.IncludesRawSpelling("System.Private.CoreLib"));
    }

    static string NewTempDirectory()    {
        string directory = Path.Combine(Path.GetTempPath(), "fwd-alias-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Writes an assembly that forwards one type to another assembly.</summary>
    static void WriteForwarder(string directory, string name, string target, string ns, string typeName)
    {
        var metadata = NewAssembly(name);
        var targetReference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(target),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        metadata.AddExportedType(
            // tdForwarder (ECMA-335 II.23.1.15); not named in System.Reflection.TypeAttributes.
            (TypeAttributes)0x00200000,
            metadata.GetOrAddString(ns),
            metadata.GetOrAddString(typeName),
            targetReference,
            typeDefinitionId: 0);

        File.WriteAllBytes(Path.Combine(directory, name + ".dll"), SerializePE(metadata));
    }

    /// <summary>Metadata for an image whose only TypeRef names one type in one assembly.</summary>
    static MetadataReaderProvider BuildCallerNaming(string assembly, string ns, string typeName)
    {
        var metadata = NewAssembly("Contoso.Caller");
        var reference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(assembly),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        metadata.AddTypeReference(
            reference,
            metadata.GetOrAddString(ns),
            metadata.GetOrAddString(typeName));

        var root = new MetadataRootBuilder(metadata);
        var blob = new BlobBuilder();
        root.Serialize(blob, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
    }

    static MetadataBuilder NewAssembly(string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: System.Reflection.AssemblyHashAlgorithm.Sha1);

        // Row 1 is always <Module>, exactly as a compiler emits.
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] SerializePE(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
