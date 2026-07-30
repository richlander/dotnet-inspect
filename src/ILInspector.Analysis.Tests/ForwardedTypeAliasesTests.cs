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
    ///
    /// <para>The rival route is an intermediate facade that <em>verifies</em>, on purpose. An
    /// earlier fixture pointed the second file straight at a bare <c>Other.Definer</c>, which the
    /// definer-edge check rejected on its own — so this test passed with the disagreement rule
    /// disabled and gated nothing (raised in review of <c>7572838c</c>). Routing through a facade
    /// whose own edge is well formed leaves the disagreement as the only thing that can refuse
    /// it.</para>
    /// </summary>
    [Fact]
    public void PrefilterDeclinesASpellingWhoseTwoFilesForwardToDifferentDefiners()
    {
        string directory = NewTempDirectory();

        // The rival route: a real intermediate whose edge from `Contoso.Facade` verifies, and which
        // carries the type on somewhere that is not the target.
        WriteForwarder(
            directory, "Contoso.Middle", "Other.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "Contoso.Middle");

        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "first");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");

        // The premise: the genuine file alone aliases the spelling, so the refusal below is the
        // second file's doing.
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(
                caller.GetMetadataReader(),
                target,
                ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"))));

        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Middle", "Contoso", "Widget",
            publicKey: null, fileName: "second");

        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

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
    /// A failing forwarder edge withdraws its own spelling and nothing else. Canonicalization
    /// collapses the five core-library facade spellings onto one name, so keying the chain map on
    /// the canonical name made a stray <c>mscorlib</c> that forwards the type somewhere
    /// unverifiable withdraw the genuine <c>netstandard</c> alias sharing its bucket — a caller
    /// that really does call the target, dropped. Raised in review of <c>7572838c</c>.
    ///
    /// <para>This is the caller side's lesson arriving on the definer side one round later.
    /// <see cref="PrefilterKeepsAnAliasWhenAnUncheckableReferenceSharesItsCanonicalName"/> settled
    /// the same question for references in review of <c>b18e5009</c>: withdraw the spelling, never
    /// the bucket. The fix is to key the chain map on the raw spelling and canonicalize only after
    /// pruning.</para>
    /// </summary>
    [Fact]
    public void ADeadCorelibFacadeDoesNotWithdrawAGenuineSiblingInItsBucket()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "netstandard", "Contoso.Definer", "Contoso", "Widget");

        // The control: alone, the genuine facade is an alias. Without it this test would pass on a
        // build where nothing is ever aliased.
        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        using var caller = BuildCallerNaming("netstandard", "Contoso", "Widget");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(
                caller.GetMetadataReader(),
                target,
                ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"))));

        // A stray same-bucket facade pointing at an assembly that is not on disk, so its own edge
        // cannot be verified and is refused. `mscorlib` and `netstandard` both canonicalize to
        // `corelib`.
        WriteForwarder(
            directory, "mscorlib", "Contoso.Absent", "Contoso", "Widget",
            publicKey: null, fileName: "mscorlib");
        File.Delete(Path.Combine(directory, "Contoso.Absent.dll"));

        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        Assert.True(aliases.IncludesRawSpelling("netstandard"));
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));

        // And the stray itself is still refused, so this did not buy recall with a fabrication.
        using var strayCaller = BuildCallerNaming("mscorlib", "Contoso", "Widget");
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(strayCaller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// The negative control for the unsigned multi-version rule: observing two versions widens the
    /// accepted set to exactly those two, not to every version.
    /// </summary>
    [Fact]
    public void PrefilterDeclinesAVersionUnsignedEvidenceWasNeverObservedAt()    {
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
    /// Two spellings that canonicalize together are still two assemblies, and one that forwards the
    /// type somewhere else entirely must not be credited with its sibling's target. Collapsing them
    /// before asking the reachability question pooled their targets, so <c>netstandard</c> vouched
    /// for callers on the strength of what <c>mscorlib</c> forwards (executed in review of
    /// <c>7572838c</c>).
    ///
    /// <para>This is the fabricating direction of the same defect as
    /// <see cref="ADeadCorelibFacadeDoesNotWithdrawAGenuineSiblingInItsBucket"/>, which is its
    /// dropping direction. Canonicalization is right for what the matcher compares and wrong for
    /// what the evidence says.</para>
    /// </summary>
    [Fact]
    public void ACanonicalSiblingDoesNotLendItsTargetToASpellingThatForwardsElsewhere()
    {
        byte[] definerKey = [.. Enumerable.Repeat((byte)0x51, 16)];
        byte[] msKey = [.. Enumerable.Repeat((byte)0x52, 16)];
        byte[] netKey = [.. Enumerable.Repeat((byte)0x53, 16)];
        byte[] otherKey = [.. Enumerable.Repeat((byte)0x54, 16)];
        byte[] otherDefinerKey = [.. Enumerable.Repeat((byte)0x55, 16)];

        string directory = NewTempDirectory();

        // Written first so no forwarder supplies an unsigned stand-in for them.
        File.WriteAllBytes(
            Path.Combine(directory, "Contoso.Definer.dll"),
            SerializePE(NewAssembly("Contoso.Definer", definerKey)));
        File.WriteAllBytes(
            Path.Combine(directory, "Contoso.OtherDefiner.dll"),
            SerializePE(NewAssembly("Contoso.OtherDefiner", otherDefinerKey)));

        // `netstandard` carries the type to a wholly unrelated definer, by way of a second facade
        // so the route is a real verified chain rather than a dangling edge.
        WriteForwarder(
            directory, "Contoso.Other", "Contoso.OtherDefiner", "Contoso", "Widget",
            publicKey: otherKey, fileName: "Contoso.Other",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: TokenOf(otherDefinerKey));
        WriteForwarder(
            directory, "netstandard", "Contoso.Other", "Contoso", "Widget",
            publicKey: netKey, fileName: "netstandard",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: TokenOf(otherKey));

        // `mscorlib` is the genuine facade for the target, and canonicalizes to the same bucket.
        WriteForwarder(
            directory, "mscorlib", "Contoso.Definer", "Contoso", "Widget",
            publicKey: msKey, fileName: "mscorlib",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: TokenOf(definerKey));

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        // The positive control: the genuine sibling in that bucket still works, so the refusal
        // below is not the whole bucket having been withdrawn.
        using var genuine = BuildCallerNaming("mscorlib", "Contoso", "Widget", TokenOf(msKey));
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(genuine.GetMetadataReader(), target, aliases));

        using var elsewhere = BuildCallerNaming("netstandard", "Contoso", "Widget", TokenOf(netKey));
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(elsewhere.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// A file that <em>defines</em> the type contradicts a same-named file that forwards it, just
    /// as a rival forwarder does. Nothing distinguishes which of the two a caller naming that
    /// spelling was bound to, so reporting it against the target invents a call it may never have
    /// made.
    ///
    /// <para>Reduced from a review fixture built with the real compiler: two projects both named
    /// <c>Contoso.Facade</c>, one carrying <c>[assembly: TypeForwardedTo(typeof(Contoso.Widget))]</c>
    /// and one declaring <c>Contoso.Widget</c> itself, with a caller referencing the second.</para>
    /// </summary>
    [Fact]
    public void ATwinThatDefinesTheTypeContradictsTheFacadeThatForwardsIt()
    {
        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "ForwardingFacade");

        // The control: with only the forwarder present the spelling is an alias, so the refusal
        // below is the twin's doing and not this fixture failing to produce an alias at all.
        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(
                caller.GetMetadataReader(),
                target,
                ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"))));

        // The twin: same assembly name, same (absent) key, same version — indistinguishable to any
        // caller — but it declares Contoso.Widget rather than forwarding it.
        WriteDefiner(directory, "Contoso.Facade", "Contoso", "Widget", fileName: "RivalFacade");

        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));

        // The raw spelling survives, as it does for every other contradiction here: it only widens
        // which assemblies are opened, and the refusal above is what decides the answer. Asserted
        // so a later reading does not mistake its presence for the alias still applying.
        Assert.True(aliases.IncludesRawSpelling("Contoso.Facade"));
    }

    /// <summary>
    /// A twin declaring some <em>other</em> type still contradicts the facade, because it is silent
    /// about <em>this</em> one. What matters is not what the twin declares but that a caller bound
    /// to it does not reach the target.
    ///
    /// <para>This test previously asserted the opposite, as the negative control for the rule
    /// above — the reasoning being that a twin must contradict on the type rather than on the name.
    /// That reasoning was refuted by a runtime control in review of <c>e7c04f92</c>: one caller
    /// binary and one deployment, with two files of identical identity swapped in turn, prints
    /// <c>target</c> against the forwarder and throws
    /// <c>TypeLoadException: Could not load type 'Contoso.Widget'</c> against a twin declaring only
    /// <c>Contoso.Gadget</c> — this fixture's exact shape. Reporting the caller was a fabrication.
    /// The guard against "any same-named neighbour refuses" is now the version limit, held by
    /// <see cref="ASiblingAtAVersionTheSpellingDoesNotAnswerForLeavesTheAliasStanding"/> and
    /// <see cref="TwoSiblingsThatBothForwardDoNotContradictEachOther"/>.</para>
    /// </summary>
    [Fact]
    public void ATwinThatDefinesAnUnrelatedTypeContradictsTheFacadeAnyway()
    {
        string directory = NewTempDirectory();
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "ForwardingFacade");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");

        // The control: without the twin the facade answers, so the refusal below is the twin's
        // doing and not a fixture that never resolved in the first place.
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(
                caller.GetMetadataReader(),
                target,
                ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"))));

        WriteDefiner(directory, "Contoso.Facade", "Contoso", "Gadget", fileName: "RivalFacade");

        var aliases = ForwardedTypeAliases.ForTarget(target, Directory.GetFiles(directory, "*.dll"));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));

        // The raw spelling survives, as it does for every other contradiction here: it only widens
        // which assemblies are opened, and the refusal above is what decides the answer.
        Assert.True(aliases.IncludesRawSpelling("Contoso.Facade"));
    }

    /// <summary>
    /// The real guard against "any same-named neighbour refuses": a silent sibling at a version the
    /// spelling does not answer for takes nothing from the forwarder, because a caller naming the
    /// forwarder's version was never going to be admitted for that file anyway.
    ///
    /// <para>Without this, the contradiction rule would undo the version ceiling from review of
    /// <c>37a4444b</c>, whose point was that a v2 file dropped beside a v1 facade must refuse v2
    /// callers <em>without</em> costing the v1 callers that the facade genuinely answers for.
    /// <see cref="ANonForwardingSiblingDoesNotLendItsVersionToAFacade"/> holds the refusing half;
    /// this holds the half that must keep working, which nothing asserted before.</para>
    /// </summary>
    [Fact]
    public void ASiblingAtAVersionTheSpellingDoesNotAnswerForLeavesTheAliasStanding()
    {
        string directory = NewTempDirectory();
        try
        {
            byte[] publicKey = [.. Enumerable.Repeat((byte)0xA7, 16)];
            var target = TypeRef.Definition("Contoso.Target", "Contoso", "Widget");
            string targetPath = Path.Combine(directory, "Contoso.Target.dll");

            WriteDefiner(directory, "Contoso.Target", "Contoso", "Widget", "Contoso.Target");
            WriteForwarder(
                directory, "Contoso.Facade", "Contoso.Target", "Contoso", "Widget",
                publicKey, fileName: "Facade.v1",
                version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);
            WriteNonForwarder(
                directory, "Contoso.Facade", publicKey, "Facade.v2", new Version(2, 0, 0, 0));

            var aliases = ForwardedTypeAliases.ForTarget(
                target, targetPath, Directory.GetFiles(directory, "*.dll"), seedSpellings: null);
            using var caller = BuildCallerNaming(
                "Contoso.Facade", "Contoso", "Widget", TokenOf(publicKey),
                flags: default, culture: null, version: new Version(1, 0, 0, 0));

            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// An unreadable target file must not produce a <em>wider</em> answer than a readable one.
    /// Losing the authoritative identity used to fall through to the census, which named a rival
    /// definer the readable path refuses — so deleting a file admitted a caller.
    /// </summary>
    [Fact]
    public void AnUnreadableTargetFileDoesNotWidenTheAnswerTheReadableOneGives()
    {
        string directory = NewTempDirectory();
        byte[] scopeKey = [.. Enumerable.Repeat((byte)0x0A, 16)];
        byte[] authoritativeKey = [.. Enumerable.Repeat((byte)0x0B, 16)];

        // The facade forwards to a `Contoso.Definer` bearing the SCOPE's key, and a matching
        // `Contoso.Definer` sits beside it — so the census on its own would confirm this edge.
        File.WriteAllBytes(
            Path.Combine(directory, "Contoso.Definer.dll"),
            SerializePE(NewAssembly("Contoso.Definer", scopeKey)));
        WriteForwarder(
            directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "Contoso.Facade",
            version: new Version(1, 0, 0, 0), targetPublicKeyToken: TokenOf(scopeKey));

        // The assembly actually under inspection is a different `Contoso.Definer`, so the facade
        // does not forward to it and the alias must be refused.
        string targetDirectory = NewTempDirectory();
        string targetPath = Path.Combine(targetDirectory, "Contoso.Definer.dll");
        File.WriteAllBytes(targetPath, SerializePE(NewAssembly("Contoso.Definer", authoritativeKey)));

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var scope = Directory.GetFiles(directory, "*.dll");

        // Read authoritatively, the edge is refused.
        Assert.False(
            ForwardedTypeAliases
                .ForTarget(target, targetPath, scope, seedSpellings: null)
                .IncludesRawSpelling("Contoso.Facade"));

        // The sensitivity control: the census answers the OTHER way, so the assertion below can
        // only hold because the fallback is not taken — not because nothing verifies here.
        Assert.True(
            ForwardedTypeAliases
                .ForTarget(target, targetAssemblyPath: null, scope, seedSpellings: null)
                .IncludesRawSpelling("Contoso.Facade"));

        // Losing the authoritative file must not hand the question to the census, which would make
        // an unreadable target admit a caller the readable one refuses.
        string unreadable = Path.Combine(targetDirectory, "Gone.dll");
        var afterLoss = ForwardedTypeAliases.ForTarget(
            target, unreadable, scope, seedSpellings: null);

        Assert.False(afterLoss.IncludesRawSpelling("Contoso.Facade"));

        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, afterLoss));
    }

    /// <summary>
    /// Assembly names are compared case-insensitively everywhere else here — including this walk's
    /// own visited set — so the chain's terminal comparison must be too. An ordinal comparison
    /// there dropped a genuine caller whose facade spelled the definer's name in another case.
    /// </summary>
    [Fact]
    public void AForwarderNamingTheDefinerInAnotherCaseStillReachesIt()
    {
        string directory = NewTempDirectory();

        // The facade's ExportedType points at `CONTOSO.DEFINER`; the file on disk claims
        // `Contoso.Definer`. ECMA identity comparison is case-insensitive, so this is one assembly.
        WriteForwarder(directory, "Contoso.Facade", "CONTOSO.DEFINER", "Contoso", "Widget");
        File.WriteAllBytes(
            Path.Combine(directory, "Contoso.Definer.dll"),
            SerializePE(NewAssembly("Contoso.Definer", publicKey: null)));

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        string targetPath = Path.Combine(directory, "Contoso.Definer.dll");
        var aliases = ForwardedTypeAliases.ForTarget(
            target, targetPath, Directory.GetFiles(directory, "*.dll"), seedSpellings: null);

        Assert.True(aliases.IncludesRawSpelling("Contoso.Facade"));

        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
    }

    /// <summary>
    /// One file listed twice is one claimant. Counting it twice made a single-claimant name look
    /// contested, and the target's identity was then treated as unknowable — dropping every caller.
    /// </summary>
    [Fact]
    public void TheSameFileSuppliedTwiceIsStillOneClaimantOfItsName()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var once = Directory.GetFiles(directory, "*.dll");

        // The control: the answer this fixture gives when nothing is duplicated.
        Assert.True(
            ForwardedTypeAliases.ForTarget(target, once).IncludesRawSpelling("Contoso.Facade"));

        var twice = ForwardedTypeAliases.ForTarget(target, [.. once, .. once]);

        Assert.True(twice.IncludesRawSpelling("Contoso.Facade"));

        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, twice));
    }

    /// <summary>
    /// A multi-module assembly declares its public types in netmodules and lists them in the
    /// manifest as non-forwarder <c>ExportedType</c> rows implemented by an <c>AssemblyFile</c>.
    /// That is a declaration, not a forward, so a same-named twin holding one contradicts a facade
    /// exactly as a <c>TypeDef</c> does — but a scan that looked only at <c>TypeDef</c> rows could
    /// not see it, and the caller bound to the twin's own type was reported as calling the target.
    /// </summary>
    [Fact]
    public void ATwinThatExportsTheTypeFromItsOwnModuleContradictsTheFacade()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        string targetPath = Path.Combine(directory, "Contoso.Definer.dll");

        // The control: with only the forwarder present, the alias stands.
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(target, targetPath, directory));

        // The twin claims the same identity and declares `Contoso.Widget` in its own netmodule.
        WriteModuleExporter(
            directory, "Contoso.Facade", "Contoso", "Widget", fileName: "Rival.Facade");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(target, targetPath, directory));
    }

    /// <summary>
    /// A module-exported type that is not the one under inspection makes the twin no less of a
    /// contradiction: it is still a file of the facade's identity that does not carry
    /// <c>Contoso.Widget</c>, so a caller bound to it fails to load the type.
    ///
    /// <para>Like <see cref="ATwinThatDefinesAnUnrelatedTypeContradictsTheFacadeAnyway"/>, this
    /// asserted the opposite until the runtime control in review of <c>e7c04f92</c> refuted the
    /// premise. The concern it was written for — that a rule might refuse on the mere presence of
    /// an <c>AssemblyFile</c>-implemented export — is still worth holding, and the control below
    /// holds it: the same fixture answers before the twin is written.</para>
    /// </summary>
    [Fact]
    public void ATwinThatExportsAnUnrelatedTypeFromItsModuleContradictsTheFacadeAnyway()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        string targetPath = Path.Combine(directory, "Contoso.Definer.dll");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(target, targetPath, directory));

        WriteModuleExporter(
            directory, "Contoso.Facade", "Contoso", "Gadget", fileName: "Rival.Facade");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(target, targetPath, directory));
    }

    /// <summary>
    /// A file carrying two forwarder rows for one type disagrees with itself, and nothing says
    /// which row a caller bound to. Answering from whichever row the table happens to list first
    /// makes the verdict depend on metadata row order — so the same disagreement that refuses two
    /// files must refuse one file twice over.
    /// </summary>
    [Fact]
    public void AFileForwardingOneTypeToTwoDefinersCannotAnswerForIt()
    {
        foreach (bool targetFirst in new[] { true, false })
        {
            string directory = NewTempDirectory();

            // Both definers exist and both edges verify, so the only thing that can refuse this is
            // the disagreement itself.
            File.WriteAllBytes(
                Path.Combine(directory, "Contoso.Definer.dll"),
                SerializePE(NewAssembly("Contoso.Definer", publicKey: null)));
            File.WriteAllBytes(
                Path.Combine(directory, "Other.Definer.dll"),
                SerializePE(NewAssembly("Other.Definer", publicKey: null)));

            WriteDoubleForwarder(
                directory,
                "Contoso.Facade",
                first: targetFirst ? "Contoso.Definer" : "Other.Definer",
                second: targetFirst ? "Other.Definer" : "Contoso.Definer",
                "Contoso",
                "Widget");

            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
                Classify(
                    TypeRef.Definition("Contoso.Definer", "Contoso", "Widget"),
                    Path.Combine(directory, "Contoso.Definer.dll"),
                    directory));
        }
    }

    /// <summary>
    /// The control for the rule above: one file forwarding a type it already forwards to the same
    /// definer is not a disagreement, so a duplicated row must not refuse the alias.
    /// </summary>
    [Fact]
    public void AFileForwardingOneTypeTwiceToTheSameDefinerStillAnswersForIt()
    {
        string directory = NewTempDirectory();
        File.WriteAllBytes(
            Path.Combine(directory, "Contoso.Definer.dll"),
            SerializePE(NewAssembly("Contoso.Definer", publicKey: null)));
        WriteDoubleForwarder(
            directory, "Contoso.Facade",
            first: "Contoso.Definer", second: "Contoso.Definer", "Contoso", "Widget");

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(
                TypeRef.Definition("Contoso.Definer", "Contoso", "Widget"),
                Path.Combine(directory, "Contoso.Definer.dll"),
                directory));
    }

    /// <summary>
    /// One file reached by two spellings of its path is one claimant. Comparing the supplied
    /// strings alone let `dir\x.dll` and `dir\.\x.dll` count as two files contesting one name, so
    /// the target's identity became unknowable and every genuine caller was dropped.
    /// </summary>
    [Fact]
    public void OneFileNamedByTwoEquivalentPathsIsStillOneClaimant()
    {
        string directory = NewTempDirectory();
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Definer", "Contoso", "Widget");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var once = Directory.GetFiles(directory, "*.dll");

        // The control: the answer this fixture gives when no path is restated.
        Assert.True(
            ForwardedTypeAliases.ForTarget(target, once).IncludesRawSpelling("Contoso.Facade"));

        // The same files, with the target restated through an equivalent path.
        string restated = Path.Combine(directory, ".", "Contoso.Definer.dll");
        Assert.True(File.Exists(restated));

        Assert.True(
            ForwardedTypeAliases
                .ForTarget(target, [.. once, restated])
                .IncludesRawSpelling("Contoso.Facade"));
    }

    /// <summary>
    /// A file that answers to a facade's exact identity — same name, version, culture and token —
    /// but carries no forwarder for the type contradicts the facade. Nothing distinguishes the two
    /// at bind time, so a caller naming that identity may load the one without the type and fail;
    /// reporting it as a caller of the target invents a call it never makes.
    ///
    /// <para>Round 13 capped the <em>version</em> a non-forwarding sibling could lend. That left
    /// the case where there is no version to disagree about, which is the strongest form of the
    /// attack rather than the weakest (found by GPT-5.6 in round 15, with a runtime control showing
    /// the caller throws <c>TypeLoadException</c> against the non-forwarding file).</para>
    /// </summary>
    [Fact]
    public void ASameVersionSiblingThatForwardsNothingContradictsAFacade()
    {
        string directory = NewTempDirectory();
        try
        {
            byte[] publicKey = [.. Enumerable.Repeat((byte)0xA7, 16)];
            var target = TypeRef.Definition("Contoso.Target", "Contoso", "Widget");
            string targetPath = Path.Combine(directory, "Contoso.Target.dll");

            WriteDefiner(directory, "Contoso.Target", "Contoso", "Widget", "Contoso.Target");
            WriteForwarder(
                directory, "Contoso.Facade", "Contoso.Target", "Contoso", "Widget",
                publicKey, fileName: "Facade.a",
                version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);

            CallerScopeTypeFilter.TypeReferenceState AskForAV1Caller()
            {
                var aliases = ForwardedTypeAliases.ForTarget(
                    target, targetPath, Directory.GetFiles(directory, "*.dll"), seedSpellings: null);
                using var caller = BuildCallerNaming(
                    "Contoso.Facade", "Contoso", "Widget", TokenOf(publicKey),
                    flags: default, culture: null, version: new Version(1, 0, 0, 0));
                return CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases);
            }

            // The control: one file answers to the identity and it does forward the type.
            Assert.Equal(CallerScopeTypeFilter.TypeReferenceState.Names, AskForAV1Caller());

            // The attack: a second file of the SAME version that forwards nothing. There is now no
            // identity field on which the two disagree, so nothing can tell them apart.
            WriteNonForwarder(
                directory, "Contoso.Facade", publicKey, "Facade.b", new Version(1, 0, 0, 0));

            Assert.Equal(CallerScopeTypeFilter.TypeReferenceState.DoesNotName, AskForAV1Caller());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The negative control for <see cref="ASameVersionSiblingThatForwardsNothingContradictsAFacade"/>:
    /// two files of one identity that BOTH forward the type are not a contradiction, because either
    /// one a caller binds to reaches the target. Refusing here would drop a genuine caller whenever
    /// a facade is duplicated across two directories of a scope, which is ordinary.
    /// </summary>
    [Fact]
    public void TwoSiblingsThatBothForwardDoNotContradictEachOther()
    {
        string directory = NewTempDirectory();
        try
        {
            byte[] publicKey = [.. Enumerable.Repeat((byte)0xA7, 16)];
            var target = TypeRef.Definition("Contoso.Target", "Contoso", "Widget");
            string targetPath = Path.Combine(directory, "Contoso.Target.dll");

            WriteDefiner(directory, "Contoso.Target", "Contoso", "Widget", "Contoso.Target");
            foreach (string fileName in new[] { "Facade.a", "Facade.b" })
            {
                WriteForwarder(
                    directory, "Contoso.Facade", "Contoso.Target", "Contoso", "Widget",
                    publicKey, fileName,
                    version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);
            }

            var aliases = ForwardedTypeAliases.ForTarget(
                target, targetPath, Directory.GetFiles(directory, "*.dll"), seedSpellings: null);
            using var caller = BuildCallerNaming(
                "Contoso.Facade", "Contoso", "Widget", TokenOf(publicKey),
                flags: default, culture: null, version: new Version(1, 0, 0, 0));

            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A file that claims a spelling but forwards nothing does not lend its version to the file
    /// that does forward.
    ///
    /// <para>The version ceiling a spelling can answer for is merged across every census claimant,
    /// and the census deliberately sees files that do not forward the type — that is what lets a
    /// same-named twin contradict a facade. But merging a non-forwarding claimant's <em>version</em>
    /// raises the ceiling for the forwarder, and a signed reference is admitted whenever it sits at
    /// or below that ceiling. So a v1 facade that forwards the type could be made to vouch for a
    /// caller built against v2 by dropping an unrelated v2 file of the same identity beside it —
    /// a caller that in fact binds to the v2 file, which does not forward the type at all.</para>
    ///
    /// <para>This is the additive rule failing in the fabricating direction: evidence that says
    /// nothing about the type creates a caller. Found in review of <c>37a4444b</c>. The version
    /// ceiling now comes from the files that actually forward the type; the census still supplies
    /// token and culture, so a twin can still contradict.</para>
    /// </summary>
    [Fact]
    public void ANonForwardingSiblingDoesNotLendItsVersionToAFacade()
    {
        string directory = NewTempDirectory();
        try
        {
            byte[] publicKey = [.. Enumerable.Repeat((byte)0xA7, 16)];
            var target = TypeRef.Definition("Contoso.Target", "Contoso", "Widget");
            string targetPath = Path.Combine(directory, "Contoso.Target.dll");

            WriteDefiner(directory, "Contoso.Target", "Contoso", "Widget", "Contoso.Target");
            WriteForwarder(
                directory, "Contoso.Facade", "Contoso.Target", "Contoso", "Widget",
                publicKey, fileName: "Facade.v1",
                version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);

            CallerScopeTypeFilter.TypeReferenceState AskForAV2Caller()
            {
                var aliases = ForwardedTypeAliases.ForTarget(
                    target, targetPath, Directory.GetFiles(directory, "*.dll"), seedSpellings: null);
                using var caller = BuildCallerNaming(
                    "Contoso.Facade", "Contoso", "Widget", TokenOf(publicKey),
                    flags: default, culture: null, version: new Version(2, 0, 0, 0));
                return CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases);
            }

            // The control. Only the v1 file forwards the type, and it cannot answer for a reference
            // that names v2 — so without the sibling there is no alias and no caller.
            Assert.Equal(CallerScopeTypeFilter.TypeReferenceState.DoesNotName, AskForAV2Caller());

            // The attack: a v2 assembly of the same identity that forwards nothing at all.
            WriteNonForwarder(
                directory, "Contoso.Facade", publicKey, "Facade.v2", new Version(2, 0, 0, 0));

            Assert.Equal(CallerScopeTypeFilter.TypeReferenceState.DoesNotName, AskForAV2Caller());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The negative control for the rule above: when the v2 sibling really does forward the type,
    /// its version is genuine evidence and the v2 caller is a real caller.
    /// </summary>
    [Fact]
    public void AForwardingSiblingDoesLendItsVersionToAFacade()
    {
        string directory = NewTempDirectory();
        try
        {
            byte[] publicKey = [.. Enumerable.Repeat((byte)0xA7, 16)];
            var target = TypeRef.Definition("Contoso.Target", "Contoso", "Widget");
            string targetPath = Path.Combine(directory, "Contoso.Target.dll");

            WriteDefiner(directory, "Contoso.Target", "Contoso", "Widget", "Contoso.Target");
            WriteForwarder(
                directory, "Contoso.Facade", "Contoso.Target", "Contoso", "Widget",
                publicKey, fileName: "Facade.v1",
                version: new Version(1, 0, 0, 0), targetPublicKeyToken: null);
            WriteForwarder(
                directory, "Contoso.Facade", "Contoso.Target", "Contoso", "Widget",
                publicKey, fileName: "Facade.v2",
                version: new Version(2, 0, 0, 0), targetPublicKeyToken: null);

            var aliases = ForwardedTypeAliases.ForTarget(
                target, targetPath, Directory.GetFiles(directory, "*.dll"), seedSpellings: null);
            using var caller = BuildCallerNaming(
                "Contoso.Facade", "Contoso", "Widget", TokenOf(publicKey),
                flags: default, culture: null, version: new Version(2, 0, 0, 0));

            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.Names,
                CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Writes an assembly that claims a name and identity but exports nothing — a census claimant
    /// with no forwarder evidence of its own.
    /// </summary>
    static void WriteNonForwarder(
        string directory,
        string name,
        byte[]? publicKey,
        string fileName,
        Version version)
        => File.WriteAllBytes(
            Path.Combine(directory, fileName + ".dll"),
            SerializePE(NewAssembly(name, publicKey, version)));

    static CallerScopeTypeFilter.TypeReferenceState Classify(
        TypeRef target,
        string targetPath,
        string directory)
    {
        var aliases = ForwardedTypeAliases.ForTarget(
            target, targetPath, Directory.GetFiles(directory, "*.dll"), seedSpellings: null);

        using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");
        return CallerScopeTypeFilter.Classify(caller.GetMetadataReader(), target, aliases);
    }

    /// <summary>
    /// Writes an assembly whose manifest exports a type declared in one of its own netmodules —
    /// the multi-module representation of a public type, which is a declaration rather than a
    /// forward.
    /// </summary>
    static void WriteModuleExporter(
        string directory,
        string name,
        string ns,
        string typeName,
        string fileName)
    {
        var metadata = NewAssembly(name);
        var file = metadata.AddAssemblyFile(
            metadata.GetOrAddString(name + ".netmodule"),
            hashValue: default,
            containsMetadata: true);

        // No tdForwarder bit: this row says "declared over there in my own module", not
        // "forwarded to another assembly".
        metadata.AddExportedType(
            TypeAttributes.Public,
            metadata.GetOrAddString(ns),
            metadata.GetOrAddString(typeName),
            file,
            typeDefinitionId: 0);

        File.WriteAllBytes(Path.Combine(directory, fileName + ".dll"), SerializePE(metadata));
    }

    /// <summary>
    /// Writes one facade carrying two forwarder rows for a single type, so a test can vary which
    /// row the metadata lists first while holding everything else fixed.
    /// </summary>
    static void WriteDoubleForwarder(
        string directory,
        string name,
        string first,
        string second,
        string ns,
        string typeName)
    {
        var metadata = NewAssembly(name);

        foreach (string target in new[] { first, second })
        {
            var reference = metadata.AddAssemblyReference(
                metadata.GetOrAddString(target),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            metadata.AddExportedType(
                (TypeAttributes)0x00200000,
                metadata.GetOrAddString(ns),
                metadata.GetOrAddString(typeName),
                reference,
                typeDefinitionId: 0);
        }

        File.WriteAllBytes(Path.Combine(directory, name + ".dll"), SerializePE(metadata));
    }

    /// <summary>
    /// A file's forwarder rows and the files claiming what they point at multiply: every row
    /// enqueued every claimant of its target, so one file carrying many rows for one type queued
    /// rows × claimants entries before anything deduplicated them. Metadata row counts and scope
    /// size are both attacker-controlled, so that product is unbounded work on hostile input.
    ///
    /// <para>Measured as allocation against a one-row control over the same fixture, so the file
    /// reads both cases share cancel out and what remains is the frontier itself.</para>
    /// </summary>
    [Fact]
    public void DuplicateForwarderRowsDoNotMultiplyTheNextFrontier()
    {
        var seeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Contoso.Facade" };

        long control = FrontierCost(rows: 1, claimants: 200, seeds);
        long duplicated = FrontierCost(rows: 2000, claimants: 200, seeds);

        Assert.True(
            duplicated - control < 2_000_000,
            $"one row allocated {control:N0} bytes; 2,000 duplicate rows allocated {duplicated:N0}");
    }

    /// <summary>
    /// Bytes allocated resolving a fixture whose single facade carries <paramref name="rows"/>
    /// forwarder rows for one type, against <paramref name="claimants"/> files all claiming the
    /// assembly those rows point at.
    /// </summary>
    static long FrontierCost(int rows, int claimants, IReadOnlySet<string> seeds)
    {
        string directory = NewTempDirectory();
        WriteRepeatedForwarder(directory, "Contoso.Facade", "Contoso.Hop", "Contoso", "Widget", rows);

        for (int i = 0; i < claimants; i++)
        {
            File.WriteAllBytes(
                Path.Combine(directory, $"Hop{i}.dll"),
                SerializePE(NewAssembly("Contoso.Hop", publicKey: null)));
        }

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        var paths = Directory.GetFiles(directory, "*.dll");

        // Warmed first, so the measurement below is the walk and not one-time JIT.
        ForwardedTypeAliases.ForTarget(target, targetAssemblyPath: null, paths, seeds);

        long before = GC.GetAllocatedBytesForCurrentThread();
        ForwardedTypeAliases.ForTarget(target, targetAssemblyPath: null, paths, seeds);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Writes a facade carrying one type's forwarder row a given number of times.</summary>
    static void WriteRepeatedForwarder(
        string directory,
        string name,
        string target,
        string ns,
        string typeName,
        int rows)
    {
        var metadata = NewAssembly(name);
        var reference = metadata.AddAssemblyReference(
            metadata.GetOrAddString(target),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);

        for (int i = 0; i < rows; i++)
        {
            metadata.AddExportedType(
                (TypeAttributes)0x00200000,
                metadata.GetOrAddString(ns),
                metadata.GetOrAddString(typeName),
                reference,
                typeDefinitionId: 0);
        }

        File.WriteAllBytes(Path.Combine(directory, name + ".dll"), SerializePE(metadata));
    }

    /// <summary>
    /// Assembly names are compared case-insensitively because ECMA-335 says so; file paths are
    /// not, and the two must not share a comparer. On a case-sensitive volume `Hop.dll` and
    /// `hop.dll` are different files, and folding them together discards whichever the walk saw
    /// second — so a file that <em>contradicts</em> a facade disappears and the alias stands on
    /// evidence that was refuted.
    ///
    /// <para>Merging is the fabricating direction and splitting is the safe one: two spellings of
    /// one file counted twice only make a name look contested, which withdraws an alias. So paths
    /// are compared exactly, and normalization — not case folding — is what makes one file compare
    /// equal to itself.</para>
    /// </summary>
    [Fact]
    public void TwoFilesDifferingOnlyInPathCaseAreNotOneClaimant()
    {
        string directory = NewTempDirectory();
        Assert.SkipUnless(
            IsCaseSensitive(directory),
            "Needs a case-sensitive volume to hold two files whose paths differ only in case.");

        // The chain the alias would rest on: facade -> Contoso.Hop -> the target.
        WriteForwarder(directory, "Contoso.Facade", "Contoso.Hop", "Contoso", "Widget");
        WriteForwarder(
            directory, "Contoso.Hop", "Contoso.Definer", "Contoso", "Widget",
            publicKey: null, fileName: "Hop");

        // A different file, also claiming `Contoso.Hop`, that DECLARES the type instead. It
        // contradicts the forwarder, so the spelling cannot answer for the type.
        WriteDefiner(directory, "Contoso.Hop", "Contoso", "Widget", fileName: "hop");

        var target = TypeRef.Definition("Contoso.Definer", "Contoso", "Widget");
        string targetPath = Path.Combine(directory, "Contoso.Definer.dll");
        string[] files =
        [
            Path.Combine(directory, "Contoso.Facade.dll"),
            Path.Combine(directory, "Hop.dll"),
            Path.Combine(directory, "hop.dll"),
            targetPath,
        ];

        // Asserted in both orders, because folding the two files together keeps whichever arrived
        // first — so the defect this pins is visible in one order only.
        //
        // And in both walks, because they reach the second file by different routes: the unseeded
        // walk probes every supplied path, so only its `probed` set can drop one, while the seeded
        // walk reaches files through the census and then through the chain frontier. A fixture that
        // exercised one route left the other's comparer free to fold.
        var seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Contoso.Facade" };

        foreach (var order in new[] { files, (string[])[files[0], files[2], files[1], files[3]] })
        {
            foreach (var seeds in new[] { null, seeded })
            {
                using var caller = BuildCallerNaming("Contoso.Facade", "Contoso", "Widget");
                Assert.Equal(
                    CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
                    CallerScopeTypeFilter.Classify(
                        caller.GetMetadataReader(),
                        target,
                        ForwardedTypeAliases.ForTarget(target, targetPath, order, seeds)));
            }
        }
    }

    /// <summary>Whether a directory's volume distinguishes file names by case.</summary>
    static bool IsCaseSensitive(string directory)
    {
        string probe = Path.Combine(directory, "case-probe.tmp");
        File.WriteAllText(probe, "");
        try
        {
            return !File.Exists(Path.Combine(directory, "CASE-PROBE.TMP"));
        }
        finally
        {
            File.Delete(probe);
        }
    }

    /// <summary>Writes an assembly that declares the named type rather than forwarding it.</summary>
    static void WriteDefiner(string directory, string name, string ns, string typeName, string fileName)
    {
        var metadata = NewAssembly(name);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString(ns),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        File.WriteAllBytes(Path.Combine(directory, fileName + ".dll"), SerializePE(metadata));
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

    static string NewTempDirectory()
    {
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
