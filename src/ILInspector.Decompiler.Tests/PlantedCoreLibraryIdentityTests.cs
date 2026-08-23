using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Core-library identity comes from acquisition, not from what an assembly says
/// about itself. The platform public keys are published data and nothing here
/// verifies a strong-name signature — shipped platform assemblies are public-
/// signed, so the signature slot is zero-filled and there is nothing to verify
/// — which means any file can carry the ECMA key verbatim. Before
/// <c>CoreLibraryIdentityTrust</c>, that was enough to mint <c>corelib</c> for
/// such a file's own definitions, and its <c>System.Collections.IEnumerable</c>
/// then compared equal to the real one — authorizing collection-initializer
/// raising for a type that implements nothing of the sort.
/// <para>
/// These tests plant a file because that is the cheapest way to construct the
/// condition, not because the concern is an attacker. The same confusion
/// arises unintentionally whenever a loose directory holds a stale, mismatched,
/// or reference-only copy of the core library: a pile of binaries is not a
/// coherent closure, and only acquisition can tell the difference.
/// </para>
/// </summary>
public class PlantedCoreLibraryIdentityTests
{
    /// <summary>
    /// A resolver-opened file carrying the ECMA public key does not get to name
    /// its own definitions as core-library types. Fails if the trust check in
    /// <c>TypeRefDecoder.CanonicalSelf</c> is removed, or if the classification
    /// at <c>MetadataContext.Open(ResolvedAssemblyReference)</c> stops running:
    /// the fake interface then satisfies the real one.
    /// </summary>
    [Fact]
    public void PlantedPlatformKey_DoesNotMintCoreLibraryIdentity()
    {
        string directory = Directory.CreateTempSubdirectory(
            "planted-corelib-identity-").FullName;
        try
        {
            string path = Path.Combine(directory, "System.Runtime.dll");
            File.WriteAllBytes(path, BuildPlantedCoreLibrary());

            using var source = MetadataSource.Open(
                typeof(object).Assembly.Location,
                null,
                TestAssemblyReferenceResolvers.SingleAssembly(path));
            TypeRef fake = TypeRef.Definition("System.Runtime", "N", "Fake");

            Assert.NotEqual(
                MetadataFactState.Yes,
                source.SupportsCollectionInitializer(fake));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The same file opened directly is the caller's designated target, which is
    /// trusted by designation, so it keeps the identity its key claims. This is
    /// the negative case for the rule above: the deny list must be scoped to
    /// resolution, not applied to every reader.
    /// </summary>
    [Fact]
    public void DesignatedTarget_KeepsCoreLibraryIdentity()
    {
        string directory = Directory.CreateTempSubdirectory(
            "designated-corelib-identity-").FullName;
        try
        {
            string path = Path.Combine(directory, "System.Runtime.dll");
            File.WriteAllBytes(path, BuildPlantedCoreLibrary());

            using var source = MetadataSource.OpenWithoutSymbols(path);
            TypeRef decoded = TypeRefDecoder.Instance.GetTypeFromDefinition(
                source.Reader,
                MetadataTokens.TypeDefinitionHandle(2),
                rawTypeKind: 0);

            Assert.Equal(TypeRef.CoreLibrary, decoded.Assembly);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// An assembly the caller designated — a corpus path — keeps core-library
    /// identity even though it is reached by resolution rather than being the
    /// target. Designation is the caller's statement of trust, and it is what
    /// would separate a dotnet/runtime build layout from a planted sibling:
    /// the two are indistinguishable in metadata. No command designates a
    /// user-named directory today, and none needs to, because a platform-token
    /// reference resolves at platform scope, which excludes siblings outright.
    /// Fails if <c>DesignatedAsset</c> stops being honoured, which would
    /// silently degrade corpus inspection.
    /// </summary>
    [Fact]
    public void DesignatedAcquisition_KeepsCoreLibraryIdentity()
    {
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Designated("corpus"),
            expectedCorelib: true);
    }

    /// <summary>
    /// A platform acquisition keeps core-library identity, driven end to end
    /// through the resolved-reader path.
    /// <para>
    /// This is the ordinary journey — open a package, follow a reference to a
    /// platform type in a runtime pack or an installed shared framework — and
    /// it is the one every other case here is the negative of. It was missing:
    /// <c>PlatformAsset</c> appeared only in the derived entitled-set gate,
    /// which asks the trust type directly, so nothing drove a platform
    /// acquisition through <c>MetadataContext.Open</c> and checked that the
    /// identity survives.
    /// </para>
    /// <para>
    /// That asymmetry mattered. Every gate here answers "does an attack fail?"
    /// and a change that denied identity to <em>every</em> platform assembly —
    /// a scope regression, a classification that stops running, a deny list
    /// applied too broadly — makes all of them greener, not redder. This is the
    /// case that fails when the feature stops working.
    /// </para>
    /// </summary>
    [Fact]
    public void PlatformAcquisition_KeepsCoreLibraryIdentity()
    {
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Platform(
                "Microsoft.NETCore.App",
                frameworkVersion: null,
                "runtime pack"),
            expectedCorelib: true);
    }

    /// <summary>
    /// A discovered sibling is denied core-library identity. There is no host
    /// opt-in to consult: promoting a loose file to platform status is exactly
    /// the type confusion the strict model rules out, because the directory it
    /// sits in carries no evidence that its contents form a coherent closure.
    /// Fails if <c>MayMint</c> starts entitling <c>LocalAsset</c>, which would
    /// let a stale or mismatched copy of the core library define types that
    /// compare equal to the real ones.
    /// </summary>
    [Fact]
    public void DiscoveredSibling_IsDenied()
    {
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Local("sibling"),
            expectedCorelib: false);
    }

    /// <summary>
    /// Package payloads and embedded uploads are denied too. These are the
    /// close negative cases for the rule above: each is a plausible way for a
    /// file claiming to be the core library to arrive, and neither is a
    /// coherent platform closure. Fails if entitlement is ever widened from
    /// the two acquisitions that carry it.
    /// </summary>
    [Fact]
    public void PackagesAndUploads_AreDenied()
    {
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Package(
                "Contoso.Package",
                "1.0.0",
                tfm: null,
                rid: null),
            expectedCorelib: false);

        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Embedded(
                "upload",
                digest: "sha256:0",
                declaredName: "System.Runtime"),
            expectedCorelib: false);
    }

    /// <summary>
    /// A project output is denied as well, exercised end to end against a real
    /// planted assembly rather than through <c>MayMint</c> alone.
    /// <para>
    /// Both round-3 reviewers observed that <c>ProjectAsset</c> was the one
    /// acquisition with no named case here, which is what made it the natural
    /// carrier for their tamper. The derived gate below does cover it, but it
    /// covers it by calling <c>MayMint</c> directly; this case is the
    /// defence-in-depth backstop that drives the real reader path, so a
    /// widening that somehow escaped the derived gate still has to get past a
    /// decoded assembly.
    /// </para>
    /// <para>
    /// A build output is genuinely close to the entitled cases — it is
    /// produced locally by a compiler the user ran, not fetched from anywhere
    /// — which is exactly why it needs saying explicitly. It is still a loose
    /// file, and a loose file is not a coherent closure.
    /// </para>
    /// </summary>
    [Fact]
    public void ProjectOutput_IsDenied()
    {
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Project(
                "Contoso.csproj",
                tfm: "net10.0",
                rid: null),
            expectedCorelib: false);

        // A project acquisition may carry no target framework at all, and
        // round 4 found that absence was a shape nothing exercised. Denial
        // must not depend on the optional fields being populated. Round 5
        // added the empty spelling: absent and blank are distinct states, and
        // a rule keyed on either reads as the same intent.
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Project(
                "Contoso.csproj",
                tfm: null,
                rid: null),
            expectedCorelib: false);

        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Project(
                "Contoso.csproj",
                tfm: string.Empty,
                rid: string.Empty),
            expectedCorelib: false);
    }

    /// <summary>
    /// Every concrete acquisition is classified, exactly two are entitled, and
    /// entitlement depends only on how the assembly was acquired.
    /// <para>
    /// The cases above name their provenances one at a time, which left a gap
    /// round 1 found: adding <c>ProjectAsset</c> to <c>MayMint</c> widened the
    /// rule past its own documented allow list and left all of them green,
    /// because no test mentioned that provenance. This gate derives its
    /// coverage from the type hierarchy instead, so a provenance nobody
    /// remembered fails here until it is classified deliberately — in either
    /// direction.
    /// </para>
    /// <para>
    /// Round 2 showed that deriving the coverage is not enough on its own, and
    /// each half below answers one of its findings. The enumeration scans the
    /// whole declaring assembly rather than the base type's public nested
    /// types, because <c>private protected</c> closes the hierarchy to that
    /// assembly and nothing more: a same-assembly subtype declared at top level
    /// or marked <c>internal</c> is perfectly legal, and both were invisible to
    /// the first version of this gate. It also compares <see cref="Type"/>
    /// identities rather than names, so two acquisitions that share a short
    /// name cannot stand in for one another.
    /// </para>
    /// <para>
    /// Whether entitlement follows the <em>kind</em> of acquisition rather than
    /// its contents is gated separately and structurally, by
    /// <see cref="TheEntitlementDecisionPath_ReadsNoValueOutOfTheAcquisition"/>. That gate replaced
    /// four rounds of sampling: this one need only establish which kinds are
    /// entitled, using any single instance of each.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryAcquisitionIsClassified_AndExactlyTwoAreEntitled()
    {
        Type[] concrete = ConcreteProvenanceTypes();

        // Guards against the enumeration silently finding nothing, which would
        // make every assertion below vacuously true. Deliberately not a fixed
        // count: a provenance added later is denied by default and caught by
        // the entitled set, so pinning the number would only fail a legitimate
        // change without proving anything the rest does not.
        Assert.NotEmpty(concrete);

        // Observed through the grant path rather than by calling MayMint, so
        // that an entitlement added in GrantIfEntitled is caught here too.
        // Round 7 moved a rule one frame out and left every gate green; a gate
        // that asks the decision method directly cannot see that, because the
        // grant no longer depends only on the answer it gives.
        Type[] entitled = concrete
            .Where(t => GrantsIdentity(Sample(t)))
            .ToArray();

        Assert.True(
            entitled.ToHashSet().SetEquals(
                new[]
                {
                    typeof(AssemblyResolutionProvenance.PlatformAsset),
                    typeof(AssemblyResolutionProvenance.DesignatedAsset),
                }),
            "Entitled acquisitions are "
            + string.Join(", ", entitled.Select(t => t.Name))
            + "; core-library identity may be minted only from a coherent "
            + "closure or an explicit designation.");
    }

    /// <summary>
    /// An absent acquisition entitles nothing. This is a third property, not a
    /// restatement of the other two, and neither of them can see it.
    /// <para>
    /// <see cref="TheEntitlementDecisionPath_ReadsNoValueOutOfTheAcquisition"/> cannot, because
    /// <c>provenance is null</c> is honestly not a read: it lowers to
    /// <c>ldnull</c> and <c>ceq</c>, carries no field, getter, or literal, and
    /// so satisfies that gate truthfully rather than by evading it. That gate's
    /// claim is about the acquisition's <em>contents</em>, and a missing
    /// acquisition has none.
    /// <see cref="EveryAcquisitionIsClassified_AndExactlyTwoAreEntitled"/>
    /// cannot either, because it enumerates the provenance types the product
    /// declares, and absence is not one of them.
    /// </para>
    /// <para>
    /// It is worth gating even though the parameter is declared non-nullable:
    /// nullable annotations are not enforced at run time, so a caller reaching
    /// <c>GrantIfEntitled</c> with a null acquisition would mint core-library
    /// identity for an assembly that was never acquired at all. Round 6 raised
    /// this as a live escape; it is closed here by naming the property rather
    /// than by widening either of the other gates past what it honestly proves.
    /// </para>
    /// </summary>
    [Fact]
    public void AnAbsentAcquisition_IsNotEntitled()
    {
        Assert.False(
            CoreLibraryIdentityTrust.MayMint(null!),
            "A missing acquisition entitled the assembly to core-library "
            + "identity. Nothing was acquired, so nothing establishes the "
            + "coherent closure that identity depends on.");

        // Asked of the grant path as well, for the same reason the entitled
        // set is: `provenance is null` short-circuiting in GrantIfEntitled
        // never reaches MayMint, and reads nothing, so neither the assertion
        // above nor the IL gate can see it.
        Assert.False(
            GrantsIdentity(null!),
            "A missing acquisition granted core-library identity on the way "
            + "to the decision, without the decision entitling it.");
    }

    /// <summary>
    /// One acquisition of the given type, built with a neutral value in every
    /// field. Which value it is no longer matters: that entitlement cannot
    /// depend on any value is established structurally by
    /// <see cref="TheEntitlementDecisionPath_ReadsNoValueOutOfTheAcquisition"/>, so this only has
    /// to produce an instance of the right kind.
    /// </summary>
    static AssemblyResolutionProvenance Sample(Type provenanceType) =>
        Construct(
            provenanceType.GetConstructors()[0],
            (parameter, _) => IsOptional(parameter) ? null : "probe");

    /// <summary>
    /// Whether the acquisition may be constructed without this field, read from
    /// the declared nullability so a field that becomes optional later is built
    /// as absent without anyone remembering to say so.
    /// </summary>
    static bool IsOptional(ParameterInfo parameter) =>
        new NullabilityInfoContext().Create(parameter).WriteState
            == NullabilityState.Nullable;

    /// <summary>
    /// Entitlement must follow the <em>kind</em> of acquisition and never
    /// anything the acquisition contains. This gate proves that from the
    /// mechanism rather than by sampling behaviour: it decodes the IL the
    /// compiler actually emitted for <c>MayMint</c> and requires that the
    /// method never reads a value out of its argument. A rule keyed on content
    /// has to load one — a field directly, a record property through its
    /// getter, or a literal to compare against — so a body that loads none
    /// cannot be keyed on content, whatever it is spelled as.
    /// <para>
    /// This replaced an enumerative gate that built each acquisition under
    /// several field shapes and required a consistent answer. Four consecutive
    /// review rounds each defeated it with a rule shape its shapes could not
    /// distinguish, one axis per round: a field compared against a constant
    /// (<c>PackageAsset { PackageId: "System.Runtime" }</c>), a field compared
    /// against another field (<c>ProjectAsset p when p.Project != p.Tfm</c>), a
    /// field being absent (<c>ProjectAsset { Tfm: null }</c>), and absence
    /// spelled as empty rather than null (<c>ProjectAsset { Tfm: "" }</c>).
    /// Each round closed one axis and left the next one open, because sampling
    /// can only refute the shapes it thought to build. Every one of those four
    /// emits a field or getter load, so all four fail here for the same reason,
    /// as does any fifth axis nobody has thought of.
    /// </para>
    /// <para>
    /// What this does not gate is <em>which</em> kinds are entitled; adding
    /// <c>or ProjectAsset</c> reads nothing and passes. That is the other half,
    /// and <see cref="EveryAcquisitionIsClassified_AndExactlyTwoAreEntitled"/>
    /// holds it. The two compose: this one fixes what the decision may be made
    /// of, that one fixes what the decision is.
    /// </para>
    /// <para>
    /// It covers the whole decision <em>path</em>, not one method. Round 7
    /// defeated a version that decoded only <c>MayMint</c>: the rule was moved
    /// one frame out into <c>GrantIfEntitled</c>, which is what actually
    /// performs the grant, and every gate stayed green while a package named
    /// <c>System.Runtime</c> minted identity. Constraining the method that
    /// answers the question while leaving the method that acts on the answer
    /// unconstrained gates nothing, so both are decoded here.
    /// <c>GrantIfEntitled</c> is additionally allowed to <em>call</em> the two
    /// members it must call, and nothing else — permitting a call is not
    /// permitting a read, and any other callee would be a place to hide one.
    /// </para>
    /// <para>
    /// The path is bounded by <c>ReaderConstructionSiteTests</c> rather than by
    /// this list being complete: the trust type's members are classified as
    /// granting or non-granting there, and a new member fails until someone
    /// classifies it. <c>GrantCoreLibraryIdentity</c> is the other granting
    /// member and is deliberately absent here, because it takes only a reader
    /// and so has no acquisition to key on.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEntitlementDecisionPath_ReadsNoValueOutOfTheAcquisition()
    {
        string assemblyPath = typeof(CoreLibraryIdentityTrust).Assembly.Location;
        Assert.True(
            File.Exists(assemblyPath),
            $"Cannot read the product assembly at '{assemblyPath}'.");

        using var pe = new PEReader(File.OpenRead(assemblyPath));
        MetadataReader metadata = pe.GetMetadataReader();

        AssertReadsNothing(
            pe,
            metadata,
            TheEntitlementDecision(metadata),
            nameof(CoreLibraryIdentityTrust.MayMint),
            permittedCallees: []);

        AssertReadsNothing(
            pe,
            metadata,
            TheGrantSite(metadata),
            nameof(CoreLibraryIdentityTrust.GrantIfEntitled),
            permittedCallees:
            [
                TheEntitlementDecisionHandle(metadata),
                TheUnconditionalGrantHandle(metadata),
            ]);
    }

    /// <summary>
    /// Decodes one method on the decision path and requires that it read no
    /// value out of the acquisition: no field, no property getter, no literal.
    /// <paramref name="permittedCallees"/> is the exact set of methods it may
    /// call, empty for a method that should call nothing at all.
    /// <para>
    /// Callees are matched by <em>metadata row</em>, never by name. Matching on
    /// the name alone was itself an escape, found by replaying round 6's decoy
    /// trick one frame out: a <c>Decoy.MayMint</c> on an unrelated type, called
    /// <em>in addition to</em> the real one, satisfied a name-based allow list
    /// while keeping every legitimate grant working, and the whole class stayed
    /// green. A row handle names one method in one type, so nothing can stand in
    /// for the method the product actually calls.
    /// </para>
    /// </summary>
    static void AssertReadsNothing(
        PEReader pe,
        MetadataReader metadata,
        MethodDefinition method,
        string methodName,
        MethodDefinitionHandle[] permittedCallees)
    {

        MethodInstructions decoded = MethodInstructions.Decode(
            pe.GetMethodBody(method.RelativeVirtualAddress));

        // Non-vacuity: an undecodable or empty body would satisfy every
        // assertion below without proving anything.
        Assert.True(decoded.IsComplete, $"{methodName}'s IL did not decode.");
        Assert.NotEmpty(decoded.Instructions);

        DecodedInstruction[] reads = decoded.Instructions
            .Where(instruction => instruction.Operand is
                OperandKind.InlineField     // ldfld/ldsfld: a field, directly
                or OperandKind.InlineMethod // callvirt: a record property getter
                or OperandKind.InlineString // ldstr: a literal to compare against
                or OperandKind.InlineTok
                or OperandKind.InlineSig)
            .Where(instruction =>
                instruction.Operand != OperandKind.InlineMethod
                || !IsPermittedCallee(
                    metadata, instruction.OperandValue, permittedCallees))
            .ToArray();

        Assert.True(
            reads.Length == 0,
            $"{methodName} reads a value out of the acquisition at IL "
            + "offset(s) "
            + string.Join(
                ", ",
                reads.Select(r =>
                    $"{r.Offset:x4} ({r.OpCode}, {r.Operand}"
                    + (r.Operand == OperandKind.InlineMethod
                        ? $", {CalleeName(metadata, r.OperandValue)}"
                        : string.Empty)
                    + ")"))
            + ". Core-library trust must follow the kind of acquisition, not "
            + "anything the acquisition happens to contain, so the decision may "
            + "name types and nothing else."
            + (permittedCallees.Length == 0
                ? string.Empty
                : " This method may call only "
                    + string.Join(
                        " and ",
                        permittedCallees.Select(c =>
                            metadata.GetString(
                                metadata.GetMethodDefinition(c).Name)))
                    + ", because any other callee is a place to hide a read."));
    }

    /// <summary>
    /// Whether a call instruction's operand is one of the permitted callees,
    /// compared by metadata row. A <c>MemberReference</c> — which is how a call
    /// to any other assembly is encoded — is never permitted, and neither is a
    /// same-named method on another type, because neither shares a row with the
    /// method the product actually calls.
    /// </summary>
    static bool IsPermittedCallee(
        MetadataReader metadata,
        long token,
        MethodDefinitionHandle[] permittedCallees)
    {
        _ = metadata;
        EntityHandle handle = MetadataTokens.EntityHandle((int)token);

        return handle.Kind == HandleKind.MethodDefinition
            && permittedCallees.Contains((MethodDefinitionHandle)handle);
    }

    /// <summary>
    /// The name a call instruction's operand refers to, for diagnostics only.
    /// Permission is decided by <see cref="IsPermittedCallee"/> on the metadata
    /// row; this never participates in that decision.
    /// </summary>
    static string CalleeName(MetadataReader metadata, long token)
    {
        EntityHandle handle = MetadataTokens.EntityHandle((int)token);

        return handle.Kind switch
        {
            HandleKind.MethodDefinition => metadata.GetString(
                metadata.GetMethodDefinition(
                    (MethodDefinitionHandle)handle).Name),
            HandleKind.MemberReference => metadata.GetString(
                metadata.GetMemberReference(
                    (MemberReferenceHandle)handle).Name),
            _ => $"<{handle.Kind}>",
        };
    }

    /// <summary>
    /// The one method whose IL decides entitlement, resolved by metadata token
    /// rather than by name.
    /// <para>
    /// Round 6 defeated a name-scanning predecessor that returned the first
    /// method called <c>MayMint</c>: adding an unrelated parameterless
    /// <c>MayMint()</c> overload ahead of the real one redirected the gate onto
    /// the decoy's empty body, so a genuinely content-keyed rule
    /// (<c>PackageAsset { PackageId: "System.Runtime" }</c>) passed the whole
    /// class. Reflection binds the overload by parameter type, and the token it
    /// reports names exactly one row in the method table, so no sibling method
    /// can stand in for the one the product actually consults.
    /// </para>
    /// <para>
    /// Ambiguity is refused rather than resolved: if the type ever declares
    /// more than one <c>MayMint</c>, this fails and asks for a deliberate
    /// decision about which one is the entitlement decision, instead of
    /// quietly gating one of them and leaving the other unexamined.
    /// </para>
    /// </summary>
    static MethodDefinition TheEntitlementDecision(MetadataReader metadata) =>
        metadata.GetMethodDefinition(
            Resolve(
                metadata,
                nameof(CoreLibraryIdentityTrust.MayMint),
                typeof(bool),
                [typeof(AssemblyResolutionProvenance)]));

    /// <summary>
    /// The row <c>MayMint</c> occupies, so that a call to it can be told from a
    /// call to anything else that merely shares its name.
    /// </summary>
    static MethodDefinitionHandle TheEntitlementDecisionHandle(
        MetadataReader metadata) =>
        Resolve(
            metadata,
            nameof(CoreLibraryIdentityTrust.MayMint),
            typeof(bool),
            [typeof(AssemblyResolutionProvenance)]);

    /// <summary>
    /// The row of the unconditional grant. <c>GrantIfEntitled</c> is permitted
    /// to call it because that call is the grant itself; it takes only a
    /// reader, so it has no acquisition to key on and nothing to hide.
    /// </summary>
    /// <remarks>
    /// Named by string rather than by <c>nameof</c> because the member is
    /// <see langword="private"/> as of round 9 — that privacy is what makes
    /// <c>GrantIfEntitled</c> the only way to reach it. The name is not thereby
    /// unpinned: <c>ReaderConstructionSiteTests.TrustTypeMembers_AreClassified</c>
    /// enumerates the type's methods out of metadata, where accessibility does
    /// not hide them, and fails if this member is renamed or removed without
    /// being reclassified.
    /// </remarks>
    static MethodDefinitionHandle TheUnconditionalGrantHandle(
        MetadataReader metadata) =>
        Resolve(
            metadata,
            "GrantCoreLibraryIdentity",
            typeof(void),
            [typeof(MetadataReader)]);

    /// <summary>
    /// The method that acts on the decision, resolved the same way and for the
    /// same reason. Round 7 moved a content-keyed rule here, one frame out from
    /// <c>MayMint</c>, and every gate stayed green — so this body is decoded
    /// too.
    /// </summary>
    static MethodDefinition TheGrantSite(MetadataReader metadata) =>
        metadata.GetMethodDefinition(
            Resolve(
                metadata,
                nameof(CoreLibraryIdentityTrust.GrantIfEntitled),
                typeof(void),
                [typeof(MetadataReader), typeof(AssemblyResolutionProvenance)]));

    /// <summary>
    /// The single method of that name and signature on the trust type,
    /// resolved by metadata token rather than by name.
    /// </summary>
    static MethodDefinitionHandle Resolve(
        MetadataReader metadata,
        string methodName,
        Type returnType,
        Type[] parameterTypes)
    {
        const BindingFlags Declared =
            BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        MethodInfo[] named = typeof(CoreLibraryIdentityTrust)
            .GetMethods(Declared)
            .Where(m => m.Name == methodName)
            .ToArray();

        Assert.True(
            named.Length == 1,
            $"Expected exactly one {nameof(CoreLibraryIdentityTrust)}."
            + $"{methodName}, found {named.Length}: "
            + string.Join(", ", named.Select(m => m.ToString()))
            + ". This gate decodes that method, and cannot tell which overload "
            + "is meant. Name it uniquely, or teach this gate which one to "
            + "read.");

        MethodInfo decision = named[0];

        // The signature is pinned as well as the count, so that redefining the
        // method to take something other than an acquisition -- which would
        // make "reads no value out of the acquisition" a claim about a
        // different argument -- fails here rather than passing quietly.
        Assert.Equal(returnType, decision.ReturnType);
        Assert.Equal(
            parameterTypes,
            decision.GetParameters().Select(p => p.ParameterType));

        var handle = (MethodDefinitionHandle)
            MetadataTokens.EntityHandle(decision.MetadataToken);

        // Guards the assumption that the loaded assembly and the file just
        // opened are the same build. A token read from one and resolved
        // against the other would otherwise land on an arbitrary method.
        Assert.Equal(
            methodName,
            metadata.GetString(metadata.GetMethodDefinition(handle).Name));

        return handle;
    }

    /// <summary>
    /// Whether the product actually grants core-library identity for an
    /// acquisition, observed end to end: hand it to the grant site and ask the
    /// reader afterwards.
    /// <para>
    /// This is what makes the behavioural gates cover the decision
    /// <em>path</em> rather than the decision method. A rule added anywhere on
    /// the way to the grant changes this answer, wherever it is spelled.
    /// </para>
    /// <para>
    /// Each call opens its own reader because trust is recorded per reader
    /// instance, so a shared one would carry a grant from an earlier sample
    /// into a later one and report entitlement that acquisition never earned.
    /// </para>
    /// </summary>
    static bool GrantsIdentity(AssemblyResolutionProvenance provenance)
    {
        using var pe = new PEReader(
            File.OpenRead(typeof(CoreLibraryIdentityTrust).Assembly.Location));

        MetadataReader reader = pe.GetMetadataReader();
        CoreLibraryIdentityTrust.GrantIfEntitled(reader, provenance);

        return CoreLibraryIdentityTrust.MayMintCoreLibraryIdentity(reader);
    }

    /// <summary>
    /// Every acquisition the product can express. The base constructor is
    /// <c>private protected</c>, so subtypes are confined to the declaring
    /// assembly but not to the base type's nested public scope — hence the
    /// assembly-wide scan.
    /// </summary>
    static Type[] ConcreteProvenanceTypes() =>
        typeof(AssemblyResolutionProvenance).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract
                && t.IsSubclassOf(typeof(AssemblyResolutionProvenance)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Builds a provenance through the given constructor, taking each argument
    /// from <paramref name="value"/>. Callers pass every public constructor
    /// rather than one chosen constructor: round 5 observed that a wider
    /// overload delegating to a narrower one can pin a field to a value no
    /// argument controls, so a rule keyed on that pinned value would answer
    /// identically for every variant built through the wider overload alone.
    /// Each constructor in this hierarchy takes only strings, and the required
    /// ones reject blank input, so any non-blank value satisfies all of them
    /// without the test needing to know which arguments a particular
    /// acquisition carries.
    /// </summary>
    static AssemblyResolutionProvenance Construct(
        ConstructorInfo constructor,
        Func<ParameterInfo, int, string?> value)
    {
        Type provenanceType = constructor.DeclaringType!;

        // Without this the reflection call below throws from inside the BCL,
        // which still fails the gate but reports a MemberAccessException
        // instead of saying what a maintainer has to do about it.
        Assert.False(
            provenanceType.ContainsGenericParameters,
            $"{provenanceType.Name} is an open generic type; this helper "
            + "constructs closed types only and needs updating.");

        ParameterInfo[] parameters = constructor.GetParameters();
        Assert.All(
            parameters,
            parameter => Assert.True(
                parameter.ParameterType == typeof(string),
                $"{provenanceType.Name} takes a non-string parameter "
                + $"'{parameter.Name}'; this helper needs updating."));

        object?[] arguments = parameters
            .Select(object? (parameter, index) => value(parameter, index))
            .ToArray();

        return (AssemblyResolutionProvenance)constructor.Invoke(arguments);
    }

    /// <summary>
    /// Opens the planted assembly through reference resolution with a given
    /// acquisition provenance, and reports whether its own definitions decoded
    /// as core-library types.
    /// </summary>
    static void RunWithResolvedCoreLibrary(
        AssemblyResolutionProvenance provenance,
        bool expectedCorelib)
    {
        string directory = Directory.CreateTempSubdirectory(
            "resolved-corelib-identity-").FullName;
        try
        {
            string path = Path.Combine(directory, "System.Runtime.dll");
            File.WriteAllBytes(path, BuildPlantedCoreLibrary());

            var resolver = new ProvenanceResolver(path, provenance);
            using var context = new MetadataContext(resolver);

            OpenedAssembly opened = Assert.IsType<OpenedAssembly>(
                context.Open(
                    resolver.Resolve(
                        new AssemblyReferenceIdentity(
                            "System.Runtime",
                            Version: null,
                            Culture: null,
                            PublicKeyToken: null),
                        AssemblyResolutionScope.Any)!));

            TypeRef decoded = TypeRefDecoder.Instance.GetTypeFromDefinition(
                opened.Reader,
                MetadataTokens.TypeDefinitionHandle(2),
                rawTypeKind: 0);

            Assert.Equal(
                expectedCorelib,
                decoded.Assembly == TypeRef.CoreLibrary);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    sealed class ProvenanceResolver(
        string path,
        AssemblyResolutionProvenance provenance) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => string.Equals(
                identity.Name,
                "System.Runtime",
                StringComparison.OrdinalIgnoreCase)
                ? ResolvedAssemblyReference.CreateFromPath(path, provenance)
                : null;
    }

    /// <summary>
    /// An assembly named <c>System.Runtime</c> carrying the real ECMA public key
    /// blob, defining its own <c>System.Collections.IEnumerable</c> (row 2) and a
    /// class implementing it (row 3). The key is copied from the running core
    /// library precisely because it is public: no private key is involved and no
    /// signature is produced.
    /// </summary>
    static byte[] BuildPlantedCoreLibrary()
    {
        byte[] platformPublicKey = typeof(object).Assembly.GetName().GetPublicKey()!;

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("System.Runtime.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(platformPublicKey),
            AssemblyFlags.PublicKey,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var iface = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("System.Collections"),
            metadata.GetOrAddString("IEnumerable"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var fake = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fake"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddInterfaceImplementation(fake, iface);

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    /// <summary>
    /// The bypass round-2 review found: <c>MetadataSource.OpenCore</c> creates
    /// readers without going through <c>MetadataContext</c>, which is the path
    /// <c>MemberBodyProducer</c> takes for a type defined in a sibling
    /// assembly. Under the original deny list an unregistered reader failed
    /// open and the planted sibling minted corelib identity anyway. Fails if
    /// the registry ever goes back to deny-list polarity.
    /// </summary>
    [Fact]
    public void PlantedSibling_OpenedThroughMetadataSource_LosesCoreLibraryIdentity()
    {
        string directory = Directory.CreateTempSubdirectory(
            "planted-corelib-bypass-").FullName;
        try
        {
            string path = Path.Combine(directory, "System.Runtime.dll");
            File.WriteAllBytes(path, BuildPlantedCoreLibrary());

            var identity = IdentityOf(path);
            var sibling = ResolvedAssemblyReference.Create(
                identity,
                path,
                () => File.OpenRead(path),
                AssemblyResolutionProvenance.Local("SiblingAssembly"));

            using var source = MetadataSource.OpenWithoutSymbols(
                sibling,
                TestAssemblyReferenceResolvers.SingleAssembly(path));
            TypeRef fake = TypeRef.Definition("System.Runtime", "N", "Fake");

            Assert.NotEqual(
                MetadataFactState.Yes,
                source.SupportsCollectionInitializer(fake));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A raw path is a caller designation, so a genuine core library opened out
    /// of a plain directory — the dotnet/runtime build-layout workflow — keeps
    /// its identity. This is the negative case that keeps the fail-closed
    /// registry from re-breaking ordinary use.
    /// </summary>
    [Fact]
    public void RawPathOpen_KeepsCoreLibraryIdentity()
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            typeof(object).Assembly.Location);

        Assert.Equal(
            TypeRef.CoreLibrary,
            TypeRefDecoder.CanonicalSelf(source.Reader));
    }

    /// <summary>
    /// An explicitly enumerated corpus path is designated, so it must satisfy
    /// the platform-scope request a corelib TypeRef forces. Round-2 review
    /// found the resolver excluding <c>CorpusAssembly</c> from platform scope,
    /// which left the designated build-layout route broken even though the
    /// provenance mapping was correct.
    /// </summary>
    [Fact]
    public void DesignatedCorpusAssembly_SatisfiesPlatformScope()
    {
        string directory = Directory.CreateTempSubdirectory(
            "designated-corpus-scope-").FullName;
        try
        {
            string target = Path.Combine(directory, "Target.dll");
            File.Copy(typeof(PlantedCoreLibraryIdentityTests).Assembly.Location, target);

            string corelib = Path.Combine(directory, "System.Private.CoreLib.dll");
            File.Copy(typeof(object).Assembly.Location, corelib);

            var resolver = new DotnetInspector.Services.AssemblyDependencyResolver(
                new DotnetInspector.Services.AssemblyDependencyResolutionOptions(target)
                {
                    CorpusAssemblyPaths = [corelib],
                    IncludeSiblingAssemblies = false,
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                });

            var identity = IdentityOf(corelib);

            ResolvedAssemblyReference? resolved = resolver.Resolve(
                identity,
                AssemblyResolutionScope.Platform);

            Assert.Equal(corelib, resolved?.Path);
            Assert.IsType<AssemblyResolutionProvenance.DesignatedAsset>(
                resolved?.Provenance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Reads the assembly identity and lets the mapped image go. Returning the
    /// <see cref="MetadataReader"/> itself would hand back a reader over a
    /// disposed <see cref="PEReader"/>.
    /// </summary>
    static AssemblyReferenceIdentity IdentityOf(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }


    /// <summary>
    /// Every public static factory that produces a <see cref="MetadataSource"/>
    /// must classify the reader it creates. Twice now a hand-maintained list of
    /// those sites has been wrong — round-2 review found
    /// <c>MetadataSource.OpenCore</c> missing while the registry still failed
    /// open, and round-3 review found <c>OpenFromPrefetchedImage</c> missing
    /// once it failed closed. So this gate derives the set by reflection rather
    /// than restating it: a new factory that forgets to classify fails here
    /// without anyone remembering to add it.
    /// <para>
    /// Each entry point is handed a genuine core library, named either by path
    /// (a designation) or by a <see cref="ResolvedAssemblyReference"/> carrying
    /// a designated acquisition, so the expected answer is the same for all of
    /// them: core-library identity is granted.
    /// </para>
    /// <para>
    /// Selection deliberately ignores the method's name and its declared
    /// return type, keying on the object actually handed back, because review
    /// defeated both of the narrower rules in turn.
    /// <para>
    /// Scope, stated precisely because this gate is meant to be trusted instead
    /// of a hand audit: it proves a grant was not <em>forgotten</em>, not that a
    /// grant is correctly <em>conditional</em>. Every entry point here is fed a
    /// trusted acquisition, so a future discovery-style overload could satisfy
    /// this test with an unconditional grant — which would be the #4411 bug.
    /// <c>PlantedSibling_OpenedThroughMetadataSource_LosesCoreLibraryIdentity</c>
    /// and <c>DiscoveredSibling_IsDenied</c> are what hold the
    /// conditional half, by driving an untrusted provenance into
    /// <see cref="CoreLibraryIdentityTrust.GrantIfEntitled"/> and asserting
    /// denial — the first through <c>MetadataSource.OpenCore</c>, the second
    /// through <c>MetadataContext.OpenResolved</c>, which are the two routes
    /// that reach it. A new overload taking a
    /// <see cref="ResolvedAssemblyReference"/> needs a negative case there as
    /// well as passing this.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPublicFactory_ClassifiesTheReaderItCreates()
    {
        string corelib = typeof(object).Assembly.Location;
        // Derive on the return type, not on the method name: filtering on an
        // "Open" prefix let a rename drop an entry point out of coverage while
        // the remaining overloads kept this test green (round-4 review). Match
        // any return type a MetadataSource is assignable to, not just the exact
        // type, because declaring a factory as IDisposable escaped an exact
        // comparison the same way (round-5 review). What the method is called
        // and how its result is typed are both cosmetic; handing back a live
        // MetadataSource is the property that matters, so the actual returned
        // object decides.
        var entryPoints = typeof(MetadataSource)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType.IsAssignableFrom(typeof(MetadataSource)))
            .ToList();

        Assert.NotEmpty(entryPoints);

        var unclassified = new List<string>();
        foreach (var method in entryPoints)
        {
            object?[] arguments;
            try
            {
                arguments = [.. method.GetParameters().Select(p => ArgumentFor(p, corelib))];
            }
            catch (NotSupportedException e)
            {
                throw new InvalidOperationException(
                    $"{method.Name} takes a parameter this gate cannot supply ({e.Message}). "
                    + "Extend ArgumentFor so the entry point stays covered.",
                    e);
            }

            object? result = method.Invoke(null, arguments);
            if (result is not MetadataSource source)
                continue;

            using (source)
            {
                if (TypeRefDecoder.CanonicalSelf(source.Reader) != TypeRef.CoreLibrary)
                    unclassified.Add(Signature(method));
            }
        }

        Assert.True(
            unclassified.Count == 0,
            "These MetadataSource entry points create a reader without granting "
            + "core-library identity to a designated core library, so anything "
            + "opened through them silently loses corelib identity:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, unclassified));
    }

    static string Signature(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    static object? ArgumentFor(ParameterInfo parameter, string corelib)
    {
        if (parameter.ParameterType == typeof(string))
            return parameter.Name == "path" ? corelib : null;
        if (parameter.ParameterType == typeof(ImmutableArray<byte>))
            return File.ReadAllBytes(corelib).ToImmutableArray();
        if (parameter.ParameterType == typeof(MetadataContext))
            return null;
        if (typeof(IAssemblyReferenceResolver).IsAssignableFrom(parameter.ParameterType))
            return TestAssemblyReferenceResolvers.SingleAssembly(corelib);
        if (typeof(IAssemblyBindingPolicy).IsAssignableFrom(parameter.ParameterType))
            return new AssemblyReferenceBindingPolicy(
                TestAssemblyReferenceResolvers.SingleAssembly(corelib));
        if (parameter.ParameterType == typeof(ResolvedAssemblyReference))
            return ResolvedAssemblyReference.Create(
                IdentityOf(corelib),
                corelib,
                () => File.OpenRead(corelib),
                AssemblyResolutionProvenance.Designated("test designation"));
        if (parameter.ParameterType == typeof(bool))
            return parameter.HasDefaultValue ? parameter.DefaultValue : true;

        throw new NotSupportedException(parameter.ParameterType.Name);
    }

}
