using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.CSharp;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for API surface extraction, including signature formatting.
/// </summary>
public class ApiSurfaceExtractorTests
{
    static readonly CSharpFormatter Formatter = new();
    static readonly CSharpFormatter AttributeFormatter = new(
        new CSharpFormatOptions { IncludeCustomAttributes = true });

    [Fact]
    public void Extract_IncludesParameterNamesInMethodSignatures()
    {
        // Use the test assembly itself - we know its methods have parameter names
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        // Find a method with known parameters
        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "MethodWithParameters");
        Assert.NotNull(method);

        // Verify parameter names are included, not just types
        Assert.Contains("int count", method.Signature);
        Assert.Contains("string name", method.Signature);
    }

    [Fact]
    public void Extract_EscapesKeywordParameterNamesInMethodSignatures()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == nameof(SampleKeywordParameterHost));
        Assert.NotNull(testType);

        var instance = testType.Members.FirstOrDefault(m => m.Name == nameof(SampleKeywordParameterHost.Instance));
        Assert.NotNull(instance);
        Assert.Equal("int Instance(int @object, string @class)", instance.Signature);

        var staticMethod = testType.Members.FirstOrDefault(m => m.Name == nameof(SampleKeywordParameterHost.Static));
        Assert.NotNull(staticMethod);
        Assert.Equal("int Static(int @params, int @void)", staticMethod.Signature);
    }

    [Fact]
    public void Extract_PreservesRefReadonlyReturnInMethodSignatures()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == nameof(SampleRefReadonlyReturnHost));
        Assert.NotNull(testType);

        var readOnly = testType.Members.FirstOrDefault(m => m.Name == nameof(SampleRefReadonlyReturnHost.ChooseReadonly));
        Assert.NotNull(readOnly);
        Assert.Equal("ref readonly int ChooseReadonly(in int left, in int right, bool chooseLeft)", readOnly.Signature);

        var writable = testType.Members.FirstOrDefault(m => m.Name == nameof(SampleRefReadonlyReturnHost.ChooseWritable));
        Assert.NotNull(writable);
        Assert.Equal("ref int ChooseWritable(ref int left, ref int right, bool chooseLeft)", writable.Signature);
    }

    [Fact]
    public void Extract_SurfacesTopLevelInternalTypesOnlyUnderIncludeAll()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;

        using (var stream = File.OpenRead(assemblyPath))
        using (var peReader = new PEReader(stream))
        {
            var all = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            // --all surfaces the top-level internal type so its members are inspectable (#1300).
            var internalType = Assert.Single(
                all.Types,
                t => t.Name == nameof(InternalTopLevelSurfaceFixture));
            Assert.Equal("internal", internalType.Accessibility);
            Assert.StartsWith(
                "internal class InternalTopLevelSurfaceFixture",
                Formatter.FormatTypeDeclaration(internalType));
        }

        using (var stream = File.OpenRead(assemblyPath))
        using (var peReader = new PEReader(stream))
        {
            var publicOnly = ApiSurfaceExtractor.Extract(peReader, includeAll: false);
            // The default (public) surface is unchanged: internal types stay hidden.
            Assert.DoesNotContain(publicOnly.Types, t => t.Name == nameof(InternalTopLevelSurfaceFixture));
        }
    }

    [Fact]
    public void Extract_RecoversRefAndReadonlyStructModifiers()
    {
        // #1066: [IsByRefLike]/[IsReadOnly] are suppressed from the attribute list as
        // compiler-synthesized syntax, so the ref/readonly struct modifier must be
        // reconstructed onto the type model (and surfaced as a type modifier), not lost.
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var refStruct = surface.Types.FirstOrDefault(t => t.Name == "SampleRefStruct");
        Assert.NotNull(refStruct);
        Assert.Equal("struct", refStruct.Kind);
        Assert.True(refStruct.IsByRefLike);
        Assert.False(refStruct.IsReadOnly);
        var refStructDeclaration = Formatter.FormatTypeDeclaration(refStruct);
        Assert.Contains("ref struct SampleRefStruct", refStructDeclaration);
        Assert.DoesNotContain("CompilerFeatureRequired", refStructDeclaration);

        var readOnlyStruct = surface.Types.FirstOrDefault(t => t.Name == "SampleReadOnlyStruct");
        Assert.NotNull(readOnlyStruct);
        Assert.True(readOnlyStruct.IsReadOnly);
        Assert.False(readOnlyStruct.IsByRefLike);

        var plainStruct = surface.Types.FirstOrDefault(t => t.Name == "SamplePlainStruct");
        Assert.NotNull(plainStruct);
        Assert.False(plainStruct.IsByRefLike);
        Assert.False(plainStruct.IsReadOnly);

        var inlineArray = surface.Types.FirstOrDefault(t => t.Name == "SampleInlineBuffer");
        Assert.NotNull(inlineArray);
        var inlineArrayDeclaration = Formatter.FormatTypeDeclaration(inlineArray);
        Assert.DoesNotContain("InlineArray", inlineArrayDeclaration);
    }

    [Fact]
    public void Extract_HandlesMethodWithNoParameters()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "MethodWithNoParameters");
        Assert.NotNull(method);

        Assert.Contains("()", method.Signature);
    }

    [Fact]
    public void Extract_HandlesGenericMethodParameters()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "GenericMethod");
        Assert.NotNull(method);

        // Should have both the generic type and parameter name
        Assert.Contains("GenericMethod<T>", method.Signature);
        Assert.Contains("T item", method.Signature);
    }

    [Fact]
    public void Extract_EscapesStringParameterDefaults()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);
        var method = testType.Members.FirstOrDefault(m => m.Name == "MethodWithStringDefault");
        Assert.NotNull(method);

        Assert.Contains("string text = \"a\\\"b\\\\c\\n\\u0001\"", method.Signature);
        Assert.DoesNotContain("a\"b\\c", method.Signature);
    }

    [Fact]
    public void Extract_EscapesCharDefaultValuesInMethodSignatures()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "MethodWithCharDefaults");
        Assert.NotNull(method);

        var expected =
            """void MethodWithCharDefaults(char nul = '\0', """ +
            """char newline = '\n', """ +
            """char tab = '\t', """ +
            """char quote = '\'', """ +
            """char nonPrintable = '\u0001', """ +
            """char letter = 'A')""";
        Assert.Equal(expected, method.Signature);
    }

    [Fact]
    public void Extract_RendersDecimalAndDateTimeConstantDefaults()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var decimalMethod = testType.Members.FirstOrDefault(m => m.Name == "MethodWithDecimalDefault");
        Assert.NotNull(decimalMethod);
        Assert.Contains("System.Decimal amount = 1.5m", decimalMethod.Signature);

        var dateTimeMethod = testType.Members.FirstOrDefault(m => m.Name == "MethodWithDateTimeConstantDefault");
        Assert.NotNull(dateTimeMethod);
        Assert.Contains(
            "[System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when",
            dateTimeMethod.Signature);
        Assert.DoesNotContain("MethodWithDateTimeConstantDefault(System.DateTime when)", dateTimeMethod.Signature);
    }

    [Fact]
    public void Extract_RendersMarshalAsParameterAttributes()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "MethodWithMarshalAs");
        Assert.NotNull(method);
        Assert.NotNull(method.SignatureModel);
        var declaration = Formatter.FormatMember(testType, method);
        Assert.Contains(
            "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] int value",
            declaration);
        Assert.Contains(
            "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string text",
            declaration);
        Assert.Contains(
            "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeParamIndex = 2)] int[] values",
            declaration);
        Assert.Contains(
            "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeConst = 4)] int[] fixedValues",
            declaration);
        Assert.Contains(
            "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] int[] plainValues",
            declaration);
        Assert.Contains(
            "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 4)] int[] fixedPlainValues",
            declaration);
        Assert.Contains(
            "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 0)] int[] zeroSizedValues",
            declaration);
    }

    [Fact]
    public void Extract_RendersReturnParameterAttributes()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "MethodWithReturnAttributes");
        Assert.NotNull(method);
        Assert.NotNull(method.SignatureModel);
        Assert.DoesNotContain("[return:", method.Signature);
        var declaration = Formatter.FormatMember(testType, method);
        Assert.StartsWith(
            "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] public int MethodWithReturnAttributes()",
            declaration,
            StringComparison.Ordinal);

        var notNullMethod = testType.Members.FirstOrDefault(m => m.Name == "MethodWithReturnNotNull");
        Assert.NotNull(notNullMethod);
        var notNullDeclaration = Formatter.FormatMember(testType, notNullMethod);
        Assert.StartsWith(
            "[return: System.Diagnostics.CodeAnalysis.NotNull] public string MethodWithReturnNotNull()",
            notNullDeclaration,
            StringComparison.Ordinal);

        var fallbackMethod = testType.Members.FirstOrDefault(m => m.Name == "MethodWithReturnAttributesAndFallbackSignature");
        Assert.NotNull(fallbackMethod);
        Assert.DoesNotContain("[return:", fallbackMethod.Signature);
        var fallbackDeclaration = Formatter.FormatMember(testType, fallbackMethod);
        Assert.StartsWith(
            "[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] public int MethodWithReturnAttributesAndFallbackSignature(",
            fallbackDeclaration,
            StringComparison.Ordinal);
        Assert.Contains(
            "[System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when",
            fallbackDeclaration);
        Assert.DoesNotContain("public [return:", fallbackDeclaration);
    }

    [Fact]
    public void Extract_RendersPropertyAccessorReturnAttributes()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var textProperty = testType.Members.FirstOrDefault(m => m.Name == "PropertyWithReturnNotNull");
        Assert.NotNull(textProperty);
        var textDeclaration = Formatter.FormatMember(testType, textProperty);
        Assert.Contains("string PropertyWithReturnNotNull { [return: System.Diagnostics.CodeAnalysis.NotNull] get; }", textDeclaration);

        var numberProperty = testType.Members.FirstOrDefault(m => m.Name == "PropertyWithReturnMarshalAs");
        Assert.NotNull(numberProperty);
        var numberDeclaration = Formatter.FormatMember(testType, numberProperty);
        Assert.Contains("int PropertyWithReturnMarshalAs { [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] get; }", numberDeclaration);

        var indexer = testType.Members.FirstOrDefault(m => m.Name == "Item");
        Assert.NotNull(indexer);
        var indexerDeclaration = Formatter.FormatMember(testType, indexer);
        Assert.Contains("this[int index] { [return: System.Diagnostics.CodeAnalysis.NotNull] get; }", indexerDeclaration);
    }

    [Fact]
    public void Extract_RendersMemberAttributes()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "MethodWithMemberAttribute");
        Assert.NotNull(method);
        var methodDeclaration = AttributeFormatter.FormatMember(testType, method);
        Assert.StartsWith(
            "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic int MethodWithMemberAttribute()",
            methodDeclaration,
            StringComparison.Ordinal);

        var property = testType.Members.FirstOrDefault(m => m.Name == "PropertyWithMemberAttribute");
        Assert.NotNull(property);
        var propertyDeclaration = AttributeFormatter.FormatMember(testType, property);
        Assert.StartsWith(
            "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic string PropertyWithMemberAttribute",
            propertyDeclaration,
            StringComparison.Ordinal);

        var decimalField = testType.Members.FirstOrDefault(m => m.Name == "DecimalField");
        Assert.NotNull(decimalField);
        var decimalFieldDeclaration = AttributeFormatter.FormatMember(testType, decimalField);
        Assert.DoesNotContain("DecimalConstant", decimalFieldDeclaration);
        Assert.Contains("DecimalField", decimalFieldDeclaration);
    }

    [Fact]
    public void Extract_RendersTypeAttributes()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == nameof(SampleTypeAttributeHost));
        Assert.NotNull(testType);

        var declaration = AttributeFormatter.FormatTypeDeclaration(testType);
        Assert.StartsWith(
            "[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]\npublic class SampleTypeAttributeHost",
            declaration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_RendersEnumParameterDefaultsAsEnumLiterals()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == nameof(SampleEnumDefaultHost));
        Assert.NotNull(testType);

        var green = testType.Members.FirstOrDefault(m => m.Name == nameof(SampleEnumDefaultHost.Green));
        Assert.NotNull(green);
        Assert.Contains("DotnetInspector.Tests.SampleColor color = DotnetInspector.Tests.SampleColor.Green", green.Signature);

        var zero = testType.Members.FirstOrDefault(m => m.Name == nameof(SampleEnumDefaultHost.Zero));
        Assert.NotNull(zero);
        Assert.Contains("DotnetInspector.Tests.SampleColor color = DotnetInspector.Tests.SampleColor.Red", zero.Signature);

        var unnamed = testType.Members.FirstOrDefault(m => m.Name == nameof(SampleEnumDefaultHost.Unnamed));
        Assert.NotNull(unnamed);
        Assert.Contains("DotnetInspector.Tests.SampleFlags flags = (DotnetInspector.Tests.SampleFlags)3", unnamed.Signature);
    }

    [Fact]
    public void Extract_ShowsGenericInterfaceNamesWithTypeParameters()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleGenericClass`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.Interfaces);

        // Should show IEnumerable<T> not "(generic)" or "IEnumerable`1"
        Assert.Contains(testType.Interfaces, i => i.Contains("IEnumerable<T>"));
        
        // Should not contain "(generic)" placeholder
        Assert.DoesNotContain(testType.Interfaces, i => i.Contains("(generic)"));
    }

    [Fact]
    public void Extract_ExtractsClassConstraint()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassConstraint`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.TypeParameters);
        Assert.Single(testType.TypeParameters);
        
        var param = testType.TypeParameters[0];
        Assert.Equal("T", param.Name);
        Assert.Contains("class", param.Constraints);
    }

    [Fact]
    public void Extract_ExtractsStructConstraint()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleStructConstraint`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.TypeParameters);
        Assert.Single(testType.TypeParameters);
        
        var param = testType.TypeParameters[0];
        Assert.Equal("T", param.Name);
        Assert.Contains("struct", param.Constraints);
    }

    [Fact]
    public void Extract_ExtractsNewConstraint()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleNewConstraint`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.TypeParameters);
        Assert.Single(testType.TypeParameters);
        
        var param = testType.TypeParameters[0];
        Assert.Equal("T", param.Name);
        Assert.Contains("new()", param.Constraints);
    }

    [Fact]
    public void Extract_ExtractsInterfaceConstraint()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleInterfaceConstraint`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.TypeParameters);
        Assert.Single(testType.TypeParameters);
        
        var param = testType.TypeParameters[0];
        Assert.Equal("T", param.Name);
        Assert.Contains("System.IDisposable", param.Constraints);
    }

    [Fact]
    public void Extract_ExtractsMultipleConstraints()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleMultipleConstraints`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.TypeParameters);
        Assert.Single(testType.TypeParameters);
        
        var param = testType.TypeParameters[0];
        Assert.Equal("T", param.Name);
        Assert.Equal(["class", "System.IDisposable", "new()"], param.Constraints);
    }

    [Fact]
    public void Extract_DistinguishesNullableAwareGenericConstraintShapes()
    {
        using var stream = File.OpenRead(typeof(ApiSurfaceExtractorTests).Assembly.Location);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        AssertConstraints(surface, "SampleUnconstrainedConstraint`1", []);
        AssertConstraints(surface, "SampleNotNullConstraint`1", ["notnull"]);
        AssertConstraints(surface, "SampleClassConstraint`1", ["class"]);
        AssertConstraints(surface, "SampleClassNullableConstraint`1", ["class?"]);
        AssertConstraints(surface, "SampleStructConstraint`1", ["struct"]);
        AssertConstraints(surface, "SampleUnmanagedConstraint`1", ["unmanaged"]);
        AssertConstraints(surface, "SampleInterfaceConstraint`1", ["System.IDisposable"]);
        AssertConstraints(surface, "SampleInterfaceNullableConstraint`1", ["System.IDisposable?"]);
        AssertConstraints(surface, "SampleInterfaceNewConstraint`1", ["System.IDisposable", "new()"]);
        AssertConstraints(surface, "SampleNotNullInterfaceNewConstraint`1", ["notnull", "System.IDisposable", "new()"]);
    }

    [Fact]
    public void Extract_DistinguishesMixedGenericParameterNullableOverrides()
    {
        using var stream = File.OpenRead(typeof(ApiSurfaceExtractorTests).Assembly.Location);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleMixedNullableConstraints`4");
        Assert.NotNull(testType);
        Assert.Collection(testType.TypeParameters,
            p =>
            {
                Assert.Equal("TNotNull", p.Name);
                Assert.Equal(["notnull"], p.Constraints);
            },
            p =>
            {
                Assert.Equal("TUnconstrained", p.Name);
                Assert.Empty(p.Constraints);
            },
            p =>
            {
                Assert.Equal("TClass", p.Name);
                Assert.Equal(["class"], p.Constraints);
            },
            p =>
            {
                Assert.Equal("TClassNullable", p.Name);
                Assert.Equal(["class?"], p.Constraints);
            });
    }

    /// <summary>
    /// Nullable context bytes do not annotate a parameter carrying the metadata value-type
    /// constraint flag, whether it belongs to a method or type. Unconstrained parameters
    /// remain eligible. This is the extraction gate for issue #3729: removing the flag
    /// from <c>GenericContext</c> produces <c>Nullable&lt;T?&gt;</c>, <c>Handler&lt;T?&gt;</c>,
    /// and a spurious bare <c>T?</c>.
    /// </summary>
    [Fact]
    public void Extract_DoesNotApplyNullableAnnotationsToValueConstrainedParameters()
    {
        using var stream = File.OpenRead(typeof(ValueTypeNullabilityFixture).Assembly.Location);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(ValueTypeNullabilityFixture).FullName);
        var nullable = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(ValueTypeNullabilityFixture.NullableValue));
        var plain = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(ValueTypeNullabilityFixture.PlainValue));
        var open = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(ValueTypeNullabilityFixture.Open));

        Assert.Contains("System.Nullable<T> value", nullable.Signature, StringComparison.Ordinal);
        Assert.Contains("Handler<T> message", nullable.Signature, StringComparison.Ordinal);
        Assert.DoesNotContain("T?>", nullable.Signature, StringComparison.Ordinal);

        Assert.Contains("(T value,", plain.Signature, StringComparison.Ordinal);
        Assert.Contains("Handler<T> message", plain.Signature, StringComparison.Ordinal);
        Assert.DoesNotContain("T?", plain.Signature, StringComparison.Ordinal);
        Assert.Contains("Open<T>(T? value)", open.Signature, StringComparison.Ordinal);

        var valueContainer = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(ValueTypeNullabilityContainer<>).FullName);
        var valueField = Assert.Single(
            valueContainer.Members,
            candidate => candidate.Name == nameof(ValueTypeNullabilityContainer<int>.Value));
        var maybeField = Assert.Single(
            valueContainer.Members,
            candidate => candidate.Name == nameof(ValueTypeNullabilityContainer<int>.Maybe));
        Assert.Equal("T", valueField.ReturnType);
        Assert.Equal("System.Nullable<T>", maybeField.ReturnType);

        var openContainer = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(OpenNullabilityContainer<>).FullName);
        var openMaybeField = Assert.Single(
            openContainer.Members,
            candidate => candidate.Name == nameof(OpenNullabilityContainer<int>.Maybe));
        Assert.Equal("T?", openMaybeField.ReturnType);
    }

    /// <summary>
    /// The same rule applies to a declaring type's parameter. The compiler fixture above
    /// gates real method metadata; this synthesized seam case isolates a type-level
    /// <c>NullableContextAttribute(2)</c>, which causes <c>T?</c> and
    /// <c>Nullable&lt;T?&gt;</c> if <c>GenericContext.ForType</c> drops the constraint flag.
    /// </summary>
    [Fact]
    public void Extract_DoesNotApplyNullableContextToValueConstrainedTypeParameter()
    {
        string path = EmitValueConstrainedTypeNullableContextSample();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            var type = Assert.Single(
                surface.Types,
                candidate => candidate.Name.StartsWith(
                    "ValueConstrainedType",
                    StringComparison.Ordinal));
            var value = Assert.Single(type.Members, candidate => candidate.Name == "Value");
            var maybe = Assert.Single(type.Members, candidate => candidate.Name == "Maybe");

            Assert.Equal("T", value.ReturnType);
            Assert.Equal("System.Nullable<T>", maybe.ReturnType);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string EmitValueConstrainedTypeNullableContextSample()
    {
        var assembly = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName("ValueConstrainedTypeEmit"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ValueConstrainedTypeEmit");

        var attributeBuilder = module.DefineType(
            "System.Runtime.CompilerServices.NullableContextAttribute",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.Class
                | System.Reflection.TypeAttributes.Sealed,
            typeof(Attribute));
        var attributeConstructor = attributeBuilder.DefineConstructor(
            System.Reflection.MethodAttributes.Public,
            System.Reflection.CallingConventions.Standard,
            [typeof(byte)]);
        var attributeIl = attributeConstructor.GetILGenerator();
        attributeIl.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
        attributeIl.Emit(
            System.Reflection.Emit.OpCodes.Call,
            typeof(Attribute).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null)!);
        attributeIl.Emit(System.Reflection.Emit.OpCodes.Ret);
        var nullableContextAttribute = attributeBuilder.CreateType();

        var typeBuilder = module.DefineType(
            "ValueConstrainedType",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
        typeBuilder.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            nullableContextAttribute.GetConstructor([typeof(byte)])!,
            [(byte)2]));
        var parameter = Assert.Single(typeBuilder.DefineGenericParameters("T"));
        parameter.SetGenericParameterAttributes(
            System.Reflection.GenericParameterAttributes.NotNullableValueTypeConstraint
                | System.Reflection.GenericParameterAttributes.DefaultConstructorConstraint);
        parameter.SetBaseTypeConstraint(typeof(ValueType));
        typeBuilder.DefineField(
            "Value",
            parameter,
            System.Reflection.FieldAttributes.Public);
        typeBuilder.DefineField(
            "Maybe",
            typeof(Nullable<>).MakeGenericType(parameter),
            System.Reflection.FieldAttributes.Public);
        typeBuilder.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"value-constrained-type-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    [Fact]
    public void Extract_ExtractsCovariance()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "ISampleCovariant`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.TypeParameters);
        Assert.Single(testType.TypeParameters);
        
        var param = testType.TypeParameters[0];
        Assert.Equal("T", param.Name);
        Assert.Equal("out", param.Variance);
        Assert.Equal("out T", param.DisplayName);
    }

    [Fact]
    public void Extract_ExtractsContravariance()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "ISampleContravariant`1");
        Assert.NotNull(testType);
        Assert.NotNull(testType.TypeParameters);
        Assert.Single(testType.TypeParameters);
        
        var param = testType.TypeParameters[0];
        Assert.Equal("T", param.Name);
        Assert.Equal("in", param.Variance);
        Assert.Equal("in T", param.DisplayName);
    }

    [Fact]
    public void Extract_NonGenericTypeHasNoTypeParameters()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleClassForTesting");
        Assert.NotNull(testType);
        Assert.Empty(testType.TypeParameters);
    }

    [Fact]
    public void TypeParameter_ConstraintsSummary_ReturnsCommaSeparated()
    {
        var param = new TypeParameter
        {
            Name = "T",
            Constraints = ["class", "IDisposable", "new()"]
        };

        Assert.Equal("class, IDisposable, new()", param.ConstraintsSummary);
    }

    [Fact]
    public void TypeParameter_ConstraintsSummary_ReturnsNullWhenEmpty()
    {
        var param = new TypeParameter
        {
            Name = "T",
            Constraints = []
        };

        Assert.Null(param.ConstraintsSummary);
    }

    private static void AssertConstraints(ApiSurface surface, string typeName, string[] expected)
    {
        var testType = surface.Types.FirstOrDefault(t => t.Name == typeName);
        Assert.NotNull(testType);
        var param = Assert.Single(testType.TypeParameters);
        Assert.Equal(expected, param.Constraints);
    }

    [Fact]
    public void TypeParameter_DisplayName_IncludesVariance()
    {
        var param = new TypeParameter
        {
            Name = "T",
            Variance = "out"
        };

        Assert.Equal("out T", param.DisplayName);
    }

    [Fact]
    public void TypeParameter_DisplayName_OmitsVarianceWhenNull()
    {
        var param = new TypeParameter
        {
            Name = "T",
            Variance = null
        };

        Assert.Equal("T", param.DisplayName);
    }

    [Fact]
    public void PopulateDerivedTypes_FindsDerivedClasses()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var baseType = surface.Types.FirstOrDefault(t => t.Name == "SampleBaseClass");
        Assert.NotNull(baseType);

        ApiSurfaceExtractor.PopulateDerivedTypes(surface, baseType);

        Assert.NotNull(baseType.DerivedTypes);
        Assert.Contains("DotnetInspector.Tests.SampleDerivedClass", baseType.DerivedTypes);
        Assert.Contains("DotnetInspector.Tests.AnotherDerivedClass", baseType.DerivedTypes);
    }

    [Fact]
    public void PopulateDerivedTypes_FindsInterfaceImplementors()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var interfaceType = surface.Types.FirstOrDefault(t => t.Name == "ISampleInterface");
        Assert.NotNull(interfaceType);

        ApiSurfaceExtractor.PopulateDerivedTypes(surface, interfaceType);

        Assert.NotNull(interfaceType.DerivedTypes);
        Assert.Contains("DotnetInspector.Tests.SampleImplementation", interfaceType.DerivedTypes);
    }

    [Fact]
    public void PopulateDerivedTypes_ReturnsNullWhenNoDerivedTypes()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        // SampleDerivedClass has no derived types
        var leafType = surface.Types.FirstOrDefault(t => t.Name == "SampleDerivedClass");
        Assert.NotNull(leafType);

        ApiSurfaceExtractor.PopulateDerivedTypes(surface, leafType);

        Assert.Empty(leafType.DerivedTypes);
    }
    [Fact]
    public void Extract_ObsoleteMethod_VisibleByDefault_WithMessage()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        // includeAll: false — obsolete should still appear, but EditorBrowsable(Never) should not.
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleObsoleteHost");
        Assert.NotNull(testType);

        var obsoleteMethod = testType.Members.FirstOrDefault(m => m.Name == "OldMethod");
        Assert.NotNull(obsoleteMethod);
        Assert.True(obsoleteMethod.IsObsolete);
        Assert.Equal("Use NewMethod instead.", obsoleteMethod.ObsoleteMessage);

        var newMethod = testType.Members.FirstOrDefault(m => m.Name == "NewMethod");
        Assert.NotNull(newMethod);
        Assert.False(newMethod.IsObsolete);
        Assert.Null(newMethod.ObsoleteMessage);

        // EditorBrowsable(Never) is still filtered out by default.
        var hidden = testType.Members.FirstOrDefault(m => m.Name == "HiddenMethod");
        Assert.Null(hidden);
    }

    [Fact]
    public void Extract_ObsoleteMethodWithoutMessage_HasIsObsoleteTrue()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleObsoleteHost");
        Assert.NotNull(testType);

        var method = testType.Members.FirstOrDefault(m => m.Name == "OldMethodNoMessage");
        Assert.NotNull(method);
        Assert.True(method.IsObsolete);
        Assert.Null(method.ObsoleteMessage);
    }

    [Fact]
    public void Extract_RequiredMemberConstructor_IgnoresCompilerCompatibilityObsolete()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleRequiredHost");
        Assert.NotNull(testType);

        var constructor = testType.Members.FirstOrDefault(m => m.Kind == "constructor");
        Assert.NotNull(constructor);
        Assert.False(constructor.IsObsolete);
        Assert.Null(constructor.ObsoleteMessage);
    }

    [Fact]
    public void Extract_RequiredProperty_RendersRequiredModifier()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleRequiredHost");
        Assert.NotNull(testType);

        var property = testType.Members.FirstOrDefault(m => m.Name == "Active");
        Assert.NotNull(property);
        Assert.Equal("required bool Active { get; set; }", property.Signature);
    }

    [Fact]
    public void Extract_RefStruct_NotHiddenByCompilerCompatibilityObsolete()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: false);

        // Ref structs are stamped by Roslyn with a synthetic
        // [Obsolete("Types with embedded references are not supported...")] paired with
        // [CompilerFeatureRequired("RefStructs")]. That synthetic marker must not hide them.
        var refStruct = surface.Types.FirstOrDefault(t => t.Name == "SampleRefStruct");
        Assert.NotNull(refStruct);
    }

    [Fact]
    public void Extract_EditorBrowsableNever_StillVisibleWithIncludeAll()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var testType = surface.Types.FirstOrDefault(t => t.Name == "SampleObsoleteHost");
        Assert.NotNull(testType);

        var hidden = testType.Members.FirstOrDefault(m => m.Name == "HiddenMethod");
        Assert.NotNull(hidden);
        Assert.False(hidden.IsObsolete);
    }

    [Fact]
    public void Extract_FoldsFieldLikeEventBackingFieldIntoEvent()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        // includeAll so the private field-like backing field would be surfaced if not folded.
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var host = surface.Types.FirstOrDefault(t => t.Name == nameof(SampleFieldLikeEventHost));
        Assert.NotNull(host);

        var changedMembers = host.Members.Where(m => m.Name == "Changed").ToList();

        // A field-like event's backing field shares the event's name; it must not appear as a
        // separate field. Exactly one `Changed` member, and it is the event (never a field).
        Assert.Single(changedMembers);
        Assert.Equal("event", changedMembers[0].Kind);
        Assert.DoesNotContain(host.Members, m => m.Name == "Changed" && m.Kind == "field");
    }

    [Fact]
    public void Extract_KeepsCustomEventDistinctlyNamedBackingField()
    {
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var host = surface.Types.FirstOrDefault(t => t.Name == nameof(SampleCustomEventHost));
        Assert.NotNull(host);

        // A custom event with explicit accessors and a distinctly-named user field: the event
        // is surfaced, and its differently-named backing field is unaffected by the fold.
        Assert.Contains(host.Members, m => m.Name == "Custom" && m.Kind == "event");
        Assert.Contains(host.Members, m => m.Name == "_customBacking" && m.Kind == "field");
    }

    [Fact]
    public void Extract_KeepsPublicFieldMaskedBySameNamedNonFieldLikeEvent()
    {
        // C#/VB/F# forbid a same-named field+event (CS0102/BC30260/FS0023), but arbitrary IL
        // may contain both. Emit a type with a public field `Clash` alongside a private event
        // `Clash` whose accessors are NOT compiler-generated (a custom/explicit event backed by
        // a distinctly-named field). The field-like fold must NOT suppress the legitimate public
        // field: the event is not field-like, so the same-named field is not its backing field.
        string dllPath = EmitSameNameFieldAndCustomEvent(
            publicField: true, compilerGeneratedAccessors: false);
        try
        {
            var members = ExtractClashTypeMembers(dllPath);

            // The legitimate public field survives; the non-field-like event is faithfully
            // surfaced too (both members genuinely exist in this hand-authored type).
            Assert.Contains(members, m => m.Name == "Clash" && m.Kind == "field");
            Assert.Contains(members, m => m.Name == "Clash" && m.Kind == "event");
        }
        finally
        {
            try { File.Delete(dllPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Extract_KeepsFieldWhenCompilerGeneratedAdderBacksAnotherField()
    {
        // Sharper case: a [CompilerGenerated] accessor is not proof that a same-named field is
        // the event's backing storage. Emit a *private* field `Clash` (NOT [CompilerGenerated])
        // alongside a private event `Clash` whose add/remove ARE [CompilerGenerated] but back a
        // distinctly-named field (_eventBacking). This defeats both the accessibility guard and
        // the adder-attribute signal; only the candidate field's own [CompilerGenerated] marker
        // distinguishes a genuine backing field. The legitimate field must survive under --all.
        string dllPath = EmitSameNameFieldAndCustomEvent(
            publicField: false, compilerGeneratedAccessors: true);
        try
        {
            var members = ExtractClashTypeMembers(dllPath);

            Assert.Contains(members, m => m.Name == "Clash" && m.Kind == "field");
            Assert.Contains(members, m => m.Name == "Clash" && m.Kind == "event");
        }
        finally
        {
            try { File.Delete(dllPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Extract_ExcludesCompilerGeneratedTypesByDefault()
    {
        // The test assembly itself is a real csc artifact: SampleClosureHost's capturing lambda
        // forces the compiler to emit a nested `<>c__DisplayClass`. By default (opt-in off) no
        // compiler-generated type is surfaced, even under --all.
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        Assert.DoesNotContain(surface.Types, t => (t.MetadataName ?? "").Contains("DisplayClass"));
    }

    [Fact]
    public void Extract_SurfacesCompilerGeneratedTypesAndRealFieldsWhenOptedIn()
    {
        // With the opt-in, compiler-generated types are surfaced together with their genuine
        // fields (the display class carries the captured state), but synthesized auto-property
        // backing fields stay excluded everywhere — even under the opt-in.
        var assemblyPath = typeof(ApiSurfaceExtractorTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var surface = ApiSurfaceExtractor.Extract(
            peReader, includeAll: true, includeCompilerGenerated: true);

        // A display class from a real capturing lambda is surfaced with at least one field member.
        Assert.Contains(
            surface.Types,
            t => (t.MetadataName ?? "").Contains("DisplayClass")
                 && t.Members.Any(m => m.Kind == "field"));

        // Auto-property backing fields (<Prop>k__BackingField) are never surfaced as fields, even
        // once compiler-generated members are opted in.
        Assert.DoesNotContain(
            surface.Types.SelectMany(t => t.Members),
            m => m.Kind == "field" && m.Name.EndsWith("k__BackingField", StringComparison.Ordinal));
    }

    [Fact]
    public void SurfaceFieldHandles_ExcludesSynthesizedFieldsAndGatesCompilerGeneratedFields()
    {
        // Direct unit test of the shared field-inclusion primitive over a hand-authored type with
        // precisely-named fields. Synthetic metadata is appropriate here: the primitive is a pure
        // name/attribute filter, so exact field names isolate exactly what it decides.
        string dllPath = EmitFieldSurfaceSample();
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var typeDef = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(t => reader.GetString(t.Name) == "FieldSurfaceSample");
            var enumDef = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(t => reader.GetString(t.Name) == "FieldSurfaceEnum");

            var off = SurfaceFieldNames(reader, typeDef, includeCompilerGenerated: false);
            var on = SurfaceFieldNames(reader, typeDef, includeCompilerGenerated: true);

            foreach (var set in new[] { off, on })
            {
                // Ordinary fields are always surfaced, including a user field whose name merely
                // contains "__BackingField" (only the exact <Prop>k__BackingField shape is
                // synthetic) and a non-enum type's `value__` field (the storage-slot exclusion is
                // gated on enums, so on a plain class `value__` is an ordinary field).
                Assert.Contains("Plain", set);
                Assert.Contains("count__BackingField", set);
                Assert.Contains("value__", set);

                // A field-like event's private [CompilerGenerated] backing field is always folded.
                Assert.DoesNotContain("Evt", set);

                // A genuine auto-property backing field — [CompilerGenerated] AND matched by a
                // declared property of the same name (`Value`) — is dropped everywhere, even under
                // the opt-in.
                Assert.DoesNotContain("<Value>k__BackingField", set);
            }

            // Positive-evidence discriminator: <Orphan>k__BackingField is [CompilerGenerated] and
            // has the mangled backing-field name shape, but NO property named `Orphan` is declared,
            // so it backs no auto-property. It is preserved (surfaced as an ordinary
            // compiler-generated <...> field under the opt-in), matching the old RTS field surface:
            // the compiler-generated marker and mangled name alone are not enough; a matching
            // property is required to fold. A plain hoisted local behaves the same way.
            Assert.DoesNotContain("<Orphan>k__BackingField", off);
            Assert.Contains("<Orphan>k__BackingField", on);
            Assert.DoesNotContain("<hoisted>5__1", off);
            Assert.Contains("<hoisted>5__1", on);

            // Even with a matching property name, a candidate is folded only when its type and
            // staticness agree with the property and the accessor is [CompilerGenerated]. A type
            // mismatch (`string` field vs `int` property), a staticness mismatch (static field vs
            // instance property), and a non-auto property (hand-authored accessor) each decline the
            // fold, so the compiler-generated field is preserved rather than silently dropped.
            foreach (var preserved in new[]
                     {
                         "<Mismatch>k__BackingField",
                         "<Slot>k__BackingField",
                         "<Manual>k__BackingField",
                     })
            {
                Assert.DoesNotContain(preserved, off);
                Assert.Contains(preserved, on);
            }

            // On a genuine enum, `value__` is the storage slot and is excluded; literal members
            // still surface.
            var enumOff = SurfaceFieldNames(reader, enumDef, includeCompilerGenerated: false);
            Assert.DoesNotContain("value__", enumOff);
            Assert.Contains("Red", enumOff);
        }
        finally
        {
            try { File.Delete(dllPath); } catch { /* best-effort */ }
        }
    }

    static HashSet<string> SurfaceFieldNames(
        MetadataReader reader, TypeDefinition typeDef, bool includeCompilerGenerated)
        => ApiSurfaceExtractor
            .SurfaceFieldHandles(reader, typeDef, includeAll: true, includeCompilerGenerated)
            .Select(h => reader.GetString(reader.GetFieldDefinition(h).Name))
            .ToHashSet(StringComparer.Ordinal);

    // Emits (via Reflection.Emit) types that exercise every branch of the field-inclusion
    // primitive: an ordinary field, a user field that merely contains "__BackingField", a genuine
    // auto-property backing field ([CompilerGenerated], type/staticness-matched, [CompilerGenerated]
    // accessor), same-named lookalikes that decline the fold (type mismatch, staticness mismatch,
    // non-auto property, and no backing property), a non-enum `value__` field, a compiler-generated
    // hoisted local, and a field-like event's private [CompilerGenerated] backing field — plus a
    // genuine enum whose `value__` is the storage slot. Returns the saved path.
    static string EmitFieldSurfaceSample()
    {
        var ab = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName("FieldSurfaceEmit"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("FieldSurfaceEmit");
        var tb = module.DefineType("FieldSurfaceSample",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);

        var cgCtor = typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
            .GetConstructor(Type.EmptyTypes)!;
        var cgAttr = new System.Reflection.Emit.CustomAttributeBuilder(cgCtor, Array.Empty<object>());

        // Defines a `<PropertyName>k__BackingField` field (always [CompilerGenerated]) alongside a
        // matching property, letting a caller vary each fold discriminator independently: whether
        // the accessor is [CompilerGenerated] (auto-property signal), and whether the field's type
        // and staticness match the property. Only when every discriminator agrees is the field a
        // genuine auto-property backing field.
        void DefineBackingFieldLookalike(
            string propertyName,
            Type propertyType,
            Type fieldType,
            bool accessorCompilerGenerated,
            bool fieldStatic,
            bool accessorStatic)
        {
            var fieldAttrs = System.Reflection.FieldAttributes.Private;
            if (fieldStatic)
                fieldAttrs |= System.Reflection.FieldAttributes.Static;
            var backing = tb.DefineField($"<{propertyName}>k__BackingField", fieldType, fieldAttrs);
            backing.SetCustomAttribute(cgAttr);

            var getterAttrs = System.Reflection.MethodAttributes.Public
                | System.Reflection.MethodAttributes.SpecialName
                | System.Reflection.MethodAttributes.HideBySig;
            if (accessorStatic)
                getterAttrs |= System.Reflection.MethodAttributes.Static;
            var getter = tb.DefineMethod($"get_{propertyName}", getterAttrs, propertyType, Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(propertyType.IsValueType
                ? System.Reflection.Emit.OpCodes.Ldc_I4_0
                : System.Reflection.Emit.OpCodes.Ldnull);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            if (accessorCompilerGenerated)
                getter.SetCustomAttribute(cgAttr);

            var prop = tb.DefineProperty(
                propertyName, System.Reflection.PropertyAttributes.None, propertyType, null);
            prop.SetGetMethod(getter);
        }

        tb.DefineField("Plain", typeof(int), System.Reflection.FieldAttributes.Public);
        tb.DefineField("count__BackingField", typeof(int), System.Reflection.FieldAttributes.Public);

        // A genuine auto-property backing field: [CompilerGenerated] <Value>k__BackingField whose
        // type (int) and staticness (instance) match a declared auto-property `Value` with a
        // [CompilerGenerated] accessor. Every discriminator agrees, so it is folded.
        DefineBackingFieldLookalike(
            "Value", typeof(int), typeof(int),
            accessorCompilerGenerated: true, fieldStatic: false, accessorStatic: false);

        // Type mismatch: <Mismatch>k__BackingField is a [CompilerGenerated] `string` field while the
        // matching auto-property `Mismatch` is `int`. A field of a different type cannot be that
        // property's backing field, so it is preserved (old RTS compared field/property types).
        DefineBackingFieldLookalike(
            "Mismatch", typeof(int), typeof(string),
            accessorCompilerGenerated: true, fieldStatic: false, accessorStatic: false);

        // Staticness mismatch: <Slot>k__BackingField is a [CompilerGenerated] static field while the
        // matching auto-property `Slot` is an instance property. Staticness disagreement means it is
        // not that property's backing field, so it is preserved.
        DefineBackingFieldLookalike(
            "Slot", typeof(int), typeof(int),
            accessorCompilerGenerated: true, fieldStatic: true, accessorStatic: false);

        // Non-auto property: a hand-authored (non-[CompilerGenerated]) property `Manual` plus an
        // unrelated [CompilerGenerated] <Manual>k__BackingField. Because the property is not an
        // auto-property, the field backs nothing the compiler re-synthesizes, so it must be
        // preserved (its data would otherwise be silently lost).
        DefineBackingFieldLookalike(
            "Manual", typeof(int), typeof(int),
            accessorCompilerGenerated: false, fieldStatic: false, accessorStatic: false);

        // A [CompilerGenerated] field with the mangled backing-field name shape but NO declared
        // property `Orphan`: it backs no auto-property, so it must be preserved.
        var orphanBacking = tb.DefineField(
            "<Orphan>k__BackingField", typeof(int), System.Reflection.FieldAttributes.Private);
        orphanBacking.SetCustomAttribute(cgAttr);

        tb.DefineField("value__", typeof(int), System.Reflection.FieldAttributes.Public);
        tb.DefineField("<hoisted>5__1", typeof(int), System.Reflection.FieldAttributes.Public);

        // Field-like event: a private [CompilerGenerated] field sharing the event name, with
        // [CompilerGenerated] add/remove accessors.
        var evtField = tb.DefineField("Evt", typeof(Action), System.Reflection.FieldAttributes.Private);
        evtField.SetCustomAttribute(cgAttr);
        const System.Reflection.MethodAttributes accessorAttrs =
            System.Reflection.MethodAttributes.Private
            | System.Reflection.MethodAttributes.SpecialName
            | System.Reflection.MethodAttributes.HideBySig;
        var add = tb.DefineMethod("add_Evt", accessorAttrs, typeof(void), new[] { typeof(Action) });
        add.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        add.SetCustomAttribute(cgAttr);
        var remove = tb.DefineMethod("remove_Evt", accessorAttrs, typeof(void), new[] { typeof(Action) });
        remove.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        remove.SetCustomAttribute(cgAttr);
        var eventBuilder = tb.DefineEvent("Evt", System.Reflection.EventAttributes.None, typeof(Action));
        eventBuilder.SetAddOnMethod(add);
        eventBuilder.SetRemoveOnMethod(remove);

        tb.CreateType();

        // A genuine enum: its `value__` storage slot must be excluded; literals surface.
        var eb = module.DefineEnum(
            "FieldSurfaceEnum", System.Reflection.TypeAttributes.Public, typeof(int));
        eb.DefineLiteral("Red", 0);
        eb.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"field-surface-{Guid.NewGuid():N}.dll");
        ab.Save(path);
        return path;
    }

    static IReadOnlyList<ApiMember> ExtractClashTypeMembers(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        return surface.Types.Single(t => t.Name == "PublicFieldPrivateEvent").Members;
    }

    // Emits (via Reflection.Emit) a type carrying a field and a custom event that share the name
    // `Clash` — a shape valid in raw metadata but not producible by C#/VB/F#. The candidate field
    // is a plain int32 that is never [CompilerGenerated]; the event is always backed by a
    // distinctly-named `_eventBacking` field, so `Clash` is never a genuine backing field.
    // `compilerGeneratedAccessors` stamps the add/remove methods with [CompilerGenerated] to model
    // metadata that fakes the field-like accessor signal. Returns the persisted assembly path.
    static string EmitSameNameFieldAndCustomEvent(bool publicField, bool compilerGeneratedAccessors)
    {
        var ab = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new System.Reflection.AssemblyName("ClashEmit"), typeof(object).Assembly);
        var module = ab.DefineDynamicModule("ClashEmit");
        var tb = module.DefineType("PublicFieldPrivateEvent",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);

        var fieldVisibility = publicField
            ? System.Reflection.FieldAttributes.Public
            : System.Reflection.FieldAttributes.Private;
        tb.DefineField("Clash", typeof(int), fieldVisibility);
        tb.DefineField("_eventBacking", typeof(Action), System.Reflection.FieldAttributes.Private);

        const System.Reflection.MethodAttributes accessorAttrs =
            System.Reflection.MethodAttributes.Private
            | System.Reflection.MethodAttributes.SpecialName
            | System.Reflection.MethodAttributes.HideBySig;
        var add = tb.DefineMethod("add_Clash", accessorAttrs, typeof(void), new[] { typeof(Action) });
        add.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
        var remove = tb.DefineMethod("remove_Clash", accessorAttrs, typeof(void), new[] { typeof(Action) });
        remove.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);

        if (compilerGeneratedAccessors)
        {
            var ctor = typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
                .GetConstructor(Type.EmptyTypes)!;
            var attr = new System.Reflection.Emit.CustomAttributeBuilder(ctor, Array.Empty<object>());
            add.SetCustomAttribute(attr);
            remove.SetCustomAttribute(attr);
        }

        var eventBuilder = tb.DefineEvent("Clash", System.Reflection.EventAttributes.None, typeof(Action));
        eventBuilder.SetAddOnMethod(add);
        eventBuilder.SetRemoveOnMethod(remove);
        tb.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"clash-event-{Guid.NewGuid():N}.dll");
        ab.Save(path);
        return path;
    }

}

/// <summary>
/// Fixture: a capturing lambda that forces csc to emit a compiler-generated display class with
/// fields for the captured state, plus an auto-property whose backing field must stay excluded.
/// </summary>
public class SampleClosureHost
{
    public int AutoValue { get; set; }

    public System.Func<int> Capture(int seed)
    {
        int local = seed * 2;
        return () => seed + local;
    }
}

/// <summary>
/// Fixture: a field-like event whose compiler-generated backing field shares the event name.
/// </summary>
public class SampleFieldLikeEventHost
{
#pragma warning disable CS0067 // event is never used
    public event System.Action? Changed;
#pragma warning restore CS0067
}

/// <summary>
/// Fixture: a custom event with explicit accessors over a distinctly-named backing field.
/// </summary>
public class SampleCustomEventHost
{
    private System.Action? _customBacking;

    public event System.Action? Custom
    {
        add => _customBacking += value;
        remove => _customBacking -= value;
    }
}

/// <summary>
/// Sample class used for testing signature extraction.
/// </summary>
public class SampleClassForTesting
{
    public void MethodWithParameters(int count, string name) { }
    public void MethodWithNoParameters() { }
    public void GenericMethod<T>(T item) { }
    public void MethodWithCharDefaults(
        char nul = '\0',
        char newline = '\n',
        char tab = '\t',
        char quote = '\'',
        char nonPrintable = '\u0001',
        char letter = 'A') { }
    public void MethodWithDecimalDefault(decimal amount = 1.5m) { }
    public void MethodWithDateTimeConstantDefault(
        [System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when) { }
    public void MethodWithStringDefault(string text = "a\"b\\c\n\u0001") { }
    public void MethodWithMarshalAs(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)] int value,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string text,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeParamIndex = 2)] int[] values,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, ArraySubType = System.Runtime.InteropServices.UnmanagedType.I4, SizeConst = 4)] int[] fixedValues,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray)] int[] plainValues,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 4)] int[] fixedPlainValues,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPArray, SizeConst = 0)] int[] zeroSizedValues,
        int count) { }
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
    public int MethodWithReturnAttributes() => 42;
    [return: System.Diagnostics.CodeAnalysis.NotNull]
    public string MethodWithReturnNotNull() => "hello";
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
    public int MethodWithReturnAttributesAndFallbackSignature(
        [System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(637000000000000000L)] System.DateTime when) => when.Year;
    public string PropertyWithReturnNotNull
    {
        [return: System.Diagnostics.CodeAnalysis.NotNull]
        get => "hello";
    }

    public int PropertyWithReturnMarshalAs
    {
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]
        get => 42;
    }

    public string this[int index]
    {
        [return: System.Diagnostics.CodeAnalysis.NotNull]
        get => "hello";
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public int MethodWithMemberAttribute() => 42;

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public string PropertyWithMemberAttribute
    {
        get => "hello";
    }

    public const decimal DecimalField = 1.5m;
}

public class SampleKeywordParameterHost
{
    public int Instance(int @object, string @class) => @object + @class.Length;

    public static int Static(int @params, int @void) => @params + @void;
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class SampleTypeAttributeHost
{
    public int Method1() => 42;
}

public class SampleRefReadonlyReturnHost
{
    public static ref readonly int ChooseReadonly(in int left, in int right, bool chooseLeft)
    {
        if (chooseLeft)
            return ref left;

        return ref right;
    }

    public static ref int ChooseWritable(ref int left, ref int right, bool chooseLeft)
    {
        if (chooseLeft)
            return ref left;

        return ref right;
    }
}

public enum SampleColor
{
    Red = 0,
    Green = 1,
    Blue = 2
}

[Flags]
public enum SampleFlags
{
    One = 1,
    Two = 2
}

public class SampleEnumDefaultHost
{
    public void Green(SampleColor color = SampleColor.Green) { }
    public void Zero(SampleColor color = SampleColor.Red) { }
    public void Unnamed(SampleFlags flags = (SampleFlags)3) { }
}

/// <summary>
/// Sample class hosting Obsolete and EditorBrowsable(Never) members for testing.
/// </summary>
public class SampleObsoleteHost
{
    [Obsolete("Use NewMethod instead.")]
    public void OldMethod() { }

    [Obsolete]
    public void OldMethodNoMessage() { }

    public void NewMethod() { }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void HiddenMethod() { }
}

public class SampleRequiredHost
{
    public required bool Active { get; set; }
}

public ref struct SampleRefStruct
{
    public int Value;
}

public readonly struct SampleReadOnlyStruct
{
    public readonly int Value;
}

public struct SamplePlainStruct
{
    public int Value;
}

[System.Runtime.CompilerServices.InlineArray(4)]
public struct SampleInlineBuffer
{
    private int _element0;
}

/// <summary>
/// Sample generic class implementing generic interfaces for testing interface extraction.
/// </summary>
public class SampleGenericClass<T> : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
}

public class SampleUnconstrainedConstraint<T>
{
    public T? Maybe { get; set; }
}

public class SampleNotNullConstraint<T> where T : notnull
{
    public T Value { get; set; } = default!;
}

/// <summary>
/// Sample generic class with class constraint for testing constraint extraction.
/// </summary>
public class SampleClassConstraint<T> where T : class
{
    public T? Value { get; set; }
}

public class SampleClassNullableConstraint<T> where T : class?
{
    public T Value { get; set; } = default!;
}

/// <summary>
/// Sample generic class with struct constraint for testing constraint extraction.
/// </summary>
public class SampleStructConstraint<T> where T : struct
{
    public T Value { get; set; }
}

public class SampleUnmanagedConstraint<T> where T : unmanaged
{
    public T Value { get; set; }
}

/// <summary>
/// Sample generic class with new() constraint for testing constraint extraction.
/// </summary>
public class SampleNewConstraint<T> where T : new()
{
    public T Create() => new T();
}

/// <summary>
/// Sample generic class with interface constraint for testing constraint extraction.
/// </summary>
public class SampleInterfaceConstraint<T> where T : IDisposable
{
    public void Use(T item) => item.Dispose();
}

public class SampleInterfaceNullableConstraint<T> where T : IDisposable?
{
    public void Use(T? item) => item?.Dispose();
}

public class SampleInterfaceNewConstraint<T> where T : IDisposable, new()
{
    public T Create() => new();
}

public class SampleNotNullInterfaceNewConstraint<T> where T : notnull, IDisposable, new()
{
    public T Create() => new();
}

public class SampleMixedNullableConstraints<TNotNull, TUnconstrained, TClass, TClassNullable>
    where TNotNull : notnull
    where TClass : class
    where TClassNullable : class?
{
    public TNotNull Value { get; set; } = default!;
    public TUnconstrained? Maybe { get; set; }
    public TClass ClassValue { get; set; } = default!;
    public TClassNullable ClassMaybe { get; set; } = default!;
}

/// <summary>
/// Sample generic class with multiple constraints for testing constraint extraction.
/// </summary>
public class SampleMultipleConstraints<T> where T : class, IDisposable, new()
{
    public T Create() => new T();
}

/// <summary>
/// Sample covariant interface for testing variance extraction.
/// </summary>
public interface ISampleCovariant<out T>
{
    T Get();
}

/// <summary>
/// Sample contravariant interface for testing variance extraction.
/// </summary>
public interface ISampleContravariant<in T>
{
    void Set(T value);
}

/// <summary>
/// Base class for testing derived type detection.
/// </summary>
public abstract class SampleBaseClass
{
    public abstract void DoSomething();
}

/// <summary>
/// Derived class for testing derived type detection.
/// </summary>
public class SampleDerivedClass : SampleBaseClass
{
    public override void DoSomething() { }
}

/// <summary>
/// Another derived class for testing derived type detection.
/// </summary>
public class AnotherDerivedClass : SampleBaseClass
{
    public override void DoSomething() { }
}

/// <summary>
/// Interface for testing interface implementation detection.
/// </summary>
public interface ISampleInterface
{
    void Execute();
}

/// <summary>
/// Class implementing ISampleInterface for testing.
/// </summary>
public class SampleImplementation : ISampleInterface
{
    public void Execute() { }
}

/// <summary>
/// A top-level internal type used to verify that --all (includeAll) surfaces non-public
/// top-level types so their members are inspectable and gain Top Leverage selectors (#1300).
/// </summary>
internal class InternalTopLevelSurfaceFixture
{
    public int Value() => 1;
}
