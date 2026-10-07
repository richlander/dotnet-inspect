using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using Xunit;

namespace ILInspector.Analysis.Tests;

// Gates for docs/design/platform-caller-unsafe-contracts.md.
public partial class LibraryBodyIndexTests
{
    static readonly UnsafeContractSource.PlatformProjection s_platformSource =
        PlatformCallerUnsafeContracts.Embedded.Source;

    static TypeRef PlatformType(string assembly, string ns, string name, bool trusted = true)
        => TypeRef.Definition(assembly, ns, name, trustedFrameworkAssembly: trusted);

    // Unsafe.As<T>(object), a projected member.
    static MemberRef UnsafeAsObject(TypeRef declaring)
        => new(
            declaring,
            "As",
            [TypeRef.CoreLib("System", "Object")],
            TypeRef.MethodGenericParameter(0),
            MemberKind.Method)
        {
            OpenParameterTypes = [TypeRef.CoreLib("System", "Object")],
            OpenReturnType = TypeRef.MethodGenericParameter(0),
            GenericArity = 1,
        };

    static DirectCall CallTo(MemberRef callee, CallerUnsafeMode? targetMode = null)
    {
        var caller = new MethodIdentity(
            "Probe",
            Guid.Empty,
            TypeRef.Definition("Probe", "Samples", "Caller"),
            "Run",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
        return new DirectCall(caller, callee, 0, 0x0A000001, 0x0A000001, CallKind.Call)
        {
            TargetCallerUnsafeMode = targetMode,
        };
    }

    [Fact]
    public void PlatformContractCallIsAnExplicitContractCall()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.Open(typeof(UnsafeEvidenceFixtures).Assembly.Location);

        UnsafeMemberUse use = Assert.Single(
            index.Safety.MemberUses,
            use => use.Method.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs));
        UnsafeMemberUseEvidence call = Assert.Single(use.Evidence);
        Assert.Equal(UnsafeMemberUseKind.ExplicitContractCall, call.Kind);
        Assert.Contains("Unsafe", call.Detail, StringComparison.Ordinal);
        Assert.Equal(s_platformSource, call.ContractSource);
        Assert.False(use.HasExplicitUnsafeContract);

        UnsafeMemberFinding finding = Assert.Single(
            index.Safety.MemberCensus.Members,
            finding => finding.Member.Name == nameof(UnsafeEvidenceFixtures.CallsUnsafeAs));
        UnsafeMemberFindingEvidence evidence = Assert.Single(finding.Evidence);
        Assert.Equal(s_platformSource, evidence.ContractSource);
    }

    [Fact]
    public void SameImageContractKeepsItsSource()
    {
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.Open(FixtureCatalog.DecompilerUnsafeNew.AssemblyPath());

        UnsafeMemberUse use = Assert.Single(
            index.Safety.MemberUses,
            use => use.Method.DeclaringType.Name == "MemorySafetySpellingFixture"
                && use.Method.Name == "CallPointerFreeUnsafeMethod");
        UnsafeMemberUseEvidence call = Assert.Single(
            use.Evidence,
            evidence => evidence.Kind == UnsafeMemberUseKind.ExplicitContractCall);
        Assert.Same(UnsafeContractSource.SameImage.Instance, call.ContractSource);
        Assert.All(
            index.Safety.MemberUses.SelectMany(static use => use.Evidence),
            evidence => Assert.Equal(
                evidence.Kind == UnsafeMemberUseKind.ExplicitContractCall,
                evidence.ContractSource is not null));
    }

    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("System.Private.CoreLib")]
    [InlineData("netstandard")]
    [InlineData("System.Memory")]
    public void PlatformContractMatchesAcrossForwarding(string assembly)
    {
        PlatformCallerUnsafeContracts platform = PlatformCallerUnsafeContracts.Embedded;
        Assert.True(platform.Contains(UnsafeAsObject(
            PlatformType(assembly, "System.Runtime.CompilerServices", "Unsafe"))));

        // MemoryMarshal.GetReference<T>(Span<T>)
        TypeRef span = PlatformType(assembly, "System", "Span`1");
        TypeRef openSpan = TypeRef.GenericInstance(span, [TypeRef.MethodGenericParameter(0)]);
        Assert.True(platform.Contains(new MemberRef(
            PlatformType(assembly, "System.Runtime.InteropServices", "MemoryMarshal"),
            "GetReference",
            [openSpan],
            TypeRef.ByRef(TypeRef.MethodGenericParameter(0)),
            MemberKind.Method)
        {
            OpenParameterTypes = [openSpan],
            GenericArity = 1,
        }));
    }

    [Fact]
    public void PlatformContractMatchesGenericDefinitions()
    {
        PlatformCallerUnsafeContracts platform = PlatformCallerUnsafeContracts.Embedded;

        // A MethodSpec instantiation keeps its open definition signature.
        MemberRef instantiated = UnsafeAsObject(
            PlatformType("System.Runtime", "System.Runtime.CompilerServices", "Unsafe")) with
        {
            TypeArguments = [TypeRef.CoreLib("System", "String")],
            ReturnType = TypeRef.CoreLib("System", "String"),
        };
        Assert.True(platform.Contains(instantiated));

        // new ReadOnlySpan<byte>(void*, int) on a constructed declaring type.
        TypeRef readOnlySpan = PlatformType("System.Runtime", "System", "ReadOnlySpan`1");
        TypeRef voidPointer = TypeRef.Pointer(TypeRef.CoreLib("System", "Void"));
        TypeRef int32 = TypeRef.CoreLib("System", "Int32");
        Assert.True(platform.Contains(new MemberRef(
            TypeRef.GenericInstance(readOnlySpan, [TypeRef.CoreLib("System", "Byte")]),
            ".ctor",
            [voidPointer, int32],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Constructor)
        {
            OpenParameterTypes = [voidPointer, int32],
            HasThis = true,
        }));
    }

    [Fact]
    public void NonPlatformLookalikeDoesNotMatch()
    {
        Assert.False(PlatformCallerUnsafeContracts.Embedded.Contains(UnsafeAsObject(
            PlatformType("System.Runtime", "System.Runtime.CompilerServices", "Unsafe", trusted: false))));
    }

    [Fact]
    public void UnprojectedPlatformMemberAdmitsNothing()
    {
        // Unsafe.SizeOf<T>() is a platform member upstream leaves unmarked.
        MemberRef sizeOf = new(
            PlatformType("System.Runtime", "System.Runtime.CompilerServices", "Unsafe"),
            "SizeOf",
            [],
            TypeRef.CoreLib("System", "Int32"),
            MemberKind.Method)
        {
            GenericArity = 1,
        };
        Assert.False(PlatformCallerUnsafeContracts.Embedded.Contains(sizeOf));
        Assert.Null(LibraryBodyAnalysisAccumulator.ExplicitCallContractSource(
            CallTo(sizeOf),
            primaryImageUsesUpdatedRules: false));
    }

    [Fact]
    public void UpdatedRulesImageIsAuthoritativeForItsMembers()
    {
        MemberRef unsafeAs = UnsafeAsObject(
            PlatformType("System.Private.CoreLib", "System.Runtime.CompilerServices", "Unsafe"));

        // Same-image, unmarked: the projection applies only to a legacy image.
        Assert.Null(LibraryBodyAnalysisAccumulator.ExplicitCallContractSource(
            CallTo(unsafeAs, CallerUnsafeMode.None),
            primaryImageUsesUpdatedRules: true));
        Assert.Equal(
            s_platformSource,
            LibraryBodyAnalysisAccumulator.ExplicitCallContractSource(
                CallTo(unsafeAs, CallerUnsafeMode.None),
                primaryImageUsesUpdatedRules: false));

        // A marked same-image callee is SameImage in either model, and a
        // cross-assembly platform callee is projected in either model.
        foreach (bool updated in new[] { true, false })
        {
            Assert.Same(
                UnsafeContractSource.SameImage.Instance,
                LibraryBodyAnalysisAccumulator.ExplicitCallContractSource(
                    CallTo(unsafeAs, CallerUnsafeMode.Explicit),
                    updated));
            Assert.Equal(
                s_platformSource,
                LibraryBodyAnalysisAccumulator.ExplicitCallContractSource(
                    CallTo(unsafeAs),
                    updated));
        }
    }

    [Fact]
    public void ProjectionAddsNoDeclarationRole()
    {
        // Inside .NET 11 CoreLib, Unsafe is same-image and unmarked: calls to it
        // are projected, but its own declarations gain no contract or finding.
        LibraryBodyAnalysisExecution index =
            BodyAnalysisTestExecution.Open(typeof(object).Assembly.Location);

        Assert.Contains(
            index.Safety.MemberUses.SelectMany(static use => use.Evidence),
            evidence => evidence.ContractSource is UnsafeContractSource.PlatformProjection
                && evidence.Detail.StartsWith("System.Runtime.CompilerServices.Unsafe", StringComparison.Ordinal));
        Assert.DoesNotContain(
            index.Safety.MemberUses,
            use => use.Method.DeclaringType.Name == "Unsafe"
                && use.Method.DeclaringType.Namespace == "System.Runtime.CompilerServices"
                && use.HasExplicitUnsafeContract);
        Assert.DoesNotContain(
            index.Safety.MemberCensus.Members,
            finding => finding.Member.DeclaringType.Name == "Unsafe"
                && finding.Member.DeclaringType.Namespace == "System.Runtime.CompilerServices"
                && finding.Member.Name == "As");
    }

    [Fact]
    public void ProjectionMatchesItsHeader()
    {
        PlatformCallerUnsafeContracts platform = PlatformCallerUnsafeContracts.Embedded;
        Assert.Equal("Microsoft.NETCore.App.Ref", platform.Source.PackId);
        Assert.True(platform.Count > 0);

        string text = ReadEmbeddedProjection();
        string tampered = text.Replace(
            "M:System.Runtime.CompilerServices.Unsafe.As``1(System.Object)",
            "M:System.Runtime.CompilerServices.Unsafe.As``1(System.String)",
            StringComparison.Ordinal);
        Assert.NotEqual(text, tampered);
        Assert.Throws<InvalidDataException>(() => PlatformCallerUnsafeContracts.Parse(tampered));
    }

    [Fact]
    public void ProjectionTracksPinnedSdk()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "global.json")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("global.json not found.");
        using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
        string sdk = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;

        // SDK feature band X.Y.Z00 carries runtime X.Y.Z with Z = patch % 100.
        int dash = sdk.IndexOf('-');
        string core = dash < 0 ? sdk : sdk[..dash];
        string[] parts = core.Split('.');
        string runtime = $"{parts[0]}.{parts[1]}.{int.Parse(parts[2]) % 100}" + (dash < 0 ? "" : sdk[dash..]);

        Assert.Equal(runtime, PlatformCallerUnsafeContracts.Embedded.Source.PackVersion);
    }

    [Fact]
    public void GenerationFailsOnUnreadableAssembly()
    {
        Assert.Throws<InvalidDataException>(() => PlatformCallerUnsafeProjectionBuilder.Build(
            "Microsoft.NETCore.App.Ref",
            "0.0.0",
            [("Broken.dll", [0x4D, 0x5A, 0x00, 0x01])]));
    }

    [Fact]
    public void GenerationFailsOnMarkerWithoutRules()
    {
        byte[] legacyMarked = BuildMemorySafetyContractImage(
            [],
            includePointerSignature: false,
            callTarget: MemorySafetyCallTarget.AttributeOnly);
        Assert.Throws<InvalidDataException>(() => PlatformCallerUnsafeProjectionBuilder.Build(
            "Microsoft.NETCore.App.Ref",
            "0.0.0",
            [("AnalysisMemorySafety.dll", legacyMarked)]));
    }

    [Fact]
    public void GenerationFailsOnCollidingEntries()
    {
        byte[] updatedMarked = BuildMemorySafetyContractImage(
            [2],
            includePointerSignature: false,
            callTarget: MemorySafetyCallTarget.AttributeOnly);
        string single = PlatformCallerUnsafeProjectionBuilder.Build(
            "Microsoft.NETCore.App.Ref",
            "0.0.0",
            [("AnalysisMemorySafety.dll", updatedMarked)]);
        Assert.True(PlatformCallerUnsafeContracts.Parse(single).Count > 0);

        Assert.Throws<InvalidDataException>(() => PlatformCallerUnsafeProjectionBuilder.Build(
            "Microsoft.NETCore.App.Ref",
            "0.0.0",
            [("AnalysisMemorySafety.dll", updatedMarked), ("Copy.dll", updatedMarked)]));
    }

    static string ReadEmbeddedProjection()
    {
        using Stream stream = typeof(PlatformCallerUnsafeContracts).Assembly
            .GetManifestResourceStream(PlatformCallerUnsafeContracts.ResourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
