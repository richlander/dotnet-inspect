using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.ScalarFixtures;
using ILInspector.Metadata;

using tsbindgen;

namespace ILInspector.JsExportSurface.Tests;

/// <summary>
/// Product-outcome regressions for the generated-JSExport authentication
/// bypasses. Every negative here patches the IL bytes of a real
/// compiler-generated fixture, and every one is paired with the unpatched
/// control so a gate that stopped publishing anything would fail too.
/// </summary>
/// <remarks>
/// Patch sites are addressed through Analysis facts (call offsets, field-store
/// offsets, resolved literal offsets) rather than hard-coded byte searches, so
/// a recompiled fixture moves the patch with it instead of silently matching
/// nothing.
/// </remarks>
public sealed class GeneratedJsExportAuthenticationTests
{
    const byte Pop = 0x26;
    const byte LdNull = 0x14;
    const byte Nop = 0x00;
    const byte LdArg0 = 0x02;
    const byte Ret = 0x2A;
    const byte CastClass = 0x74;
    const byte LdcI4 = 0x20;
    const byte Call = 0x28;
    const byte StsFld = 0x80;
    const byte LdArg1 = 0x03;
    const byte LdLoc0 = 0x06;

    [Fact]
    public void Build_RejectsGeneratedRootGetterThatDiscardsTypeInfo()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        MethodIdentity getter = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == "get_WidgetDto"
                && method.DeclaringType.Name == "FixtureJsonContext");
        DirectCall getTypeInfo = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == getter.MetadataToken
                && call.Callee.Name == "GetTypeInfo");

        // The generated getter casts the JsonTypeInfo result and caches it.
        // Dropping the cast keeps every trusted call in place while the value
        // that reaches the cache field becomes null.
        PatchIl(
            image,
            getter.MetadataToken,
            getTypeInfo.ILOffset + 5,
            expected: [CastClass],
            replacement: [Pop, LdNull, Nop, Nop, Nop]);

        Assert.Contains(
            "no authentic source-generated implementation",
            BuildPatchedFixture(image, "root-getter-discards-type-info"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The generated getter merges a cached path and a fresh path at one
    /// shared <c>ret</c>. Proving the fresh value reaches the cache field does
    /// not prove it is what the getter hands back, so this replaces the load
    /// the fresh path returns with <c>null</c> and leaves every other link —
    /// the trusted calls, the cast, the cache store, the cached read — intact.
    /// </summary>
    [Fact]
    public void Build_RejectsGeneratedRootGetterThatReturnsNullOnTheFreshPath()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        MethodIdentity getter = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == "get_WidgetDto"
                && method.DeclaringType.Name == "FixtureJsonContext");
        FieldStoreFact cacheStore = Assert.Single(
            bodyIndex.FieldStores,
            store => store.EvidenceMethod.MetadataToken
                == getter.MetadataToken);

        // The cache store is a 5-byte stfld, and the generated fresh path
        // reloads the value it just cached immediately afterwards.
        PatchIl(
            image,
            getter.MetadataToken,
            cacheStore.ILOffset + 5,
            expected: [LdLoc0],
            replacement: [LdNull]);

        string message = BuildPatchedFixture(
            image,
            "root-getter-returns-null");
        Assert.Contains(
            "no authentic source-generated implementation",
            message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-vacuity for the return-flow gate: the patch above changes the
    /// getter's proven return alternatives from "cache field or trusted
    /// GetTypeInfo result" to "cache field or null", which is exactly the
    /// distinction <see cref="MethodReturnFlow"/> exists to make.
    /// </summary>
    [Fact]
    public void PatchedRootGetter_ReportsNullAsAProvenReturnAlternative()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        MethodIdentity getter = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == "get_WidgetDto"
                && method.DeclaringType.Name == "FixtureJsonContext");
        FieldStoreFact cacheStore = Assert.Single(
            bodyIndex.FieldStores,
            store => store.EvidenceMethod.MetadataToken
                == getter.MetadataToken);

        MethodReturnFlow authentic = RootGetterReturnFlow(bodyIndex);
        Assert.True(authentic.Value.IsResolved);
        Assert.Collection(
            authentic.Value.Sources.OrderBy(source => source.Kind),
            call => Assert.Equal(
                ResolvedValueSourceKind.CallResult,
                call.Kind),
            load =>
            {
                Assert.Equal(
                    ResolvedValueSourceKind.InstanceFieldLoad,
                    load.Kind);
                Assert.Equal(cacheStore.FieldName, load.Name);
            });

        PatchIl(
            image,
            getter.MetadataToken,
            cacheStore.ILOffset + 5,
            expected: [LdLoc0],
            replacement: [LdNull]);

        string path = Path.Combine(
            AppContext.BaseDirectory,
            $"jsexport-return-flow-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            MethodReturnFlow patched = RootGetterReturnFlow(
                OpenWireContractBodyIndex(path));
            Assert.True(patched.Value.IsResolved);
            Assert.Contains(
                patched.Value.Sources,
                source => source.Kind
                    == ResolvedValueSourceKind.NullReference);
            Assert.DoesNotContain(
                patched.Value.Sources,
                source => source.Kind
                    == ResolvedValueSourceKind.CallResult);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Authenticating what the static initializer hands to the generated
    /// context constructor says nothing about what the constructor does with
    /// it. This drops the forwarded options on the floor while every link the
    /// caller-side chain checks stays in place.
    /// </summary>
    [Fact]
    public void Build_RejectsGeneratedContextConstructorThatDropsOptions()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        MethodIdentity constructor = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == ".ctor"
                && method.DeclaringType.Name == "FixtureJsonContext"
                && method.ParameterTypes.Length == 1);
        DirectCall baseCall = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == constructor.MetadataToken
                && call.Callee.Name == ".ctor"
                && call.Callee.DeclaringType.Name
                    == "JsonSerializerContext");

        // The forwarded argument is the single byte immediately before the
        // base call.
        PatchIl(
            image,
            constructor.MetadataToken,
            baseCall.ILOffset - 1,
            expected: [LdArg1],
            replacement: [LdNull]);

        Assert.Contains(
            "no authentic source-generated implementation",
            BuildPatchedFixture(image, "context-ctor-drops-options"),
            StringComparison.Ordinal);
    }

    static MethodReturnFlow RootGetterReturnFlow(
        LibraryBodyIndex bodyIndex)
    {
        MethodIdentity getter = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == "get_WidgetDto"
                && method.DeclaringType.Name == "FixtureJsonContext");
        return Assert.Single(
            bodyIndex.ReturnFlows,
            flow => flow.EvidenceMethod.MetadataToken
                == getter.MetadataToken);
    }

    [Fact]
    public void Build_RejectsGeneratedContextWithUnlinkedDefaultInstance()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        MethodIdentity staticConstructor = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == ".cctor"
                && method.DeclaringType.Name == "FixtureJsonContext");
        FieldStoreFact instanceStore = Assert.Single(
            bodyIndex.FieldStores,
            store => store.EvidenceMethod.MetadataToken
                    == staticConstructor.MetadataToken
                && store.FieldName == "<Default>k__BackingField");

        // Every constructor the old shape counted is still called; only the
        // link from the constructed context to the field get_Default returns
        // is removed.
        PatchIl(
            image,
            staticConstructor.MetadataToken,
            instanceStore.ILOffset,
            expected: [StsFld],
            replacement: [Pop, Nop, Nop, Nop, Nop]);

        Assert.Contains(
            "no authentic source-generated implementation",
            BuildPatchedFixture(image, "unlinked-default-instance"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsUnreachableGeneratedWrapperEntry()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        MethodIdentity wrapper = PingWrapper(bodyIndex);

        // The wrapper keeps its call to the generated stub, but returns before
        // reaching it.
        PatchIl(
            image,
            wrapper.MetadataToken,
            ilOffset: 0,
            expected: [LdArg0],
            replacement: [Ret]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(image, "unreachable-wrapper-entry"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRegistrationWithMismatchedSignatureHash()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        DirectCall registration = PingRegistration(bodyIndex);
        ResolvedValueSource? hash = registration.ResolvedArgumentValues[1].Single;
        Assert.NotNull(hash);
        Assert.Equal(
            ResolvedValueSourceKind.Int32Literal,
            hash.Kind);
        Assert.True(
            RuntimeJsExportWrapperName.TryGetSignatureHash(
                PingWrapper(bodyIndex).Name,
                "Ping",
                out uint expected));
        Assert.Equal(expected, unchecked((uint)hash.Int32Value!));

        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            hash.ILOffset,
            expected: [LdcI4],
            replacement: [LdcI4, 0, 0, 0, 0]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(image, "mismatched-signature-hash"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDelegateRegistrationWithMismatchedSignatureHash()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        DirectCall registration =
            Registration(bodyIndex, "ReportValue");
        ResolvedValueSource hash =
            Assert.IsType<ResolvedValueSource>(
                registration.ResolvedArgumentValues[1].Single);

        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            hash.ILOffset,
            expected: [LdcI4],
            replacement: [LdcI4, 0, 0, 0, 0]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(
                image,
                "delegate-mismatched-signature-hash",
                "ReportValue"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDelegateRegistrationWithWrongNestedDescriptor()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        DirectCall registration =
            Registration(bodyIndex, "TransformValue");
        DirectCall functionFactory =
            DelegateDescriptorFactory(bodyIndex, registration);
        ResolvedValueSource stringDescriptor = Assert.IsType<ResolvedValueSource>(
            functionFactory.ResolvedArgumentValues[1].Single);
        int booleanMarshalerToken = MarshalerFactoryToken(
            bodyIndex,
            registration,
            "get_Boolean");

        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            stringDescriptor.ILOffset,
            expected: [Call],
            replacement:
            [
                Call,
                .. BitConverter.GetBytes(booleanMarshalerToken),
            ]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(
                image,
                "delegate-wrong-nested-descriptor",
                "TransformValue"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDelegateRegistrationWithWrongResultDescriptor()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        DirectCall registration =
            Registration(bodyIndex, "TransformValue");
        DirectCall functionFactory =
            DelegateDescriptorFactory(bodyIndex, registration);
        ResolvedValueSource resultDescriptor =
            Assert.IsType<ResolvedValueSource>(
                functionFactory.ResolvedArgumentValues[2].Single);
        int stringMarshalerToken = MarshalerFactoryToken(
            bodyIndex,
            registration,
            "get_String");

        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            resultDescriptor.ILOffset,
            expected: [Call],
            replacement:
            [
                Call,
                .. BitConverter.GetBytes(stringMarshalerToken),
            ]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(
                image,
                "delegate-wrong-result-descriptor",
                "TransformValue"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDelegateRegistrationWithWrongOuterFactory()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        DirectCall registration =
            Registration(bodyIndex, "TransformValue");
        DirectCall functionFactory =
            DelegateDescriptorFactory(bodyIndex, registration);
        DirectCall actionRegistration =
            Registration(bodyIndex, "ObserveValues");
        DirectCall actionFactory =
            DelegateDescriptorFactory(
                bodyIndex,
                actionRegistration);

        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            functionFactory.ILOffset,
            expected: [Call],
            replacement:
            [
                Call,
                .. BitConverter.GetBytes(actionFactory.OperandToken),
            ]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(
                image,
                "delegate-wrong-outer-factory",
                "TransformValue"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDelegateRegistrationWithReorderedDescriptors()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        DirectCall registration =
            Registration(bodyIndex, "TransformValue");
        DirectCall functionFactory =
            DelegateDescriptorFactory(bodyIndex, registration);
        ResolvedValueSource firstDescriptor = Assert.IsType<ResolvedValueSource>(
            functionFactory.ResolvedArgumentValues[0].Single);
        ResolvedValueSource secondDescriptor = Assert.IsType<ResolvedValueSource>(
            functionFactory.ResolvedArgumentValues[1].Single);
        int intMarshalerToken = MarshalerFactoryToken(
            bodyIndex,
            registration,
            "get_Int32");
        int stringMarshalerToken = MarshalerFactoryToken(
            bodyIndex,
            registration,
            "get_String");

        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            firstDescriptor.ILOffset,
            expected: [Call],
            replacement:
            [
                Call,
                .. BitConverter.GetBytes(stringMarshalerToken),
            ]);
        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            secondDescriptor.ILOffset,
            expected: [Call],
            replacement:
            [
                Call,
                .. BitConverter.GetBytes(intMarshalerToken),
            ]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(
                image,
                "delegate-reordered-descriptors",
                "TransformValue"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsDelegateWrapperThatCallsDifferentExport()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        MethodIdentity wrapper = Wrapper(bodyIndex, "ReportValue");
        DirectCall wrapperCall = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == wrapper.MetadataToken
                && call.CalleeDefinitionToken != 0);
        MethodIdentity stub = Assert.Single(
            bodyIndex.Methods,
            method => method.MetadataToken
                == wrapperCall.CalleeDefinitionToken);
        DirectCall exportCall = Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == stub.MetadataToken
                && call.Callee.Name == "ReportValue");
        MethodIdentity otherExport = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == "ReportValueAgain"
                && method.DeclaringType.Name == nameof(FixtureExports));

        PatchIl(
            image,
            stub.MetadataToken,
            exportCall.ILOffset,
            expected: [Call],
            replacement:
            [
                Call,
                .. BitConverter.GetBytes(otherExport.MetadataToken),
            ]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(
                image,
                "delegate-wrapper-different-export",
                "ReportValue"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsRegistrationWithSwappedDescriptorElement()
    {
        byte[] image = FixtureImage();
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(
            typeof(FixtureExports).Assembly.Location);
        DirectCall registration = PingRegistration(bodyIndex);
        SpanArgumentElements? descriptor =
            registration.SpanArgumentSources.ForArgument(2);
        Assert.NotNull(descriptor);
        Assert.True(descriptor.IsResolved);
        ResolvedValueSource? returnMarshaler =
            Assert.Single(descriptor.Elements).Single;
        Assert.NotNull(returnMarshaler);
        Assert.Equal("Task", returnMarshaler.Name);

        // The registration keeps its exact name, its exact hash and a resolved
        // one-element descriptor; only the marshaler the element holds stops
        // matching the export's own Task return.
        int stringMarshalerToken = Assert.Single(
            bodyIndex.DirectCalls
                .Where(call =>
                    call.EvidenceMethod.MetadataToken
                        == registration.EvidenceMethod.MetadataToken
                    && call.Callee.Name == "get_String"
                    && call.Callee.DeclaringType.Name == "JSMarshalerType")
                .Select(call => call.OperandToken)
                .Distinct());
        PatchIl(
            image,
            registration.EvidenceMethod.MetadataToken,
            returnMarshaler.ILOffset,
            expected: [Call],
            replacement: [Call, .. BitConverter.GetBytes(stringMarshalerToken)]);

        Assert.Contains(
            "no compiler-generated runtime wrapper",
            BuildPatchedFixture(image, "swapped-descriptor-element"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The .NET 11 shape that the old constructor-counting gate rejected: a
    /// user-written partial adds an unrelated static
    /// <c>JsonSerializerOptions</c>, so the generated <c>.cctor</c> contains a
    /// second default-options construction that belongs to nothing in the
    /// default-instance chain.
    /// </summary>
    [Fact]
    public void Build_AcceptsGeneratedContextWithUnrelatedStaticOptions()
    {
        string path =
            typeof(ScalarContextOptionsFixtureExports).Assembly.Location;
        LibraryBodyIndex bodyIndex = OpenWireContractBodyIndex(path);
        MethodIdentity staticConstructor = Assert.Single(
            bodyIndex.Methods,
            method => method.Name == ".cctor"
                && method.DeclaringType.Name
                    == nameof(ExtraStaticsScalarContext));
        Assert.Equal(
            2,
            bodyIndex.DirectCalls.Count(call =>
                call.EvidenceMethod.MetadataToken
                    == staticConstructor.MetadataToken
                && call.Kind == CallKind.NewObject
                && call.Callee.DeclaringType.Name == "JsonSerializerOptions"
                && call.Callee.ParameterTypes.IsEmpty));

        ApiSurface apiSurface = ExtractApiSurface(path);
        ApiType exports = Assert.Single(
            apiSurface.Types,
            type => type.Name
                == nameof(ScalarContextOptionsFixtureExports));
        ApiMember published = Assert.Single(
            exports.Members,
            member => member.Name
                == nameof(
                    ScalarContextOptionsFixtureExports
                        .SerializeExtraStaticsInt));
        foreach (ApiMember export in exports.Members.Where(
            member => member.HasRuntimeJsExport && member != published))
        {
            export.HasRuntimeJsExport = false;
            export.RuntimeJsExportAttributeCount = 0;
            export.HasMalformedRuntimeJsExportAttribute = false;
        }
        apiSurface.Types.RemoveAll(
            type => type.Name != nameof(ScalarContextOptionsFixtureExports)
                && type.Name != nameof(ExtraStaticsScalarContext));

        JsExportSurface surface = JsExportSurfaceBuilder.Build(
            apiSurface,
            bodyIndex);

        Assert.Equal(
            "int",
            Assert.Single(surface.Functions).ReturnWireType);
    }

    /// <summary>
    /// The command must read the assembly once and hand the same immutable
    /// image to metadata extraction and to body analysis. Two reads let a
    /// metadata surface be composed with bodies from different content that
    /// shares an MVID and token layout, which no downstream gate can detect.
    /// </summary>
    [Fact]
    public void TsBindGen_ReadsOneImageForMetadataAndBodyEvidence()
    {
        LibraryBodyIndex commandBodies = LibraryBodyIndex.Open(
            typeof(TsBindGenCommand).Assembly.Location,
            LibraryBodyAnalysisFeatures.JsonWireContractFlow);

        DirectCall read = Assert.Single(
            commandBodies.DirectCalls,
            call => call.Callee.Name == "ReadAllBytes"
                && call.Callee.DeclaringType.Name == "File");
        DirectCall metadataReader = Assert.Single(
            commandBodies.DirectCalls,
            call => call.Kind == CallKind.NewObject
                && call.Callee.Name == ".ctor"
                && call.Callee.DeclaringType.Name == "PEReader");
        DirectCall bodyReader = Assert.Single(
            commandBodies.DirectCalls,
            call => call.Callee.Name == "OpenFromPrefetchedImage"
                && call.Callee.DeclaringType.Name
                    == nameof(LibraryBodyIndex));
        Assert.Equal(
            read.EvidenceMethod.MetadataToken,
            metadataReader.EvidenceMethod.MetadataToken);
        Assert.Equal(
            read.EvidenceMethod.MetadataToken,
            bodyReader.EvidenceMethod.MetadataToken);
        Assert.DoesNotContain(
            commandBodies.DirectCalls,
            call => call.Callee.Name == "Open"
                && call.Callee.DeclaringType.Name
                    == nameof(LibraryBodyIndex));
        Assert.DoesNotContain(
            commandBodies.DirectCalls,
            call => call.Callee.Name == "OpenRead"
                && call.Callee.DeclaringType.Name == "File");
    }

    [Fact]
    public void Build_PublishesUnpatchedFixtureControl()
    {
        Assert.Contains(
            "Ping",
            BuildFixtureSurface().Functions.Select(
                function => function.Name));
    }

    static JsExportSurface BuildFixtureSurface()
    {
        string path = typeof(FixtureExports).Assembly.Location;
        return JsExportSurfaceBuilder.Build(
            ExtractApiSurface(path),
            OpenWireContractBodyIndex(path));
    }

    /// <summary>
    /// Builds a patched copy of the fixture assembly and returns the contained
    /// failure message, asserting that the unpatched control publishes first so
    /// the negative cannot pass by rejecting everything.
    /// </summary>
    static string BuildPatchedFixture(
        byte[] image,
        string name,
        string controlName = "Ping")
    {
        Assert.Contains(
            controlName,
            BuildFixtureSurface().Functions.Select(
                function => function.Name));

        string path = Path.Combine(
            AppContext.BaseDirectory,
            $"jsexport-{name}-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, image);
            UnsupportedJsExportSurfaceException exception =
                Assert.Throws<UnsupportedJsExportSurfaceException>(
                    () => JsExportSurfaceBuilder.Build(
                        ExtractApiSurface(path),
                        OpenWireContractBodyIndex(path)));
            return exception.Message;
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] FixtureImage()
        => File.ReadAllBytes(typeof(FixtureExports).Assembly.Location);

    static MethodIdentity PingWrapper(LibraryBodyIndex bodyIndex)
        => Wrapper(bodyIndex, "Ping");

    static DirectCall PingRegistration(LibraryBodyIndex bodyIndex)
        => Registration(bodyIndex, "Ping");

    static MethodIdentity Wrapper(
        LibraryBodyIndex bodyIndex,
        string exportName) =>
        Assert.Single(
            bodyIndex.Methods,
            method => method.Name.StartsWith(
                $"__Wrapper_{exportName}_",
                StringComparison.Ordinal));

    static DirectCall Registration(
        LibraryBodyIndex bodyIndex,
        string exportName) =>
        Assert.Single(
            bodyIndex.DirectCalls,
            call => call.Callee.Name == "BindManagedFunction"
                && call.FirstArgumentStringLiteral?.EndsWith(
                    $":{exportName}",
                    StringComparison.Ordinal) == true);

    static DirectCall DelegateDescriptorFactory(
        LibraryBodyIndex bodyIndex,
        DirectCall registration)
    {
        SpanArgumentElements descriptor = Assert.IsType<SpanArgumentElements>(
            registration.SpanArgumentSources.ForArgument(2));
        ResolvedValueSource delegateDescriptor =
            Assert.IsType<ResolvedValueSource>(
                Assert.Single(descriptor.Elements.Skip(1)).Single);
        return Assert.Single(
            bodyIndex.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                    == registration.EvidenceMethod.MetadataToken
                && call.ILOffset == delegateDescriptor.ILOffset);
    }

    static int MarshalerFactoryToken(
        LibraryBodyIndex bodyIndex,
        DirectCall registration,
        string factoryName) =>
        Assert.Single(
            bodyIndex.DirectCalls
                .Where(call =>
                    call.EvidenceMethod.MetadataToken
                        == registration.EvidenceMethod.MetadataToken
                    && call.Callee.Name == factoryName
                    && call.Callee.DeclaringType.Name
                        == "JSMarshalerType")
                .Select(call => call.OperandToken)
                .Distinct());

    /// <summary>
    /// Overwrites IL bytes in place, asserting the opcode found at the site is
    /// the one the patch was written against.
    /// </summary>
    static void PatchIl(
        byte[] image,
        int methodToken,
        int ilOffset,
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> replacement)
    {
        int start = MethodIlFileOffset(image, methodToken) + ilOffset;
        for (int index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], image[start + index]);
        replacement.CopyTo(image.AsSpan(start, replacement.Length));
    }

    static int MethodIlFileOffset(byte[] image, int methodToken)
    {
        using var peReader = new PEReader([.. image]);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinition definition = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.Handle(methodToken));
        int rva = definition.RelativeVirtualAddress;
        Assert.NotEqual(0, rva);
        int sectionIndex =
            peReader.PEHeaders.GetContainingSectionIndex(rva);
        Assert.InRange(
            sectionIndex,
            0,
            peReader.PEHeaders.SectionHeaders.Length - 1);
        SectionHeader section =
            peReader.PEHeaders.SectionHeaders[sectionIndex];
        int headerOffset =
            section.PointerToRawData + (rva - section.VirtualAddress);

        // ECMA-335 II.25.4: the two low bits select the tiny (1 byte) or fat
        // (4-byte-aligned, size in the high nibble of the flags word) header.
        const int TinyFormat = 0x02;
        return (image[headerOffset] & 0x03) == TinyFormat
            ? headerOffset + 1
            : headerOffset
                + ((image[headerOffset + 1] >> 4) * 4);
    }

    static ApiSurface ExtractApiSurface(string path)
    {
        ImmutableArray<byte> image = [.. File.ReadAllBytes(path)];
        using var peReader = new PEReader(image);
        return ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    static LibraryBodyIndex OpenWireContractBodyIndex(string path) =>
        LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.JsonWireContractFlow);
}
