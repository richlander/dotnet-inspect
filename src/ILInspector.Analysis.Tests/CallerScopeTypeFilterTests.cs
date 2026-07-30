using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// <see cref="CallerScopeTypeFilter"/> decides, from an assembly's <c>TypeRef</c> table alone,
/// whether it could contain a <em>direct</em> caller of a member declared by a given type. Its
/// correctness obligation is the same shape as <see cref="CallerScopeFilter"/>'s and no weaker: it
/// must never rule out an assembly whose call sites
/// <see cref="MemberPattern.MatchesCrossAssembly"/> would have matched.
///
/// The tests that matter are the ones distinguishing it from its sibling. <see cref="CallerScopeFilter"/>
/// asks whether an assembly could reach the target <em>assembly</em>, transitively; this asks
/// whether it names the target <em>type</em>, directly. Every case below where the two disagree is
/// a real compiled fixture, because the saving this filter exists for is exactly the size of that
/// disagreement.
/// </summary>
public class CallerScopeTypeFilterTests
{
    static string Target => FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
    static string Caller => FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath();
    static string Twin => FixtureCatalog.AnalysisCallerGraphCallerTwin.AssemblyPath();
    static string Indirect => FixtureCatalog.AnalysisCallerGraphIndirectCaller.AssemblyPath();
    static string Lookalike => FixtureCatalog.AnalysisCallerGraphLookalikeCaller.AssemblyPath();

    /// <summary>
    /// The declaring type of a real method in the target fixture, taken from the index rather than
    /// spelled by hand so the expected value is the one the matcher actually compares against.
    /// </summary>
    static TypeRef DeclaringTypeOf(string typeName, string methodName)
    {
        var index = LibraryBodyIndex.Open(Target);
        var method = index.Methods.First(m => m.DeclaringType.Name == typeName && m.Name == methodName);
        return GenericMemberIdentity.OpenDeclaringType(method.DeclaringType);
    }

    static CallerScopeTypeFilter.TypeReferenceState Classify(string candidatePath, TypeRef declaringType)
        => CallerScopeTypeFilter.Classify(candidatePath, declaringType);

    /// <summary>
    /// Whether <see cref="CallerScopeFilter"/> — the transitive, assembly-level sibling — keeps a
    /// candidate. Used to assert that this filter is strictly narrower on the same input, which is
    /// the entire reason it exists.
    ///
    /// The whole scope is supplied, not just the candidate under test: the closure closes over the
    /// scope rather than the machine, so an assembly whose only bridge to the target is another
    /// scope member is only kept when that member is present.
    /// </summary>
    static bool AssemblyFilterKeeps(string candidatePath)
    {
        static ILInspector.Metadata.AssemblyIdentityNames Identity(string path)
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return ILInspector.Metadata.AssemblyIdentityScanner.Scan(peReader);
        }

        string[] scope = [Caller, Twin, Indirect, Lookalike];
        var candidates = scope
            .Select(path =>
            {
                var identity = Identity(path);
                return CallerScopeFilter.Candidate.Known(identity.Name, identity.ReferenceNames);
            })
            .ToArray();

        var selected = CallerScopeFilter.SelectCouldReach(Identity(Target).Name, candidates);
        return selected[Array.IndexOf(scope, candidatePath)];
    }

    // The real caller calls Target.Api.Ping, so its TypeRef table names Target.Api and it must be
    // kept. This is the assembly the caller-edge tests prove does contribute edges.
    [Fact]
    public void AssemblyNamingTheDeclaringTypeIsKept()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(Caller, DeclaringTypeOf("Api", "Ping")));
    }

    // THE case this filter exists for. The twin references the target assembly and calls
    // Target.Api.Ping, so the assembly-level filter keeps it for every target in that assembly.
    // But it never mentions Target.Box`1, so no call site in it can produce a callee whose
    // declaring type matches Box`1, and opening it to discover that is pure cost.
    //
    // The two assertions are one claim: the filters disagree, and the disagreement is the saving.
    [Fact]
    public void AssemblyNamingTheTargetAssemblyButNotTheTypeIsRuledOut()
    {
        Assert.True(AssemblyFilterKeeps(Twin));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(Twin, DeclaringTypeOf("Box`1", "Store")));
    }

    // Same assembly, same scan, a type it does reference: without this the test above would pass
    // for an assembly the filter simply cannot read.
    [Fact]
    public void TheSameAssemblyIsKeptForATypeItDoesName()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(Twin, DeclaringTypeOf("Api", "Ping")));
    }

    // The transitive obligation that binds CallerScopeFilter does NOT bind this one, and that is a
    // deliberate difference rather than an oversight. The indirect fixture references only the
    // caller assembly, so it belongs in a caller *graph* rooted at Ping (two hops) and the
    // assembly-level filter correctly keeps it. It has no call site targeting Ping itself, so it
    // contributes no inbound *edge* and the single-hop consumer must not pay to open it.
    [Fact]
    public void TransitiveOnlyCallerIsRuledOutForDirectEdges()
    {
        Assert.True(AssemblyFilterKeeps(Indirect));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(Indirect, DeclaringTypeOf("Api", "Ping")));
    }

    // The lookalike declares its own Target.Api.Ping and calls that, so its calls resolve to its
    // own TypeDef and the matcher already excludes it. Ruling it out here matches that.
    //
    // Note what this does NOT pin, because the name invites the stronger reading: the lookalike
    // never references the target assembly at all, so it carries no TypeRef row for Target.Api in
    // any assembly. Mutation-tested — an assembly-insensitive comparison still rules it out, so
    // this case cannot certify assembly sensitivity. FrameworkTypeMatchesOnlyUnderItsOwnAssembly
    // is the test that does, and it is the one that kills that mutant.
    [Fact]
    public void SameNamedTypeInAnotherAssemblyIsRuledOut()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(Lookalike, DeclaringTypeOf("Api", "Ping")));
    }

    // A callee declared by a TypeDef carries the defining assembly's own name and appears in no
    // TypeRef row, so the target assembly has to be kept without scanning for it.
    [Fact]
    public void TargetAssemblyItselfIsKept()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(Target, DeclaringTypeOf("Api", "Ping")));
    }

    // A constructed instantiation reaches its declaring type through a TypeSpec whose signature
    // bottoms out at the open definition's TypeRef row, so scanning that table sees it. The caller
    // fixture only ever spells Box<int>, never the open Box`1.
    [Fact]
    public void ConstructedGenericCallerNamesTheOpenDefinition()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(Caller, DeclaringTypeOf("Box`1", "Store")));
    }

    // Generic arity is part of type identity, so Box`1 and Box`2 are different types. The caller
    // references both; the twin references neither. Pinning both directions keeps a filter that
    // strips arity from passing.
    [Fact]
    public void GenericArityIsPartOfTheIdentity()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(Caller, DeclaringTypeOf("Box`2", "Store")));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(Twin, DeclaringTypeOf("Box`2", "Store")));
    }

    // Corelib facade canonicalization, the same property that makes CallerScopeFilter safe. The
    // fixtures reference System.Runtime and none references System.Private.CoreLib by name, so a
    // raw name comparison would rule out every caller of a corelib type.
    [Fact]
    public void CorelibTypeIsFoundThroughTheFacadeReference()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(Caller, TypeRef.CoreLib("System", "Object")));
    }

    // A corelib type nothing in the fixture uses. Without this the facade test above would pass
    // for a filter that keeps any assembly referencing corelib at all — which is every assembly,
    // and is precisely the over-selection this filter removes.
    [Fact]
    public void UnreferencedCorelibTypeIsRuledOut()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(Caller, TypeRef.CoreLib("System.Net.Sockets", "Socket")));
    }

    // Assembly sensitivity on a framework type the fixture really does call, and the reason
    // canonicalization must not be mistaken for "any framework assembly will do". The caller
    // constructs a List<int>, and that callee's declaring type resolves to System.Collections —
    // NOT to the corelib alias set. Spelling the same namespace and name against corelib must
    // therefore miss, exactly as the matcher does, which compares the same value.
    [Fact]
    public void FrameworkTypeMatchesOnlyUnderItsOwnAssembly()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            Classify(Caller, TypeRef.Definition("System.Collections", "System.Collections.Generic", "List`1")));

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(Caller, TypeRef.CoreLib("System.Collections.Generic", "List`1")));
    }

    // A declaring type that is not a plain definition has no assembly-qualified identity to filter
    // on, so the filter declines to decide rather than ruling the candidate out.
    [Fact]
    public void NonDefinitionDeclaringTypeIsUndecidable()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Undecidable,
            Classify(Caller, TypeRef.SzArray(TypeRef.CoreLib("System", "Int32"))));
    }

    // A file that is not a readable managed image could not have contributed edges either, so it
    // is ruled out rather than treated as undecidable — the same reasoning that keeps the
    // assembly-level filter from being disabled by one native DLL in a --bin directory.
    [Fact]
    public void UnreadableImageIsRuledOut()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cstf-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]);
        try
        {
            Assert.Equal(
                CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
                Classify(path, DeclaringTypeOf("Api", "Ping")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileIsRuledOut()
    {
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            Classify(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.dll"),
                DeclaringTypeOf("Api", "Ping")));
    }

    /// <summary>
    /// The soundness obligation stated directly, over every fixture and every declaring type in the
    /// target: an assembly ruled out here must contribute no matching call site.
    ///
    /// This is the test that would catch a filter narrower than the matcher, which is the only
    /// failure mode that changes output rather than cost. It compares against the matcher itself
    /// rather than against a golden list, so it cannot drift from the behavior it guards, and it
    /// asserts its own non-vacuity: a run in which nothing is ever ruled out, or in which no ruled-in
    /// assembly ever matches, would prove nothing.
    /// </summary>
    [Fact]
    public void NothingRuledOutEverContributesAMatchingCallSite()
    {
        var targetIndex = LibraryBodyIndex.Open(Target);
        var declaringTypes = targetIndex.Methods
            .Select(m => GenericMemberIdentity.OpenDeclaringType(m.DeclaringType))
            .Distinct()
            .ToList();

        var candidates = new[] { Caller, Twin, Indirect, Lookalike, Target };
        int ruledOut = 0;
        int matchedWhenKept = 0;

        foreach (var declaringType in declaringTypes)
        {
            foreach (var method in targetIndex.Methods
                         .Where(m => GenericMemberIdentity.OpenDeclaringType(m.DeclaringType).Equals(declaringType)))
            {
                var pattern = MemberPattern.Method(method.DeclaringType, method.Name, method.ParameterTypes);

                foreach (string candidate in candidates)
                {
                    bool kept = Classify(candidate, declaringType)
                        is not CallerScopeTypeFilter.TypeReferenceState.DoesNotName;

                    bool matches = LibraryBodyIndex.Open(candidate).DirectCalls
                        .Any(call => pattern.MatchesCrossAssembly(call.Callee));

                    if (kept)
                    {
                        if (matches)
                            matchedWhenKept++;
                        continue;
                    }

                    ruledOut++;
                    Assert.False(
                        matches,
                        $"{Path.GetFileNameWithoutExtension(candidate)} was ruled out for "
                        + $"{declaringType.ToQualifiedDisplayString()} but matches {method.Name}");
                }
            }
        }

        // Non-vacuity, over both classifications. Without the first the filter could rule nothing
        // out; without the second the matcher could match nothing and the assertion above would
        // hold for any filter at all.
        Assert.True(ruledOut > 0, "no candidate was ever ruled out, so the assertion proved nothing");
        Assert.True(matchedWhenKept > 0, "no kept candidate ever matched, so the matcher proved nothing");
    }

    /// <summary>
    /// Metadata for a module with no assembly manifest — the shape <c>csc -target:module</c>
    /// produces. Only the metadata blob is built, because <see cref="CallerScopeTypeFilter"/>
    /// decides from a <see cref="MetadataReader"/> and never needs the surrounding PE.
    ///
    /// The corelib reference and <c>System.Object</c> base type are not decoration: a real
    /// <c>csc -target:module</c> output carries exactly them, and a fixture without them would let
    /// a negative test "rule out" <c>System.Object</c>, which no real module would allow. Verified
    /// against an actual netmodule, whose entire TypeRef table is:
    /// <code>
    /// System.Runtime.CompilerServices.RefSafetyRulesAttribute -> asmref:System.Private.CoreLib
    /// System.Object                                           -> asmref:System.Private.CoreLib
    /// </code>
    ///
    /// The assertions in the tests below pin the two properties that make this stand in for a
    /// compiled netmodule, so the fixture cannot quietly stop being one: the reader is not an
    /// assembly, and the decoder gives its definitions the empty assembly name.
    /// </summary>
    static MetadataReaderProvider BuildModuleMetadata(string ns, string typeName)
    {
        var builder = new MetadataBuilder();
        builder.AddModule(
            generation: 0,
            builder.GetOrAddString("ModOnly.netmodule"),
            builder.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        var corelib = builder.AddAssemblyReference(
            builder.GetOrAddString("System.Private.CoreLib"),
            new Version(9, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        var objectRef = builder.AddTypeReference(
            corelib,
            builder.GetOrAddString("System"),
            builder.GetOrAddString("Object"));

        // Row 1 is always <Module>; the real type follows it, exactly as a compiler emits.
        builder.AddTypeDefinition(
            default,
            default,
            builder.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        builder.AddTypeDefinition(
            TypeAttributes.Public,
            builder.GetOrAddString(ns),
            builder.GetOrAddString(typeName),
            baseType: objectRef,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var root = new MetadataRootBuilder(builder);
        var blob = new BlobBuilder();
        root.Serialize(blob, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
    }

    /// <summary>
    /// A type defined by a module with no assembly manifest decodes to the empty assembly name,
    /// and <see cref="MemberPattern.MatchesCrossAssembly"/> compares that name like any other. The
    /// filter therefore has to recognise it as the module's own identity and keep the candidate.
    ///
    /// Deriving the answer by hand from <c>reader.IsAssembly</c> gets this wrong: the guard skips
    /// the whole own-identity check for module metadata, the type appears in no <c>TypeRef</c> row
    /// because it is defined here, and the module is ruled out despite declaring the very type
    /// being searched for. Asking <see cref="TypeRefDecoder"/> instead cannot drift from what the
    /// matcher compares.
    /// </summary>
    [Fact]
    public void ModuleWithoutAnAssemblyManifestIsNotRuledOutForItsOwnTypes()
    {
        using var provider = BuildModuleMetadata("Sample", "ModTarget");
        var reader = provider.GetMetadataReader();

        Assert.False(reader.IsAssembly);

        var declaringType = reader.TypeDefinitions
            .Select(h => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, h, 0))
            .First(t => t.Name == "ModTarget");

        Assert.Equal(string.Empty, declaringType.Assembly);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(reader, declaringType));
    }

    /// <summary>
    /// The same module must still be ruled out for a type it does not declare, or the test above
    /// would be satisfied by a filter that simply kept every module.
    ///
    /// The foreign type has to be one a real module would genuinely not name. <c>System.Object</c>
    /// is the wrong choice and was the original mistake here: every compiled module references it
    /// as a base type, so ruling it out would have been a claim no real netmodule could satisfy —
    /// the assertion only held because the fixture was less faithful than the thing it stood for.
    /// It is now asserted in the other direction, as a positive control.
    /// </summary>
    [Fact]
    public void ModuleWithoutAnAssemblyManifestIsStillRuledOutForAForeignType()
    {
        using var provider = BuildModuleMetadata("Sample", "ModTarget");
        var reader = provider.GetMetadataReader();

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.DoesNotName,
            CallerScopeTypeFilter.Classify(
                reader,
                TypeRef.Definition("System.Net.Sockets", "System.Net.Sockets", "Socket")));

        // Positive control: the type the module really does reference is found through the
        // TypeRef table, so the negative above is a property of this type and not of modules.
        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Names,
            CallerScopeTypeFilter.Classify(reader, TypeRef.CoreLib("System", "Object")));
    }

    /// <summary>
    /// A <c>TypeRef</c> row the decoder cannot project is not evidence of absence: the rows that
    /// did decode do not speak for it, so the candidate has to be kept. The row here nests
    /// resolution scopes deeper than <c>MetadataSafetyPolicy</c> allows the traversal to walk,
    /// which is the cheapest way to make the real decoder return <c>Unsupported</c> without
    /// corrupting anything else.
    /// </summary>
    [Fact]
    public void AnUndecodableTypeReferenceRowLeavesTheCandidateUndecided()
    {
        var builder = new MetadataBuilder();
        builder.AddModule(
            generation: 0,
            builder.GetOrAddString("Deep.dll"),
            builder.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        builder.AddTypeDefinition(
            default,
            default,
            builder.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        // Each row's resolution scope is the row above it, so the chain never terminates in an
        // AssemblyRef and its length is bounded only by the row count.
        int depth = ILInspector.Metadata.MetadataSafetyPolicy.MaxRelationshipNodes + 2;
        for (int i = 1; i <= depth; i++)
        {
            builder.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(i == 1 ? depth : i - 1),
                builder.GetOrAddString(""),
                builder.GetOrAddString($"Nested{i}"));
        }

        var root = new MetadataRootBuilder(builder);
        var blob = new BlobBuilder();
        root.Serialize(blob, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
        var reader = provider.GetMetadataReader();

        // The premise: the decoder really does fail on this table. Without it the test would pass
        // for a filter that never returns Undecidable at all.
        Assert.Contains(
            reader.TypeReferences,
            h => TypeRefDecoder.Instance.GetTypeFromReference(reader, h, 0).Kind == TypeRefKind.Unsupported);

        Assert.Equal(
            CallerScopeTypeFilter.TypeReferenceState.Undecidable,
            CallerScopeTypeFilter.Classify(reader, TypeRef.CoreLib("System", "Object")));
    }
}
