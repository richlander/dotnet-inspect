using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml;

using DotnetInspector.Inspectors;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

/// <summary>
/// A caller that reaches the target only through a type-forwarding facade (#3419).
///
/// <para>Three gates stand between a scope directory and a rendered <c>Callers</c> row: the
/// assembly-level reverse-reference closure, the type-level prefilter, and the matcher. Each one
/// compares assembly spellings, and a caller that binds against a facade names the facade in every
/// one of them. Widening any two of the three leaves the caller dropped by the third, silently, so
/// these tests run the real <see cref="ApiMemberAnalysisInspection"/> wiring rather than any single
/// gate — a unit test of the matcher passes with the closure still discarding the assembly.</para>
///
/// <para>The artifacts are real. <c>System.Private.Xml</c> defines <c>System.Xml.XmlReader</c> and
/// the reference pack this test assembly compiled against exposes it as
/// <c>System.Xml.ReaderWriter</c>, so the call below emits a <c>TypeRef</c> naming a facade that
/// does not define the type — the exact shape every compiler produces for framework code, and one
/// no synthetic fixture would prove is real.</para>
/// </summary>
public class ForwardedCallerEdgeTests
{
    static string FrameworkDirectory => Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    static string SelfPath => typeof(ForwardedCallerEdgeTests).Assembly.Location;

    /// <summary>The caller under test. Its call site names <c>XmlReader</c> through the facade.</summary>
    internal static bool ReadThroughFacade(string path)
    {
        using var reader = XmlReader.Create(path);
        return reader.Read();
    }

    static string? PrivateXmlPath()
    {
        string path = Path.Combine(FrameworkDirectory, "System.Private.Xml.dll");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The token of <c>XmlReader.Create(string)</c> — selected by parameter type, because the
    /// overload the fixture calls is the only one an edge can point at and <c>Create</c> has a
    /// dozen siblings.
    /// </summary>
    static int CreateFromUriToken(string targetPath)
        => ILInspector.Analysis.LibraryBodyIndex.Open(targetPath).Methods
            .First(m => m.DeclaringType.Name == "XmlReader"
                && m.Name == "Create"
                && m.ParameterTypes.Length == 1
                && m.ParameterTypes[0].Name == "String")
            .MetadataToken;

    static ApiMemberAnalysisInspection CreateForCallers(string assemblyPath, IReadOnlyList<string> scope)
        => new(assemblyPath, [], new HashSet<string> { SectionNames.Callers }, scope, null);

    /// <summary>
    /// The premise. If this assembly ever stops naming the type through a facade — a reference-pack
    /// reshuffle would do it — the recovery test below would still pass while proving nothing, so
    /// the forwarding shape is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void ThisAssemblyNamesXmlReaderThroughAFacadeThatDoesNotDefineIt()
    {
        Assert.SkipWhen(PrivateXmlPath() is null, "System.Private.Xml not in the runtime directory.");

        using var stream = File.OpenRead(SelfPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        string? scope = null;
        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            if (reader.GetString(typeReference.Name) != "XmlReader"
                || reader.GetString(typeReference.Namespace) != "System.Xml"
                || typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
            {
                continue;
            }

            scope = reader.GetString(reader
                .GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope)
                .Name);
            break;
        }

        Assert.NotNull(scope);
        Assert.NotEqual("System.Private.Xml", scope);
    }

    [Fact]
    public void CallerEdges_ReportACallerThatNamesTheTargetOnlyThroughAFacade()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(target is null, "System.Private.Xml not in the runtime directory.");

        var edges = CreateForCallers(target!, [SelfPath])
            .CallerEdges(CreateFromUriToken(target!));

        Assert.Contains(edges, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
    }

    /// <summary>
    /// The negative control. Recovering the facade spelling must not make every overload match:
    /// <c>Create(Stream)</c> shares the declaring type, the facade, and the member name with the
    /// overload the fixture calls, and is never called by it.
    /// </summary>
    [Fact]
    public void CallerEdges_StillDiscriminateBetweenOverloadsOfTheFacadeSpelledType()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(target is null, "System.Private.Xml not in the runtime directory.");

        int createFromStream = ILInspector.Analysis.LibraryBodyIndex.Open(target!).Methods
            .First(m => m.DeclaringType.Name == "XmlReader"
                && m.Name == "Create"
                && m.ParameterTypes.Length == 1
                && m.ParameterTypes[0].Name == "Stream")
            .MetadataToken;

        var edges = CreateForCallers(target!, [SelfPath]).CallerEdges(createFromStream);

        Assert.DoesNotContain(edges, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
    }

    /// <summary>
    /// The gate for the assembly-level widening specifically. This scope survives none of the
    /// closure's seeds — this assembly does not reference <c>System.Private.Xml</c> at all — so a
    /// non-empty selection here can only come from the facade spelling being admitted.
    /// </summary>
    [Fact]
    public void DirectCallerScopes_SelectAnAssemblyThatOnlyNamesTheFacade()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(target is null, "System.Private.Xml not in the runtime directory.");

        var scopes = CreateForCallers(target!, [SelfPath])
            .DirectCallerScopes(CreateFromUriToken(target!));

        Assert.NotNull(scopes);
        Assert.NotEmpty(scopes);
    }

    /// <summary>
    /// Order independence. <c>DirectCallerScopes</c> reuses a scope another lens already opened, and
    /// that shared scope is selected by a closure seeded on the target's own assembly name — so it
    /// cannot contain an assembly that names the target only through a facade. Reusing it without
    /// widening returns pre-#3419 behavior, and hands the matcher no aliases, for any request that
    /// happens to resolve the shared scope first.
    ///
    /// <para>The CLI renders <c>Callers</c> before <c>Call Graph</c> today, so this is latent rather
    /// than user-visible. It is pinned anyway because nothing in the type system enforces that
    /// order, the failure is silent, and it is one section-ordering change away from shipping.</para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerEdges_FindTheForwardedCaller_EvenWhenAnotherLensResolvedTheScopeFirst(
        bool includeAllocations)
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(target is null, "System.Private.Xml not in the runtime directory.");

        var inspection = CreateForCallers(target!, [SelfPath]);

        // The poisoning step: resolves and caches the shared, un-widened scope.
        inspection.CallerScopes(includeAllocations);

        var edges = inspection.CallerEdges(CreateFromUriToken(target!));

        Assert.Contains(edges, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
    }

    /// <summary>
    /// Alias applicability is decided per caller image, from that image's own <c>AssemblyRef</c>
    /// identities — not assumed from the prefilter that selected it.
    ///
    /// <para>The distinction is the whole fix. An earlier revision checked strong-name identity
    /// inside <see cref="ILInspector.Analysis.CallerScopeTypeFilter"/> alone, which guards only the
    /// paths that run it. A scope carried over from an earlier lens is handed to the matcher
    /// without ever passing that filter, so a facade signed by one publisher could vouch for a
    /// caller that bound against a different assembly of the same name — a fabricated edge. Both
    /// reviewers of <c>4a811076</c> reached that hole independently.</para>
    ///
    /// <para>The caller here is real: this assembly's own call site, naming
    /// <c>System.Xml.ReaderWriter</c> with the token a compiler stamped on it. Only the
    /// <em>evidence</em> is synthetic — a same-named facade under a different key — so the test
    /// asks the question the attack asks: does a forwarder from the wrong assembly vouch for this
    /// caller? The scope is passed straight to <see cref="ILInspector.Analysis.MemberPattern"/> via
    /// <c>CallerEdges</c>, which is exactly the reused-shared-scope shape.</para>
    /// </summary>
    [Fact]
    public void CallerEdges_DoNotApplyAForwarderFromADifferentlySignedAssemblyOfTheSameName()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(target is null, "System.Private.Xml not in the runtime directory.");

        string directory = Path.Combine(
            Path.GetTempPath(), "fwd-impostor-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            WriteImpostorFacade(directory);

            var xmlReader = ILInspector.Analysis.TypeRef.Definition(
                "System.Private.Xml", "System.Xml", "XmlReader");
            var impostorAliases = ILInspector.Analysis.ForwardedTypeAliases.ForTarget(
                xmlReader, Directory.GetFiles(directory, "*.dll"));

            // Premise: the impostor really does supply an alias for the facade spelling, so the
            // rejection below is the identity check and not an empty alias set.
            Assert.True(impostorAliases.IncludesRawSpelling("System.Xml.ReaderWriter"));

            var targetSession = MethodBodyInspectionSession.Open(target!);
            var callerSession = MethodBodyInspectionSession.Open(SelfPath);

            var fabricated = targetSession.CallerEdges(
                CreateFromUriToken(target!), [callerSession], impostorAliases);

            Assert.DoesNotContain(
                fabricated, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));

            // The control: the same call, the same wiring, with the genuine framework evidence.
            // Without it, "reports nothing" would satisfy this test.
            var genuineAliases = ILInspector.Analysis.ForwardedTypeAliases.ForTarget(
                xmlReader, Directory.GetFiles(FrameworkDirectory, "*.dll"));

            var real = targetSession.CallerEdges(
                CreateFromUriToken(target!), [callerSession], genuineAliases);

            Assert.Contains(real, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A caller image that indexed successfully keeps its callers even if the file underneath it
    /// becomes unreadable afterwards.
    ///
    /// <para>The gate for answering identity from the bytes that were indexed. An earlier revision
    /// re-opened <c>BodyIndex.Path</c> to read the caller's <c>AssemblyRef</c> rows, so a file
    /// deleted, locked, or replaced between indexing and rendering silently produced zero forwarded
    /// edges — with every method body still in hand. Reported with an executed reproduction in
    /// review of <c>7181e795</c>.</para>
    ///
    /// <para>Deletion is the cheapest way to make the second read fail; the property under test is
    /// that there is no second read, not anything about deletion in particular.</para>
    /// </summary>
    [Fact]
    public void CallerEdges_SurviveTheCallerImageBecomingUnreadableAfterItWasIndexed()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(target is null, "System.Private.Xml not in the runtime directory.");

        string directory = Path.Combine(
            Path.GetTempPath(), "fwd-vanish-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            string copy = Path.Combine(directory, Path.GetFileName(SelfPath));
            File.Copy(SelfPath, copy);

            var xmlReader = ILInspector.Analysis.TypeRef.Definition(
                "System.Private.Xml", "System.Xml", "XmlReader");
            var aliases = ILInspector.Analysis.ForwardedTypeAliases.ForTarget(
                xmlReader, Directory.GetFiles(FrameworkDirectory, "*.dll"));

            var targetSession = MethodBodyInspectionSession.Open(target!);

            // Premise: the copy is a caller before anything is removed, so a later empty result is
            // the vanished file and not an artifact of indexing a copy.
            var control = targetSession.CallerEdges(
                CreateFromUriToken(target!), [MethodBodyInspectionSession.Open(copy)], aliases);
            Assert.Contains(control, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));

            var callerSession = MethodBodyInspectionSession.Open(copy);
            File.Delete(copy);

            var edges = targetSession.CallerEdges(
                CreateFromUriToken(target!), [callerSession], aliases);

            Assert.Contains(edges, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The reference version reaches identity verification through the <em>snapshot</em> taken at
    /// indexing time, not only through the prefilter's live <c>MetadataReader</c>.
    ///
    /// <para>The two paths are separate: <see cref="ILInspector.Analysis.CallerScopeTypeFilter"/>
    /// calls <c>RestrictedTo(MetadataReader)</c> while <c>CallerEdges</c> calls
    /// <c>RestrictedTo(ImmutableArray&lt;AssemblyReferenceSpelling&gt;)</c> over
    /// <c>LibraryBodyIndex.AssemblyReferences</c>. A scope carried over from another lens never
    /// runs the prefilter — that gap was the round-2 defect — so a version dropped on the way into
    /// the snapshot would be invisible to every prefilter test. Reported after mutating
    /// <c>CaptureAssemblyReferences</c> to record every reference as <c>0.0.0.0</c> and watching
    /// the suite stay green, in review of <c>984454a3</c>.</para>
    ///
    /// <para>The evidence is synthetic so its version can be placed below this assembly's real
    /// reference; the caller, the call site and the reference version are all real.</para>
    /// </summary>
    [Fact]
    public void CallerEdges_ConsultTheReferenceVersionCapturedAtIndexingTime()
    {
        string? target = PrivateXmlPath();
        Assert.SkipWhen(target is null, "System.Private.Xml not in the runtime directory.");

        string facade = Path.Combine(FrameworkDirectory, "System.Xml.ReaderWriter.dll");
        Assert.SkipWhen(!File.Exists(facade), "System.Xml.ReaderWriter not in the runtime directory.");

        byte[]? genuineKey = System.Reflection.AssemblyName.GetAssemblyName(facade).GetPublicKey();
        Assert.SkipWhen(genuineKey is not { Length: > 0 }, "The shipped facade is not strong-named.");

        Version? referenced = ReferencedFacadeVersion();
        Assert.SkipWhen(
            referenced is null || referenced.Major == 0,
            "This assembly does not reference the facade at a version a test can undercut.");

        var xmlReader = ILInspector.Analysis.TypeRef.Definition(
            "System.Private.Xml", "System.Xml", "XmlReader");

        string directory = Path.Combine(
            Path.GetTempPath(), "fwd-snapshot-" + Guid.NewGuid().ToString("n"));
        string control = Path.Combine(directory, "control");
        string below = Path.Combine(directory, "below");
        Directory.CreateDirectory(control);
        Directory.CreateDirectory(below);
        try
        {
            // Identical in every respect except the version: same spelling, same public key, same
            // forwarder. So the difference in the two answers is the version and nothing else.
            WriteFacade(control, genuineKey!, referenced!);
            WriteFacade(below, genuineKey!, new Version(referenced!.Major - 1, 0, 0, 0));

            var targetSession = MethodBodyInspectionSession.Open(target!);

            var accepted = targetSession.CallerEdges(
                CreateFromUriToken(target!),
                [MethodBodyInspectionSession.Open(SelfPath)],
                ILInspector.Analysis.ForwardedTypeAliases.ForTarget(
                    xmlReader, Directory.GetFiles(control, "*.dll")));

            // Premise: the wiring works at all under this synthetic evidence, so the absence below
            // is the version check rather than a facade the walk never accepted.
            Assert.Contains(accepted, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));

            var refused = targetSession.CallerEdges(
                CreateFromUriToken(target!),
                [MethodBodyInspectionSession.Open(SelfPath)],
                ILInspector.Analysis.ForwardedTypeAliases.ForTarget(
                    xmlReader, Directory.GetFiles(below, "*.dll")));

            Assert.DoesNotContain(refused, edge => edge.Call.Caller.Name == nameof(ReadThroughFacade));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The version this test assembly's own <c>AssemblyRef</c> row records for the facade — the
    /// value the snapshot must carry.
    /// </summary>
    static Version? ReferencedFacadeVersion()
    {
        using var reader = new PEReader(File.OpenRead(SelfPath));
        var metadata = reader.GetMetadataReader();
        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetAssemblyReference(handle);
            if (metadata.GetString(reference.Name) == "System.Xml.ReaderWriter")
                return reference.Version;
        }

        return null;
    }

    /// <summary>
    /// A strong-named assembly calling itself <c>System.Xml.ReaderWriter</c> and forwarding
    /// <c>System.Xml.XmlReader</c> to <c>System.Private.Xml</c>, exactly as the real facade does.
    /// Everything about it matches the genuine article except the key it is signed with.
    /// </summary>
    static void WriteImpostorFacade(string directory)
        => WriteFacade(directory, [.. Enumerable.Repeat((byte)0xC3, 16)], new Version(1, 0, 0, 0));

    /// <summary>
    /// The same, with the identity behind the spelling supplied by the caller, so a test can hold
    /// the forwarding behaviour fixed and vary one component of ECMA identity.
    /// </summary>
    static void WriteFacade(string directory, byte[] publicKey, Version version)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("System.Xml.ReaderWriter.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("System.Xml.ReaderWriter"),
            version,
            culture: default,
            publicKey: metadata.GetOrAddBlob(publicKey),
            flags: System.Reflection.AssemblyFlags.PublicKey,
            hashAlgorithm: System.Reflection.AssemblyHashAlgorithm.Sha1);

        var definer = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.Xml"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        metadata.AddExportedType(
            // tdForwarder (ECMA-335 II.23.1.15); not named in System.Reflection.TypeAttributes.
            (System.Reflection.TypeAttributes)0x00200000,
            metadata.GetOrAddString("System.Xml"),
            metadata.GetOrAddString("XmlReader"),
            definer,
            typeDefinitionId: 0);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        File.WriteAllBytes(
            Path.Combine(directory, "System.Xml.ReaderWriter.dll"), image.ToArray());
    }
}
