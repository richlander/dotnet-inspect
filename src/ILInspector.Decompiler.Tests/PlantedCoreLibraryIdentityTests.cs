using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
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
        // must not depend on the optional fields being populated.
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Project(
                "Contoso.csproj",
                tfm: null,
                rid: null),
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
    /// The second half gates the property that makes the first half meaningful.
    /// Entitlement must follow the <em>kind</em> of acquisition and never the
    /// content of its fields, so each provenance is built under several field
    /// shapes — see <see cref="Variants"/> — and <c>MayMint</c> must answer
    /// identically for every one. Two rounds each found a rule shape the
    /// previous version could not see: a field compared against a constant,
    /// such as <c>PackageAsset { PackageId: "System.Runtime" }</c>, which a
    /// single placeholder value never matched; and a field compared against
    /// another field, such as <c>ProjectAsset p when p.Project != p.Tfm</c>,
    /// which stayed invisible while every field held the same value.
    /// </para>
    /// <para>
    /// The residual is stated rather than hidden: a rule keyed on a spelling
    /// outside <see cref="ProvenanceSpellings"/> that is also insensitive to
    /// whether the fields are uniform or distinct would still satisfy this
    /// gate. Closing that completely would require proving a negative over all
    /// strings, which no test can do; the defence is that <c>MayMint</c> is a
    /// single type-pattern expression, kept small enough to audit by reading.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryAcquisitionIsClassified_AndExactlyTwoAreEntitled()
    {
        Type[] concrete = ConcreteProvenanceTypes();

        // Guards against the enumeration silently finding nothing, which would
        // make every assertion below vacuously true. Deliberately not a fixed
        // count: a provenance added later is denied by the loop below and
        // caught by the entitled set, so pinning the number would only fail a
        // legitimate change without proving anything the rest does not.
        Assert.NotEmpty(concrete);

        foreach (Type provenanceType in concrete)
        {
            bool[] answers = Variants(provenanceType)
                .Select(CoreLibraryIdentityTrust.MayMint)
                .Distinct()
                .ToArray();

            Assert.True(
                answers.Length == 1,
                $"{provenanceType.Name} is entitled for some field values and "
                + "denied for others. Core-library trust must follow the "
                + "acquisition, not what the acquisition happens to contain.");
        }

        Type[] entitled = concrete
            .Where(t => CoreLibraryIdentityTrust.MayMint(Variants(t)[0]))
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
    /// Builds one acquisition of the given type per field-value shape that a
    /// content-keyed rule could plausibly notice. <c>MayMint</c> must answer
    /// identically for all of them.
    /// <para>
    /// Each spelling is used three times, and each shape answers a question the
    /// others cannot. The **uniform** shape sets every field to the same value,
    /// which catches a rule comparing a field against a constant, such as
    /// <c>PackageAsset { PackageId: "System.Runtime" }</c>. The **distinct**
    /// shape gives every field a different value, which catches a rule
    /// comparing two fields to each other, such as
    /// <c>ProjectAsset p when p.Project != p.Tfm</c>. The **absent** shape
    /// passes <see langword="null"/> for every optional field, which catches a
    /// rule keyed on a field simply not being there, such as
    /// <c>ProjectAsset { Tfm: null }</c>.
    /// </para>
    /// <para>
    /// Each shape was added because a round found a rule the previous shapes
    /// could not distinguish — values alone in round 2, the relationship
    /// between values in round 3, and their absence in round 4. Several
    /// acquisitions carry optional fields (a project without a target
    /// framework or runtime identifier, a package without either), so
    /// <see langword="null"/> is an ordinary value here rather than an exotic
    /// one, and a rule that reads it is a plausible confusion rather than a
    /// contrived attack.
    /// </para>
    /// </summary>
    static AssemblyResolutionProvenance[] Variants(Type provenanceType) =>
        ProvenanceSpellings
            .SelectMany(spelling => new[]
            {
                Construct(provenanceType, (_, _) => spelling),
                Construct(
                    provenanceType,
                    (parameter, index) => $"{spelling}.{index}.{parameter.Name}"),
                Construct(
                    provenanceType,
                    (parameter, index) => IsOptional(parameter)
                        ? null
                        : $"{spelling}.{index}.{parameter.Name}"),
            })
            .ToArray();

    /// <summary>
    /// Whether the acquisition may be constructed without this field. Read
    /// from the declared nullability rather than from a list, so a field that
    /// becomes optional later is exercised as absent without anyone
    /// remembering to say so.
    /// </summary>
    static bool IsOptional(ParameterInfo parameter) =>
        new NullabilityInfoContext().Create(parameter).WriteState
            == NullabilityState.Nullable;

    /// <summary>
    /// Field values used to build each acquisition. The first is neutral; the
    /// rest are spellings a rule that wrongly keyed on content might single
    /// out, so that such a rule answers inconsistently and fails.
    /// </summary>
    static readonly string[] ProvenanceSpellings =
    [
        "probe",
        "System.Runtime",
        "System.Private.CoreLib",
        "Microsoft.NETCore.App",
    ];

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
    /// Builds a provenance of the given type, taking each argument from
    /// <paramref name="value"/>. The widest public constructor is used, so an
    /// acquisition that later gains a convenience overload is still exercised
    /// across all of its fields. Each constructor in this hierarchy takes only
    /// strings, and the required ones reject blank input, so any non-blank
    /// value satisfies all of them without the test needing to know which
    /// arguments a particular acquisition carries.
    /// </summary>
    static AssemblyResolutionProvenance Construct(
        Type provenanceType,
        Func<ParameterInfo, int, string?> value)
    {
        ConstructorInfo constructor = provenanceType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

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
