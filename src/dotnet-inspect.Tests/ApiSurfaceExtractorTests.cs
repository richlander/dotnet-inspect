using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for API surface extraction, including signature formatting.
/// </summary>
public class ApiSurfaceExtractorTests
{
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

        var readOnlyStruct = surface.Types.FirstOrDefault(t => t.Name == "SampleReadOnlyStruct");
        Assert.NotNull(readOnlyStruct);
        Assert.True(readOnlyStruct.IsReadOnly);
        Assert.False(readOnlyStruct.IsByRefLike);

        var plainStruct = surface.Types.FirstOrDefault(t => t.Name == "SamplePlainStruct");
        Assert.NotNull(plainStruct);
        Assert.False(plainStruct.IsByRefLike);
        Assert.False(plainStruct.IsReadOnly);
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
        Assert.Contains("class", param.Constraints);
        Assert.Contains("System.IDisposable", param.Constraints);
        Assert.Contains("new()", param.Constraints);
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

}

/// <summary>
/// Sample class used for testing signature extraction.
/// </summary>
public class SampleClassForTesting
{
    public void MethodWithParameters(int count, string name) { }
    public void MethodWithNoParameters() { }
    public void GenericMethod<T>(T item) { }
    public void MethodWithStringDefault(string text = "a\"b\\c\n\u0001") { }
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

/// <summary>
/// Sample generic class implementing generic interfaces for testing interface extraction.
/// </summary>
public class SampleGenericClass<T> : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
}

/// <summary>
/// Sample generic class with class constraint for testing constraint extraction.
/// </summary>
public class SampleClassConstraint<T> where T : class
{
    public T? Value { get; set; }
}

/// <summary>
/// Sample generic class with struct constraint for testing constraint extraction.
/// </summary>
public class SampleStructConstraint<T> where T : struct
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
