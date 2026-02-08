using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for DiffCommand output formatting and comparison logic.
/// </summary>
public class DiffCommandTests
{
    [Fact]
    public void GetSimpleName_WithNamespace_ReturnsSimpleName()
    {
        var result = DiffCommand.GetSimpleName("System.Text.Json.JsonSerializer");
        Assert.Equal("JsonSerializer", result);
    }

    [Fact]
    public void GetSimpleName_WithoutNamespace_ReturnsSameName()
    {
        var result = DiffCommand.GetSimpleName("JsonSerializer");
        Assert.Equal("JsonSerializer", result);
    }

    [Fact]
    public void GetSimpleName_WithGenericType_ReturnsSimpleName()
    {
        var result = DiffCommand.GetSimpleName("System.Collections.Generic.List`1");
        Assert.Equal("List`1", result);
    }

    [Fact]
    public void GetTypeFullName_WithNamespace_ReturnsFullName()
    {
        var type = new ApiType { Name = "JsonSerializer", Namespace = "System.Text.Json" };
        var result = DiffCommand.GetTypeFullName(type);
        Assert.Equal("System.Text.Json.JsonSerializer", result);
    }

    [Fact]
    public void GetTypeFullName_WithoutNamespace_ReturnsName()
    {
        var type = new ApiType { Name = "MyType", Namespace = null };
        var result = DiffCommand.GetTypeFullName(type);
        Assert.Equal("MyType", result);
    }

    [Fact]
    public void GetTypeFullName_WithEmptyNamespace_ReturnsName()
    {
        var type = new ApiType { Name = "MyType", Namespace = "" };
        var result = DiffCommand.GetTypeFullName(type);
        Assert.Equal("MyType", result);
    }

    [Fact]
    public void CompareTypeMembers_WithNoChanges_ReturnsZeroCounts()
    {
        var fromType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" },
                new ApiMember { Name = "Method2", Signature = "int Method2(string s)" }
            ]
        };
        var toType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" },
                new ApiMember { Name = "Method2", Signature = "int Method2(string s)" }
            ]
        };

        var (added, removed, addedMembers, removedMembers) = DiffCommand.CompareTypeMembers(fromType, toType);

        Assert.Equal(0, added);
        Assert.Equal(0, removed);
        Assert.Empty(addedMembers);
        Assert.Empty(removedMembers);
    }

    [Fact]
    public void CompareTypeMembers_WithAddedMember_ReturnsAddedCount()
    {
        var fromType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" }
            ]
        };
        var toType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" },
                new ApiMember { Name = "Method2", Signature = "int Method2()" }
            ]
        };

        var (added, removed, addedMembers, removedMembers) = DiffCommand.CompareTypeMembers(fromType, toType);

        Assert.Equal(1, added);
        Assert.Equal(0, removed);
        Assert.Single(addedMembers);
        Assert.Contains("int Method2()", addedMembers);
    }

    [Fact]
    public void CompareTypeMembers_WithRemovedMember_ReturnsRemovedCount()
    {
        var fromType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" },
                new ApiMember { Name = "Method2", Signature = "int Method2()" }
            ]
        };
        var toType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" }
            ]
        };

        var (added, removed, addedMembers, removedMembers) = DiffCommand.CompareTypeMembers(fromType, toType);

        Assert.Equal(0, added);
        Assert.Equal(1, removed);
        Assert.Single(removedMembers);
        Assert.Contains("int Method2()", removedMembers);
    }

    [Fact]
    public void CompareTypeMembers_WithChangedSignature_ReturnsAddedAndRemoved()
    {
        var fromType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1(string s)" }
            ]
        };
        var toType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1(string s, int count)" }
            ]
        };

        var (added, removed, addedMembers, removedMembers) = DiffCommand.CompareTypeMembers(fromType, toType);

        Assert.Equal(1, added);
        Assert.Equal(1, removed);
        Assert.Contains("void Method1(string s, int count)", addedMembers);
        Assert.Contains("void Method1(string s)", removedMembers);
    }

    [Fact]
    public void CompareTypeMembers_FiltersCompilerGeneratedMembers()
    {
        var fromType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" },
                new ApiMember { Name = "<generated>", Signature = "void <generated>()" },
                new ApiMember { Name = "__internal", Signature = "void __internal()" }
            ]
        };
        var toType = new ApiType
        {
            Name = "Test",
            Members =
            [
                new ApiMember { Name = "Method1", Signature = "void Method1()" }
            ]
        };

        var (added, removed, addedMembers, removedMembers) = DiffCommand.CompareTypeMembers(fromType, toType);

        // Compiler-generated members should be filtered out
        Assert.Equal(0, added);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void CompareTypeMembers_WithEmptyMembers_ReturnsZeroCounts()
    {
        var fromType = new ApiType { Name = "Test", Members = [] };
        var toType = new ApiType { Name = "Test", Members = [] };

        var (added, removed, addedMembers, removedMembers) = DiffCommand.CompareTypeMembers(fromType, toType);

        Assert.Equal(0, added);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void CompareTypeMembers_WithNullMembers_ReturnsZeroCounts()
    {
        var fromType = new ApiType { Name = "Test", Members = null };
        var toType = new ApiType { Name = "Test", Members = null };

        var (added, removed, addedMembers, removedMembers) = DiffCommand.CompareTypeMembers(fromType, toType);

        Assert.Equal(0, added);
        Assert.Equal(0, removed);
    }
}
