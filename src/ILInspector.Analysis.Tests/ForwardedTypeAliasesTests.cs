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
    /// Culture is part of ECMA identity and, unlike version, is checkable: a reference records the
    /// culture it bound against, and a satellite assembly is not the facade. Reported with an
    /// executed reproduction in review of <c>372be6d1</c>, where neutral evidence vouched for a
    /// <c>Culture=fr-FR</c> reference.
    /// </summary>
    [Fact]
    public void PrefilterRejectsAReferenceToADifferentCultureOfTheSameAssembly()
    {
        byte[] key = [.. Enumerable.Repeat((byte)0x17, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget", key);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // Premise: the neutral reference is admitted, so the rejection below is the culture alone.
        using (var neutral = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(key)))
        {
            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(neutral.GetMetadataReader(), target, aliases));
        }

        using var satellite = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(key),
            flags: default, culture: "fr-FR", version: new Version(1, 0, 0, 0));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(satellite.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// The CLR matches assembly names case-insensitively
    /// (<c>AssemblyName.ReferenceMatchesDefinition</c> matches <c>contoso.facade</c> to
    /// <c>Contoso.Facade</c>), and identity verification here already did. The alias sets did not,
    /// so a reference differing only in case verified and was then refused by the lookup — a
    /// genuine caller dropped by the two halves of one question disagreeing. Found in review of
    /// <c>984454a3</c>.
    /// </summary>
    [Fact]
    public void PrefilterKeepsAReferenceThatDiffersFromTheEvidenceOnlyInCase()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        using var lowercased = BuildCallerNaming("contoso.facade", "Contoso", "Widget");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(lowercased.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// An unsigned assembly's token proves nothing — anyone can produce the name — so version is
    /// the only discriminator left, and "could roll forward" is not evidence that it did. An
    /// unsigned v3 forwarder vouching for a caller that references v2 fabricates: the v2 the caller
    /// actually bound against may define the type itself. Executed in review of <c>984454a3</c>.
    ///
    /// <para>Strong-named evidence is the opposite case and is deliberately more permissive — see
    /// <see cref="PrefilterAcceptsAnEarlierReferenceToStrongNamedEvidence"/>. Measured over 16,294
    /// references resolving to a definition on disk, 712 legitimate roll-forwards are strong-named
    /// and exactly 1 is unsigned, so this costs one miss and closes the fabrication.</para>
    /// </summary>
    [Fact]
    public void PrefilterRejectsAnEarlierReferenceToUnsignedEvidence()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // Premise: the evidence is unsigned and written at 1.0.0.0, and an exact-version reference
        // is admitted — so the rejection below is the version and nothing else.
        using (var exact = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget"))
        {
            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(exact.GetMetadataReader(), target, aliases));
        }

        using var earlier = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", publicKeyOrToken: null,
            flags: default, culture: null, version: new Version(0, 5, 0, 0));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(earlier.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// loader rolls forward, never backward. Without this, an unrelated later assembly sharing the
    /// spelling fabricated a caller — a v1 facade forwarding <c>Widget</c> vouched for a caller
    /// built against a v2 assembly of the same name that defines <c>Widget</c> itself, so a call to
    /// the v2 type was reported as a call to the target. Executed in review of <c>b18e5009</c>.
    ///
    /// <para>Version is the only discriminator left when both assemblies are unsigned, which is the
    /// common shape outside the framework.</para>
    /// </summary>
    [Fact]
    public void PrefilterRejectsAReferenceToALaterVersionThanTheEvidence()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // Premise: the evidence is unsigned and neutral, so name, token and culture all agree and
        // the version is the only thing that can reject.
        Assert.True(aliases.IncludesRawSpelling("Contoso.Facade"));

        using var later = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", publicKeyOrToken: null,
            flags: default, culture: null, version: new Version(2, 0, 0, 0));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(later.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// Version is compared by roll-forward direction, not equality, and this pins the permissive
    /// half so a later "require exact identity" change cannot make it silently.
    ///
    /// <para>A reference's version is not the definition's: binding rolls forward, and reference
    /// assemblies routinely record <c>0.0.0.0</c>. Measured over the shared framework, ASP.NET Core
    /// and this repository's build output — 2,267 assemblies, 16,294 references resolving to a
    /// definition on disk — <b>713</b> named a version below the definition and <b>0</b> named one
    /// above. Equality would have declined all 713, including <c>mscorlib</c>, the canonical
    /// forwarding facade, whose reference to <c>System.Private.CoreLib</c> reads <c>0.0.0.0</c>
    /// against a definition of <c>9.0.0.0</c> — the very shape #3419 exists to serve.</para>
    ///
    /// <para>The evidence here is strong-named, which is what earns the roll-forward: the token
    /// already proves the publisher. Unsigned evidence gets the strict rule instead — see
    /// <see cref="PrefilterRejectsAnEarlierReferenceToUnsignedEvidence"/>. Of the 713 measured
    /// roll-forwards, <b>712</b> are strong-named and <b>1</b> is not.</para>
    /// </summary>
    [Fact]
    public void PrefilterAcceptsAReferenceToAnEarlierVersionThanTheEvidence()
    {
        byte[] key = [.. Enumerable.Repeat((byte)0x28, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget", key);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // The evidence assembly is written at 1.0.0.0; this is the mscorlib shape.
        using var rollForward = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(key),
            flags: default, culture: null, version: new Version(0, 0, 0, 0));

        // Premise: the two versions genuinely differ, so admitting the reference is a decision and
        // not an artifact of the fixture happening to agree.
        using (var evidence = new PEReader(File.OpenRead(
            Path.Combine(directory, "Contoso.Facade.dll"))))
        {
            var evidenceReader = evidence.GetMetadataReader();
            Assert.Equal(
                new Version(1, 0, 0, 0),
                evidenceReader.GetAssemblyDefinition().Version);
        }

        var callerReader = rollForward.GetMetadataReader();
        Assert.Equal(
            new Version(0, 0, 0, 0),
            callerReader.GetAssemblyReference(callerReader.AssemblyReferences.Single()).Version);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(rollForward.GetMetadataReader(), target, aliases));
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
    // A retargetable reference declares its identity substitutable, so its token is not the
    // definition's and confirms nothing.
    [InlineData("retargetable reference")]
    public void PrefilterDeclinesAnAliasItCannotVerify(string shape)
    {
        byte[] evidenceKey = [.. Enumerable.Repeat((byte)0xA1, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory,
            "Contoso.Facade",
            "Contoso.Definer",
            "Contoso",
            "Widget",
            shape == "signed reference, unsigned evidence" ? null : evidenceKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));
        Assert.False(aliases.IsEmpty);

        (byte[]? token, AssemblyFlags flags) = shape switch
        {
            "tokenless reference, signed evidence" => (null, default(AssemblyFlags)),
            "signed reference, unsigned evidence" => (TokenOf(evidenceKey), default),
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
    /// Two files claiming one assembly identity that forward the type to <em>different</em>
    /// definers cannot both be right, and neither can be picked. The chain map takes the first
    /// writer, so without this the second file's callers are aliased to the first file's definer —
    /// a call to one type reported as a call to another.
    ///
    /// <para>Identity alone does not catch this: the two files agree on name, key and culture, so
    /// every identity check passes and only the forwarding target disagrees. Raised in review of
    /// <c>984454a3</c> against the version-max rule, but it does not need differing versions — this
    /// fixture holds version fixed to show the disagreement itself is what makes the spelling
    /// unusable.</para>
    /// </summary>
    [Fact]
    public void PrefilterDeclinesASpellingWhoseTwoFilesForwardToDifferentDefiners()
    {
        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "first");
        WriteForwarder(
            directory, "Contoso.Facade", "Other.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "second");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// The <em>facade to definer</em> edge carries identity too. Six review rounds hardened the
    /// caller-to-facade edge while this one was a bare name comparison, so a facade whose
    /// <c>AssemblyRef</c> names a definer that does not exist under that identity still vouched
    /// for its callers — reporting a call to one assembly's type as a call to another's. Raised
    /// against <c>a749cd4d</c>.
    ///
    /// <para>An unverified definer edge is refused outright rather than merely withdrawn, because
    /// the polarity is inverted from the caller side: an unusable caller row costs an alias, while
    /// an unconfirmed forwarding relationship <em>asserts</em> one that was never established.</para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrefilterVerifiesTheEdgeFromTheFacadeToItsDefiner(bool edgeIsGenuine)
    {
        byte[] facadeKey = [.. Enumerable.Repeat((byte)0xA1, 16)];
        byte[] definerKey = [.. Enumerable.Repeat((byte)0xC3, 16)];
        byte[] rivalKey = [.. Enumerable.Repeat((byte)0xB2, 16)];

        string directory = NewTempDirectory();

        // Written first so the forwarder does not supply its own unsigned definer.
        File.WriteAllBytes(
            Path.Combine(directory, "Contoso.Definer.dll"),
            SerializePE(NewAssembly("Contoso.Definer", definerKey)));

        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: facadeKey, fileName: "Contoso.Facade",
            version: new Version(1, 0, 0, 0),
            targetPublicKeyToken: TokenOf(edgeIsGenuine ? definerKey : rivalKey));

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        using var caller = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(facadeKey));

        // The caller side is identical in both cases and verifies in both, so this pins the
        // definer edge alone: only the token the facade records for its definer differs.
        Assert.Equal(
            edgeIsGenuine
                ? CallerScopeTypeFilter.TypeReferenceState.Names
                : CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// An ambiguous spelling must be refused wherever it appears in a chain, not only as the hop a
    /// caller names. Marking the spelling ambiguous while leaving its entry in the chain map let an
    /// ambiguous <em>intermediate</em> still route <c>Outer → Middle → Definer</c>, and which of
    /// the two rival files won depended on directory enumeration order. Raised against
    /// <c>a749cd4d</c>; this asserts both orders to gate the order-dependence directly.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrefilterRoutesNoChainThroughAnAmbiguousIntermediateHop(bool rivalFirst)
    {
        byte[] middleKey = [.. Enumerable.Repeat((byte)0xA1, 16)];
        byte[] rivalKey = [.. Enumerable.Repeat((byte)0xB2, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Outer", "Contoso.Middle", "Contoso", "Widget",
            publicKey: null, fileName: "Contoso.Outer",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: TokenOf(middleKey));
        WriteForwarder(directory, "Contoso.Outermost", "Contoso.Outer", "Contoso", "Widget");

        var keys = rivalFirst ? new[] { rivalKey, middleKey } : [middleKey, rivalKey];
        for (int i = 0; i < keys.Length; i++)
        {
            WriteForwarder(
                directory, "Contoso.Middle", "Contoso.Definer", "Contoso", "Widget",
                publicKey: keys[i], fileName: "Contoso.Middle." + i);
        }

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // The control: the same three-hop chain with one Middle file. Without it, "no alias" would
        // be satisfied by a chain of this depth simply not resolving.
        string unambiguous = NewTempDirectory();
        WriteForwarder(unambiguous, "Contoso.Outermost", "Contoso.Outer", "Contoso", "Widget");
        WriteForwarder(
            unambiguous, "Contoso.Outer", "Contoso.Middle", "Contoso", "Widget",
            publicKey: null, fileName: "Contoso.Outer",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: TokenOf(middleKey));
        WriteForwarder(
            unambiguous, "Contoso.Middle", "Contoso.Definer", "Contoso", "Widget",
            publicKey: middleKey);

        var control = ForwardedTypeAliases.ForTarget(
            target, Directory.GetFiles(unambiguous, "*.dll"));

        using var controlCaller = BuildCallerNaming("Contoso.Outermost", "Contoso", "Widget");
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(controlCaller.GetMetadataReader(), target, control));

        using var throughMiddle = BuildCallerNaming("Contoso.Middle", "Contoso", "Widget");
        using var throughOuter = BuildCallerNaming("Contoso.Outer", "Contoso", "Widget");
        using var throughOutermost = BuildCallerNaming("Contoso.Outermost", "Contoso", "Widget");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(throughMiddle.GetMetadataReader(), target, aliases));

        // The hop the caller names is not ambiguous; the hop it forwards through is. Refusing the
        // one and admitting the other is what fabricated the edge.
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(throughOuter.GetMetadataReader(), target, aliases));

        // And at a remove, so the claim covers a chain rather than only the hop adjacent to the
        // ambiguity.
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(throughOutermost.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// Evidence is sought by the assembly identity a file <em>claims</em>, not by what it is called
    /// on disk. Seeding the walk by file name meant a rival file claiming the same identity under
    /// another name was never opened, so the ambiguity defence never fired and the seeded walk
    /// returned an alias the unseeded walk correctly refused. Raised against <c>a749cd4d</c>.
    /// </summary>
    [Fact]
    public void SeededAndUnseededWalksAgreeWhenARivalClaimsTheNameUnderAnotherFileName()
    {
        byte[] evidenceKey = [.. Enumerable.Repeat((byte)0xA1, 16)];
        byte[] rivalKey = [.. Enumerable.Repeat((byte)0xB2, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: evidenceKey, fileName: "Contoso.Facade");
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: rivalKey, fileName: "unrelated.file.name");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        string[] evidence = Directory.GetFiles(directory, "*.dll");

        var unseeded = ForwardedTypeAliases.ForTarget(target, evidence);

        // The seed names the assembly the caller binds against. Only the file whose *name* matches
        // it was previously opened, so the rival beside it went unseen.
        var seeded = ForwardedTypeAliases.ForTarget(
            target, evidence, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Contoso.Facade" });

        using var caller = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget", TokenOf(evidenceKey));

        var unseededVerdict = CallerScopeTypeFilter.Classify(
            caller.GetMetadataReader(), target, unseeded);
        var seededVerdict = CallerScopeTypeFilter.Classify(
            caller.GetMetadataReader(), target, seeded);

        Assert.Equal(CallerScopeTypeFilter.TypeReferenceState.DoesNotName, unseededVerdict);
        Assert.Equal(unseededVerdict, seededVerdict);
    }

    /// <summary>
    /// Unsigned evidence observed at several versions keeps the callers of each. Collapsing the
    /// observed version to the highest one meant adding valid newer evidence <em>removed</em> a
    /// genuine caller of the older, because an unsigned identity has to match exactly — there is no
    /// strong name to make roll-forward provable. Raised against <c>a749cd4d</c>.
    ///
    /// <para>Measured on the shared framework plus this repository's output — 2,267 assemblies and
    /// 16,294 references — roll-forward is real (713 references) but unsigned roll-forward is
    /// vanishingly rare (1), so requiring equality for unsigned identity costs almost nothing while
    /// admitting it would let any version of an unsigned assembly answer for any other.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PrefilterKeepsCallersOfEveryVersionUnsignedEvidenceWasObservedAt(int callerMajor)
    {
        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "v1",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "v2",
            version: new Version(2, 0, 0, 0), targetPublicKeyToken: null);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        using var caller = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget",
            publicKeyOrToken: null, flags: default,
            culture: null, version: new Version(callerMajor, 0, 0, 0));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// Aliasing applies to the declaring type and stops there. A parameter type the caller spells
    /// through a facade is compared by plain <see cref="TypeRef"/> equality, so the overload does
    /// not match and the caller is not listed.
    ///
    /// <para>This pins a documented boundary rather than a desired behavior. It is a conservative
    /// miss — it can lose an edge and can never invent one — and closing it needs an alias set per
    /// parameter type, because <c>ForTarget</c> builds a forwarding map for one named type.
    /// Raised in review of <c>a749cd4d</c> and tracked as #3513. The positive half is the
    /// sensitivity control: the identical call with the parameter spelled directly does match, so
    /// this cannot pass by the whole comparison being broken.</para>
    /// </summary>
    [Fact]
    public void AliasingAppliesToTheDeclaringTypeAndNotToParameterTypes()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var widget = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(widget, Directory.GetFiles(directory, "*.dll"));
        Assert.True(aliases.Includes("Contoso.Facade"));

        var pattern = MemberPattern.Method(widget, "Ping", [widget]);
        var facadeSpelling = TypeRef.Definition("Contoso.Facade", "Contoso", "Widget");
        var returnType = TypeRef.CoreLib("System", "Void");

        // Declaring type through the facade, parameter type spelled directly: matched.
        Assert.True(pattern.MatchesCrossAssembly(
            new MemberRef(facadeSpelling, "Ping", [widget], returnType, MemberKind.Method), aliases));

        // The same call with the parameter also spelled through the facade: not matched.
        Assert.False(pattern.MatchesCrossAssembly(
            new MemberRef(facadeSpelling, "Ping", [facadeSpelling], returnType, MemberKind.Method),
            aliases));
    }


    /// <summary>
    /// The negative control for the unsigned multi-version rule: observing two versions widens the
    /// accepted set to exactly those two, not to every version.
    /// </summary>
    [Fact]
    public void PrefilterDeclinesAVersionUnsignedEvidenceWasNeverObservedAt()
    {
        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "v1",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "v3",
            version: new Version(3, 0, 0, 0), targetPublicKeyToken: null);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        using var caller = BuildCallerNaming(
            "Contoso.Facade", "Contoso", "Widget",
            publicKeyOrToken: null, flags: default,
            culture: null, version: new Version(2, 0, 0, 0));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }


    /// its canonical bucket. Canonicalization collapses the five corelib facade spellings onto one
    /// name, so withdrawing a spelling from the verified set does not withdraw it from the matcher
    /// — which compares canonicalized names — and an unused verified sibling silently readmitted
    /// it. Executed in review of <c>b18e5009</c>.
    ///
    /// <para>This is the mirror of
    /// <see cref="PrefilterKeepsAnAliasWhenAnUncheckableReferenceSharesItsCanonicalName"/>, and the
    /// two together are why <see cref="TypeRef.RawAssembly"/> exists: the cases differ only in
    /// which spelling the <c>TypeRef</c> went through, and after canonicalization that difference
    /// is gone. Withdrawing the bucket would satisfy this test and break that one.</para>
    /// </summary>
    [Fact]
    public void PrefilterRefusesASpellingItWithdrewEvenWhenAVerifiedSiblingSharesItsBucket()
    {
        byte[] trustedKey = [.. Enumerable.Repeat((byte)0xE5, 16)];

        string directory = NewTempDirectory();
        WriteForwarder(directory, "netstandard", "Contoso.Definer", "Contoso", "Widget", trustedKey);
        WriteForwarder(directory, "mscorlib", "Contoso.Definer", "Contoso", "Widget", trustedKey);

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // Premise: both spellings supply evidence, so the refusal below is the retargetable flag
        // and not a missing alias.
        Assert.True(aliases.IncludesRawSpelling("mscorlib"));
        Assert.True(aliases.IncludesRawSpelling("netstandard"));

        // The TypeRef goes through the retargetable — hence unverifiable — mscorlib. The verified
        // netstandard sibling is referenced but entirely unused by the TypeRef.
        using var throughRetargetable = BuildCallerNamingThroughTwoAssemblies(
            "mscorlib", TokenOf(trustedKey), "netstandard", TokenOf(trustedKey), "Contoso", "Widget",
            otherFlags: default, namingFlags: AssemblyFlags.Retargetable);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(throughRetargetable.GetMetadataReader(), target, aliases));
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
        => BuildCallerNamingThroughTwoAssemblies(
            namingAssembly, namingToken, otherAssembly, otherToken, ns, typeName, otherFlags,
            namingFlags: default);

    /// <summary>
    /// The same, with flags on the <em>naming</em> reference too, so a test can make the reference
    /// the <c>TypeRef</c> actually goes through the unverifiable one.
    /// </summary>
    static MetadataReaderProvider BuildCallerNamingThroughTwoAssemblies(
        string namingAssembly,
        byte[] namingToken,
        string otherAssembly,
        byte[] otherToken,
        string ns,
        string typeName,
        AssemblyFlags otherFlags,
        AssemblyFlags namingFlags)
    {
        var metadata = NewAssembly("Contoso.Caller");
        var naming = metadata.AddAssemblyReference(
            metadata.GetOrAddString(namingAssembly),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(namingToken),
            flags: namingFlags,
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
        => WriteForwarder(
            directory, name, target, ns, typeName, publicKey, fileName,
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);

    /// <summary>
    /// The widest form. <paramref name="version"/> is the forwarder's own assembly version, and
    /// <paramref name="targetPublicKeyToken"/> the token it records on the reference to its
    /// definer — the identity that is verified against the definer's real
    /// <c>AssemblyDef</c>, so a test can make a facade point at an assembly that does not exist
    /// under that identity.
    /// </summary>
    static void WriteForwarder(
        string directory,
        string name,
        string target,
        string ns,
        string typeName,
        byte[]? publicKey,
        string fileName,
        Version version,
        byte[]? targetPublicKeyToken)
    {
        var metadata = NewAssembly(name, publicKey, version);
        var targetReference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(target),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: targetPublicKeyToken is null
                ? default
                : metadata.GetOrAddBlob(targetPublicKeyToken),
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

        // The assembly the forwarder points at, unless the test already placed one. The terminal
        // hop of a chain is verified against the target's real AssemblyDef, so a fixture with no
        // definer on disk is asserting a forwarding relationship nothing can confirm — which is
        // the shape that fabricated a caller edge (review of a749cd4d). Writing it here keeps the
        // fixtures modelling reality: the library under inspection always exists.
        string definer = Path.Combine(directory, target + ".dll");
        if (!File.Exists(definer))
            File.WriteAllBytes(definer, SerializePE(NewAssembly(target, publicKey: null)));
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
        => BuildCallerNaming(
            assembly, ns, typeName, publicKeyOrToken, flags,
            culture: null, version: new Version(1, 0, 0, 0));

    /// <summary>
    /// The same, with the reference's culture and version held apart from its name and token, so a
    /// test can vary each part of ECMA identity independently.
    /// </summary>
    static MetadataReaderProvider BuildCallerNaming(
        string assembly,
        string ns,
        string typeName,
        byte[]? publicKeyOrToken,
        AssemblyFlags flags,
        string? culture,
        Version version)
    {
        var metadata = NewAssembly("Contoso.Caller");
        var reference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(assembly),
            version,
            culture: culture is null ? default : metadata.GetOrAddString(culture),
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
        => NewAssembly(name, publicKey, new Version(1, 0, 0, 0));

    static MetadataBuilder NewAssembly(string name, byte[]? publicKey, Version version)
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
            version,
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
