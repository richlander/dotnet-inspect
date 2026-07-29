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

    /// <summary>
    /// The public key token a real compiler stamps on a reference to <paramref name="assembly"/>,
    /// read from the shipped file through the BCL rather than through the product's own reduction,
    /// so these fixtures cannot agree with a wrong implementation of it.
    /// </summary>
    static byte[] TokenOfFrameworkAssembly(string assembly)
        => System.Reflection.AssemblyName
            .GetAssemblyName(Path.Combine(FrameworkDirectory, assembly + ".dll"))
            .GetPublicKeyToken()!;

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
        using var provider = BuildCallerNaming(
            "System.Xml.ReaderWriter",
            "System.Xml",
            "XmlReader",
            TokenOfFrameworkAssembly("System.Xml.ReaderWriter"));
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
        using var provider = BuildCallerNaming(
            "System.Xml.ReaderWriter",
            "System.Xml",
            "XmlWriter",
            TokenOfFrameworkAssembly("System.Xml.ReaderWriter"));
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

    /// <summary>
    /// A forwarder is evidence about <em>one</em> assembly, not about every assembly that happens
    /// to share its simple name. An alias records only a name, because that is all a
    /// <see cref="TypeRef"/> carries, so without a strong-name check a facade signed with one key
    /// would vouch for a caller that bound against a different assembly of the same name — and the
    /// tool would report a call to an unrelated type as a call to the target.
    ///
    /// <para>Fabricating a caller is worse than missing one: a missing row is visibly absent, while
    /// an invented row is indistinguishable from a real finding. Found in adversarial review of
    /// #3419 against a real reproduction, so both directions are pinned here.</para>
    /// </summary>
    [Fact]
    public void PrefilterRejectsAFacadeSpellingFromADifferentlySignedAssembly()
    {
        byte[] evidenceKey = [.. Enumerable.Repeat((byte)0xA1, 16)];
        byte[] impostorKey = [.. Enumerable.Repeat((byte)0xB2, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget", evidenceKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(
            target, Directory.GetFiles(directory, "*.dll"));

        Assert.False(aliases.IsEmpty);

        using (var agreeing = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(evidenceKey)))
        {
            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(agreeing.GetMetadataReader(), target, aliases));
        }

        using var impostor = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(impostorKey));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(impostor.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// The rule the review of <c>cfd71e37</c> corrected. An alias fires only on <em>verified</em>
    /// identity, which is the opposite of the rule for ordinary identity matching and deliberately
    /// so: aliasing is additive, so declining one restores pre-#3419 behavior — a caller that is
    /// not listed — while applying one wrongly invents an edge that never existed.
    ///
    /// <para>Each case here was an admitted fabrication under the earlier
    /// "only a present-and-different token rejects" rule. Two independent reviewers reached three
    /// of them by different routes, which is why the rule was inverted rather than patched.</para>
    /// </summary>
    [Theory]
    // A tokenless reference cannot have bound to strong-named evidence; a real compiler stamps the
    // token. Admitting it let any caller claim any signed facade.
    [InlineData("tokenless reference, signed evidence")]
    // An unsigned facade cannot answer for a reference that carries a token.
    [InlineData("signed reference, unsigned evidence")]
    // Two differently signed assemblies claim the spelling, so neither can be confirmed. The
    // poisoning was previously skipped entirely for a tokenless reference.
    [InlineData("ambiguous spelling")]
    // A retargetable reference declares its identity substitutable, so its token is not the
    // definition's and confirms nothing.
    [InlineData("retargetable reference")]
    public void PrefilterDeclinesAnAliasItCannotVerify(string shape)
    {
        byte[] evidenceKey = [.. Enumerable.Repeat((byte)0xA1, 16)];
        byte[] rivalKey = [.. Enumerable.Repeat((byte)0xB2, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory,
            "Contoso.Facade",
            "Contoso.Definer",
            "Contoso",
            "Widget",
            shape == "signed reference, unsigned evidence" ? null : evidenceKey);

        if (shape == "ambiguous spelling")
        {
            // A second file claiming the same assembly name under a different key. Its own file
            // name has to differ, but the assembly identity inside it is what the walk records.
            WriteForwarder(
                directory,
                "Contoso.Facade",
                "Contoso.Definer",
                "Contoso",
                "Widget",
                rivalKey,
                fileName: "Contoso.Facade.Rival");
        }

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));
        Assert.False(aliases.IsEmpty);

        (byte[]? token, AssemblyFlags flags) = shape switch
        {
            "tokenless reference, signed evidence" => (null, default(AssemblyFlags)),
            "signed reference, unsigned evidence" => (TokenOf(evidenceKey), default),
            "ambiguous spelling" => (null, default),
            "retargetable reference" => (TokenOf(evidenceKey), AssemblyFlags.Retargetable),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget", token, flags);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// The negative control for the rule above: declining unverifiable identity must not collapse
    /// into declining everything. A reference that stores a full public key rather than a token
    /// (<see cref="AssemblyFlags.PublicKey"/>, ECMA-335 II.22.5) is a legal shape naming the very
    /// same assembly, and comparing that 160-byte blob against an 8-byte token would reject every
    /// caller that spells its identity that way. Both reviewers found this one.
    /// </summary>
    [Fact]
    public void PrefilterAcceptsAReferenceThatStoresAFullPublicKeyRatherThanAToken()
    {
        byte[] evidenceKey = [.. Enumerable.Repeat((byte)0xA1, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget", evidenceKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        using var caller = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", evidenceKey, AssemblyFlags.PublicKey);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// The other half of that control: an unsigned reference to unsigned evidence is verified, not
    /// merely unrefuted. A simple name is the whole of both identities, so they agree.
    /// </summary>
    [Fact]
    public void PrefilterAcceptsAnUnsignedReferenceToUnsignedEvidence()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget", null);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// One image, two <c>AssemblyRef</c> rows spelling the same name under different keys. A
    /// <see cref="TypeRef"/> records only the name, so nothing downstream can tell which row it
    /// resolved through — and admitting the spelling because the genuine row verified would let it
    /// vouch for the call made through the impostor beside it.
    ///
    /// <para>Found in review of <c>7181e795</c> against an executed reproduction. Per-image
    /// verification is what made this reachable: the first revision accepted a spelling as soon as
    /// any row verified it, so the fix is that one failing row withdraws the spelling entirely.</para>
    /// </summary>
    [Fact]
    public void PrefilterDeclinesASpellingOneOfWhoseReferencesFailsVerification()
    {
        byte[] evidenceKey = [.. Enumerable.Repeat((byte)0xA1, 16)];
        byte[] impostorKey = [.. Enumerable.Repeat((byte)0xB2, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget", evidenceKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));
        Assert.False(aliases.IsEmpty);

        // The premise: the genuine row alone is admitted, so the rejection below is the second row
        // and not the first failing to verify.
        using (var genuine = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(evidenceKey)))
        {
            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(genuine.GetMetadataReader(), target, aliases));
        }

        using var mixed = BuildCallerNamingThroughTwoReferences(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(impostorKey), TokenOf(evidenceKey));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(mixed.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// An image with two <c>AssemblyRef</c> rows for one assembly name under different keys, whose
    /// only <see cref="TypeRef"/> resolves through the first of them.
    /// </summary>
    static MetadataReaderProvider BuildCallerNamingThroughTwoReferences(
        string assembly,
        string ns,
        string typeName,
        byte[] resolvedThrough,
        byte[] alsoPresent)
        => BuildCallerNamingThroughTwoReferences(
            assembly, ns, typeName, resolvedThrough, alsoPresent, alsoPresentFlags: default);

    /// <summary>
    /// The same, with flags on the second row, so a test can vary how that row declares its
    /// identity while holding the spelling fixed.
    /// </summary>
    static MetadataReaderProvider BuildCallerNamingThroughTwoReferences(
        string assembly,
        string ns,
        string typeName,
        byte[] resolvedThrough,
        byte[] alsoPresent,
        AssemblyFlags alsoPresentFlags)
    {
        var metadata = NewAssembly("Contoso.Caller");
        var used = metadata.AddAssemblyReference(
            metadata.GetOrAddString(assembly),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(resolvedThrough),
            flags: default,
            hashValue: default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(assembly),
            new Version(2, 0, 0, 0),
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(alsoPresent),
            flags: alsoPresentFlags,
            hashValue: default);
        metadata.AddTypeReference(
            used,
            metadata.GetOrAddString(ns),
            metadata.GetOrAddString(typeName));

        var root = new MetadataRootBuilder(metadata);
        var blob = new BlobBuilder();
        root.Serialize(blob, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
    }

    /// <summary>
    /// The other half of the same rule. A row that cannot be checked does not withdraw the
    /// canonical bucket, but it does withdraw <em>its own</em> spelling — because a
    /// <see cref="TypeRef"/> records only a name, so a call site naming this spelling might have
    /// resolved through the uncheckable row rather than the verified one beside it.
    ///
    /// <para>Together with
    /// <see cref="PrefilterKeepsAnAliasWhenAnUncheckableReferenceSharesItsCanonicalName"/> this
    /// pins both directions: uncheckable is not permissive for the spelling it names, and not
    /// destructive for the spellings it merely shares a bucket with.</para>
    /// </summary>
    [Fact]
    public void PrefilterDeclinesASpellingOneOfWhoseReferencesCannotBeChecked()
    {
        byte[] trustedKey = [.. Enumerable.Repeat((byte)0xF6, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget", trustedKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // Premise: one checkable, agreeing row admits the spelling, so the rejection below is the
        // uncheckable row beside it.
        using (var alone = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(trustedKey)))
        {
            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(alone.GetMetadataReader(), target, aliases));
        }

        using var mixed = BuildCallerNamingThroughTwoReferences(
            "Contoso.Facade", "Contoso", "Widget",
            resolvedThrough: TokenOf(trustedKey),
            alsoPresent: TokenOf(trustedKey),
            alsoPresentFlags: AssemblyFlags.Retargetable);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(mixed.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// A reference that cannot be checked must not refute the spelling it names. Only a reference
    /// that positively contradicts the evidence may do that.
    ///
    /// <para>The distinction did not exist before refutation did. Every non-verifying answer used
    /// to mean only "this reference does not vouch for the spelling", which cost nothing, since the
    /// spelling simply stayed out of the verified set. Refutation gave the same answer a second,
    /// much stronger meaning — it now withdraws the canonical bucket for the whole image — and a
    /// retargetable reference, which declares its identity substitutable and therefore says nothing
    /// either way, was swept into it. The cost lands on a <em>different</em> spelling: the genuine,
    /// properly verified sibling in the same bucket loses its callers.</para>
    ///
    /// <para>Reported by reasoning in round-4 review of <c>372be6d1</c> and reproduced here. A
    /// portable library referencing a retargetable <c>mscorlib</c> beside a verified corelib facade
    /// is the shape that occurs in practice.</para>
    /// </summary>
    [Fact]
    public void PrefilterKeepsAnAliasWhenAnUncheckableReferenceSharesItsCanonicalName()
    {
        byte[] trustedKey = [.. Enumerable.Repeat((byte)0xE5, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(directory, "netstandard", "Contoso.Definer", "Contoso", "Widget", trustedKey);
        WriteForwarder(directory, "mscorlib", "Contoso.Definer", "Contoso", "Widget", trustedKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // The TypeRef names the verified spelling. The retargetable sibling canonicalizes into the
        // same bucket but asserts no identity, so it must neither vouch nor veto.
        using var mixed = BuildCallerNamingThroughTwoAssemblies(
            "netstandard", TokenOf(trustedKey), "mscorlib", TokenOf(trustedKey), "Contoso", "Widget",
            otherFlags: AssemblyFlags.Retargetable);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(mixed.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// Two forwarder spellings that canonicalize to one name. <see cref="TypeRef"/> collapses
    /// <c>mscorlib</c>, <c>netstandard</c>, <c>System.Runtime</c> and friends onto a single
    /// canonical corelib name, and <see cref="ForwardedTypeAliases.Includes"/> asks about the
    /// canonical name — so a spelling that failed verification would be readmitted through a
    /// verified sibling in the same bucket unless the bucket goes with it.
    ///
    /// <para>Reported by reasoning in review of <c>7181e795</c> and confirmed here. The removal is
    /// deliberately limited to spellings that supplied forwarder evidence: poisoning the bucket for
    /// <em>any</em> unverified reference that canonicalizes into it would break ordinary images,
    /// which routinely reference <c>System.Runtime</c> (no forwarder, hence never verifiable)
    /// alongside the facade that does forward. The residual — a <c>TypeRef</c> spelled through a
    /// corelib facade this image never referenced — is <see cref="TypeRef"/> canonicalization
    /// imprecision that predates aliasing and is tracked as #3485.</para>
    /// </summary>
    [Fact]
    public void PrefilterDeclinesACanonicalAliasARefutedSpellingAlsoMapsTo()
    {
        byte[] trustedKey = [.. Enumerable.Repeat((byte)0xC3, 16)];
        byte[] impostorKey = [.. Enumerable.Repeat((byte)0xD4, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(directory, "netstandard", "Contoso.Definer", "Contoso", "Widget", trustedKey);
        WriteForwarder(directory, "mscorlib", "Contoso.Definer", "Contoso", "Widget", trustedKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));
        Assert.True(aliases.IncludesRawSpelling("netstandard"));
        Assert.True(aliases.IncludesRawSpelling("mscorlib"));

        // Premise: verifying both spellings admits the type, so the rejection below is the refuted
        // sibling and not the alias set being empty to begin with.
        using (var both = BuildCallerNamingThroughTwoAssemblies(
            "netstandard", TokenOf(trustedKey), "mscorlib", TokenOf(trustedKey), "Contoso", "Widget"))
        {
            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(both.GetMetadataReader(), target, aliases));
        }

        // The TypeRef names the verified spelling; the refuted one merely sits beside it. Both
        // decode to the same canonical assembly, so admitting it would admit the impostor's.
        using var mixed = BuildCallerNamingThroughTwoAssemblies(
            "netstandard", TokenOf(trustedKey), "mscorlib", TokenOf(impostorKey), "Contoso", "Widget");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(mixed.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// An image referencing two differently named assemblies, whose only <see cref="TypeRef"/>
    /// resolves through the first.
    /// </summary>
    static MetadataReaderProvider BuildCallerNamingThroughTwoAssemblies(
        string namingAssembly,
        byte[] namingToken,
        string otherAssembly,
        byte[] otherToken,
        string ns,
        string typeName)
        => BuildCallerNamingThroughTwoAssemblies(
            namingAssembly, namingToken, otherAssembly, otherToken, ns, typeName, otherFlags: default);

    /// <summary>
    /// The same, with flags on the second reference, so a test can vary how that reference declares
    /// its identity while holding everything else fixed.
    /// </summary>
    static MetadataReaderProvider BuildCallerNamingThroughTwoAssemblies(
        string namingAssembly,
        byte[] namingToken,
        string otherAssembly,
        byte[] otherToken,
        string ns,
        string typeName,
        AssemblyFlags otherFlags)
    {
        var metadata = NewAssembly("Contoso.Caller");
        var naming = metadata.AddAssemblyReference(
            metadata.GetOrAddString(namingAssembly),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(namingToken),
            flags: default,
            hashValue: default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(otherAssembly),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(otherToken),
            flags: otherFlags,
            hashValue: default);
        metadata.AddTypeReference(
            naming,
            metadata.GetOrAddString(ns),
            metadata.GetOrAddString(typeName));

        var root = new MetadataRootBuilder(metadata);
        var blob = new BlobBuilder();
        root.Serialize(blob, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
    }

    /// <summary>
    /// The public key token for a public key, restated from ECMA-335 II.6.3 rather than shared with
    /// the product code, so this is an independent oracle rather than the same computation twice.
    /// </summary>
    static byte[] TokenOf(byte[] publicKey)
    {
        byte[] hash = System.Security.Cryptography.SHA1.HashData(publicKey);
        return [.. hash.Skip(hash.Length - 8).Reverse()];
    }

    static string NewTempDirectory()    {
        string directory = Path.Combine(Path.GetTempPath(), "fwd-alias-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Writes an assembly that forwards one type to another assembly.</summary>
    static void WriteForwarder(string directory, string name, string target, string ns, string typeName)
        => WriteForwarder(directory, name, target, ns, typeName, publicKey: null);

    /// <summary>
    /// Writes a forwarder, optionally strong-named, so a test can vary the identity behind a
    /// spelling while holding the spelling itself fixed.
    /// </summary>
    static void WriteForwarder(
        string directory,
        string name,
        string target,
        string ns,
        string typeName,
        byte[]? publicKey)
        => WriteForwarder(directory, name, target, ns, typeName, publicKey, fileName: name);

    /// <summary>
    /// The same, with the file name held apart from the assembly name, so a test can place two
    /// files that claim one assembly identity in a single directory.
    /// </summary>
    static void WriteForwarder(
        string directory,
        string name,
        string target,
        string ns,
        string typeName,
        byte[]? publicKey,
        string fileName)
    {
        var metadata = NewAssembly(name, publicKey);
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

        File.WriteAllBytes(Path.Combine(directory, fileName + ".dll"), SerializePE(metadata));
    }

    /// <summary>Metadata for an image whose only TypeRef names one type in one assembly.</summary>
    static MetadataReaderProvider BuildCallerNaming(string assembly, string ns, string typeName)
        => BuildCallerNaming(assembly, ns, typeName, publicKeyOrToken: null);

    /// <summary>
    /// The same, with an explicit public key token on the assembly reference — the identity a real
    /// compiler emits when the referenced assembly is strong-named.
    /// </summary>
    static MetadataReaderProvider BuildCallerNaming(
        string assembly,
        string ns,
        string typeName,
        byte[]? publicKeyOrToken)
        => BuildCallerNaming(assembly, ns, typeName, publicKeyOrToken, flags: default);

    /// <summary>
    /// The same, with explicit <see cref="AssemblyFlags"/> on the reference, so a test can build the
    /// two shapes that are not a plain token: a reference that stores a full public key
    /// (<see cref="AssemblyFlags.PublicKey"/>) and one that declares itself retargetable.
    /// </summary>
    static MetadataReaderProvider BuildCallerNaming(
        string assembly,
        string ns,
        string typeName,
        byte[]? publicKeyOrToken,
        AssemblyFlags flags)
    {
        var metadata = NewAssembly("Contoso.Caller");
        var reference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(assembly),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: publicKeyOrToken is null
                ? default
                : metadata.GetOrAddBlob(publicKeyOrToken),
            flags: flags,
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

    static MetadataBuilder NewAssembly(string name) => NewAssembly(name, publicKey: null);

    static MetadataBuilder NewAssembly(string name, byte[]? publicKey)
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
            publicKey: publicKey is null ? default : metadata.GetOrAddBlob(publicKey),
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
