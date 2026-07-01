using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

public class TypeSourceComposerUnionTests
{
    [Fact]
    public async Task LoweredUnionDeclaration_RendersUnionHeaderAndHidesBasicPattern()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;

                public interface IUnion
                {
                    object? Value { get; }
                }
            }

            namespace Other
            {
                public sealed class Bird { }
            }

            namespace UnionFixtures
            {
                using System;
                using System.Collections.Generic;

                public sealed class Cat { }
                public sealed class Dog { }
                public interface IMarker { }

                [System.Runtime.CompilerServices.Union]
                public readonly struct Pet : System.Runtime.CompilerServices.IUnion, IMarker
                {
                    public Pet(Cat value) => Value = value;
                    public Pet(Dog value) => Value = value;
                    public Pet(List<Other.Bird> value) => Value = value;

                    public object? Value { get; }

                    public string Describe() => Value?.ToString() ?? "";
                }
            }
            """);

        var source = ComposeType(assembly.Path, "UnionFixtures.Pet");

        Assert.Contains("using Other;", source);
        Assert.Contains("public readonly union Pet(Cat, Dog, List<Bird>) : IMarker", source);
        Assert.DoesNotContain("[Union", source);
        Assert.DoesNotContain("public struct Pet", source);
        Assert.DoesNotContain("IUnion", source);
        Assert.DoesNotContain("public Pet(Cat value)", source);
        Assert.DoesNotContain("public Pet(Dog value)", source);
        Assert.DoesNotContain("public Pet(List<Bird> value)", source);
        Assert.DoesNotContain("public object", source);
        Assert.Contains("Describe", source);
        await AssertSdkPreviewBuilds(source);
    }

    [Fact]
    public async Task RenderedUnionDeclaration_BuildsWithPreviewSdk()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;

                public interface IUnion
                {
                    object? Value { get; }
                }
            }

            namespace UnionFixtures
            {
                public sealed class Cat { }
                public sealed class Dog { }

                [System.Runtime.CompilerServices.Union]
                public struct Pet : System.Runtime.CompilerServices.IUnion
                {
                    public Pet(Cat value) => Value = value;
                    public Pet(Dog value) => Value = value;
                    public object? Value { get; }
                }
            }
            """);

        var source = ComposeType(assembly.Path, "UnionFixtures.Pet");

        await AssertSdkPreviewBuilds(source);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnionDeclaration_WithNonPublicExplicitConstructors_KeepsConstructorsOutOfCases()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures
            {
                public union Result(int, bool)
                {
                    internal Result(string message, int code)
                        : this(code)
                    {
                    }

                    private Result(double value)
                        : this((int)value)
                    {
                    }

                    public Result()
                        : this(true)
                    {
                    }
                }
            }
            """);

        var source = ComposeType(assembly.Path, "UnionFixtures.Result");

        Assert.Contains("public union Result(int, bool)", source);
        Assert.DoesNotContain("Result(int, bool, string", source);
        Assert.DoesNotContain("Result(int, bool, double", source);
        Assert.Contains("internal Result(string message, int code)", source);
        Assert.Contains("private Result(double value)", source);
        Assert.Contains("public Result() : this(true)", source);
        await AssertSdkPreviewBuilds(source);
    }

    [Fact]
    public void UnionAttributeWithoutIUnion_StaysLowered()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;
            }

            namespace UnionFixtures
            {
                public sealed class Cat { }

                [System.Runtime.CompilerServices.Union]
                public struct NotUnion
                {
                    public NotUnion(Cat value) => Value = value;
                    public object? Value { get; }
                }
            }
            """);

        var source = ComposeType(assembly.Path, "UnionFixtures.NotUnion");

        Assert.Contains("[Union]", source);
        Assert.Contains("public struct NotUnion", source);
        Assert.DoesNotContain("public union NotUnion", source);
    }

    [Fact]
    public void GlobalIUnionLookalike_StaysLowered()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;
            }

            public sealed class Cat { }

            public interface IUnion
            {
                object? Value { get; }
            }

            [System.Runtime.CompilerServices.Union]
            public struct NotRuntimeUnion : IUnion
            {
                public NotRuntimeUnion(Cat value) => Value = value;
                public object? Value { get; }
            }
            """);

        var source = ComposeType(assembly.Path, "NotRuntimeUnion");

        Assert.Contains("[Union]", source);
        Assert.Contains("public struct NotRuntimeUnion", source);
        Assert.DoesNotContain("public union NotRuntimeUnion", source);
    }

    [Fact]
    public void ByRefLikeUnionMetadata_StaysLowered()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;

                public interface IUnion
                {
                    object? Value { get; }
                }
            }

            namespace UnionFixtures
            {
                public sealed class Cat { }

                [System.Runtime.CompilerServices.Union]
                public ref struct RefPet : System.Runtime.CompilerServices.IUnion
                {
                    public RefPet(Cat value) => Value = value;
                    public object? Value { get; }
                }
            }
            """);

        var source = ComposeType(assembly.Path, "UnionFixtures.RefPet");

        Assert.Contains("public ref struct RefPet", source);
        Assert.DoesNotContain("public ref union RefPet", source);
    }

    [Fact]
    public async Task UnionMatchingTypeAndNullTests_RenderAgainstUnion()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { }
            public sealed class Dog { }
            public union Pet(Cat, Dog);

            public static class Matcher
            {
                public static bool IsCat(Pet pet) => pet is Cat;
            public static bool IsCatOrDog(Pet pet) => pet is Cat or Dog;
            public static bool IsNotNull(Pet pet) => pet is not null;
            public static bool IsNull(Pet pet) => pet is null;
            public static Cat? AsCat(Pet pet) => pet.Value as Cat;
        }
        """);

        Assert.Equal("return pet is Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCat"));
        Assert.Equal("return pet is Cat || pet is Dog;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCatOrDog"));
        Assert.Equal("return pet is not null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsNotNull"));
        Assert.Equal("return pet is null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsNull"));
        Assert.Equal("return pet.Value as Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "AsCat"));
    }

    [Fact]
    public async Task DirectUnionValuePattern_RendersPreferredUnionPattern()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures
            {
                public sealed class Cat { public int Age { get; } = 5; }
                public sealed class Dog { }
                public union Pet(Cat, Dog);

                public static class Matcher
                {
                    public static bool IsCat(Pet pet) => pet.Value is Cat;
                    public static bool IsNotCat(Pet pet) => pet is not Cat;
                    public static bool ValueIsNotCat(Pet pet) => pet.Value is not Cat;
                    public static bool ValueIsNotOlderCat(Pet pet) => pet.Value is not Cat { Age: > 3 };
                    public static bool ValueIsDogOrNull(Pet pet) => pet.Value is Dog or null;
                    public static bool ValueIsNotDogOrNull(Pet pet) => pet.Value is not (Dog or null);
                    public static bool IsNull(Pet pet) => pet.Value is null;
                    public static bool ValueIsNotNull(Pet pet) => pet.Value is not null;
                    public static string Describe(Pet pet) => pet.Value switch
                    {
                        Cat => "cat",
                        Dog => "dog",
                        _ => "other",
                    };
                    public static string SwitchNotCat(Pet pet) => pet switch
                    {
                        not Cat => "not cat",
                        Cat => "cat",
                    };
                    public static string ValueSwitchNotCat(Pet pet) => pet.Value switch
                    {
                        not Cat => "not cat",
                        Cat => "cat",
                    };
                    public static string ValueSwitchNotNull(Pet pet) => pet.Value switch
                    {
                        not null => "not null",
                        _ => "null",
                    };
                    public static string ValueSwitchNull(Pet pet) => pet.Value switch
                    {
                        null => "null",
                        Cat => "cat",
                        Dog => "dog",
                    };
                    public static string SwitchNull(Pet pet) => pet switch
                    {
                        null => "null",
                        Cat => "cat",
                        Dog => "dog",
                    };
                    public static string ValueSwitchNullDefault(Pet pet) => pet.Value switch
                    {
                        null => "null",
                        Cat => "cat",
                        _ => "other",
                    };
                    public static string ValueSwitchNotOlderCat(Pet pet) => pet.Value switch
                    {
                        not Cat { Age: > 3 } => "not older cat",
                        Cat => "cat",
                    };
                    public static string SwitchNotOlderCat(Pet pet) => pet switch
                    {
                        not Cat { Age: > 3 } => "not older cat",
                        Cat => "cat",
                    };
                }
            }
            """);

        Assert.Equal("return pet is Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCat"));
        Assert.Equal("return pet is not Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsNotCat"));
        Assert.Equal("return pet is not Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsNotCat"));
        Assert.Equal("return pet is not Cat { Age: > 3 };", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsNotOlderCat"));
        Assert.Equal("return pet is Dog || pet is null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsDogOrNull"));
        Assert.Equal("return !(pet is Dog || pet is null);", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsNotDogOrNull"));
        Assert.Equal("return pet is null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsNull"));
        Assert.Equal("return pet is not null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsNotNull"));
        Assert.Equal("""
            return pet switch
            {
                Cat => "cat",
                Dog => "dog",
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Describe"));
        Assert.Equal("""
            if (pet is not Cat)
            {
                return "not cat";
            }
            else
            {
                return "cat";
            }
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "SwitchNotCat"));
        Assert.Equal("""
            if (pet is not Cat)
            {
                return "not cat";
            }
            else
            {
                return "cat";
            }
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNotCat"));
        Assert.Equal("""
            if (pet is not null)
            {
                return "not null";
            }
            else
            {
                return "null";
            }
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNotNull"));
        Assert.Equal("""
            return pet switch
            {
                null => "null",
                Cat => "cat",
                Dog => "dog",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNull"));
        Assert.Equal("""
            return pet switch
            {
                null => "null",
                Cat => "cat",
                Dog => "dog",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "SwitchNull"));
        Assert.Equal("""
            return pet switch
            {
                null => "null",
                Cat => "cat",
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNullDefault"));
        Assert.Equal("""
            if (pet is not Cat { Age: > 3 })
            {
                return "not older cat";
            }
            else
            {
                return "cat";
            }
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNotOlderCat"));
        Assert.Equal("""
            if (pet is not Cat { Age: > 3 })
            {
                return "not older cat";
            }
            else
            {
                return "cat";
            }
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "SwitchNotOlderCat"));
    }

    [Fact]
    public async Task UnionDeclarationPattern_RendersAgainstUnion()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; public int Age { get; } = 5; }
            public sealed class Dog { }
            public union Pet(Cat, Dog);

            public static class Matcher
            {
                public static bool HasNamedCat(Pet pet) => pet is Cat cat && cat.Name.Length > 0;

                public static string IfDeclaration(Pet pet)
                {
                    if (pet is Cat cat)
                        return cat.Name;
                    return "none";
                }

                public static string IfGuardedDeclaration(Pet pet)
                {
                    if (pet is Cat cat && cat.Age > 3)
                        return cat.Name;
                    return "none";
                }

                public static string SwitchWithDefault(Pet pet) => pet switch
                {
                    Cat cat => cat.Name,
                    _ => "other",
                };
            }
            """);

        Assert.Equal("return pet is Cat cat && cat.Name.Length > 0;",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "HasNamedCat"));
        Assert.Contains("if (pet is Cat cat)", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IfDeclaration"));
        Assert.Contains("if (pet is Cat { Age: > 3 } cat)",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "IfGuardedDeclaration"));
        Assert.Equal("""
            if (pet is Cat cat)
            {
                return cat.Name;
            }
            else
            {
                return "other";
            }
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "SwitchWithDefault"));
    }

    [Fact]
    public async Task UnionConditionalReturn_RendersPatternConditional()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; public int Age { get; } = 5; }
            public sealed class Dog { public string Name { get; } = "dog"; }
            public union Pet(Cat, Dog);

            public static class Matcher
            {
                public static string Ternary(Pet pet) => pet is Cat cat ? cat.Name : "other";
                public static string TernaryGuard(Pet pet) => pet is Cat cat && cat.Age > 3 ? cat.Name : "other";
                public static int ConditionalNumber(Pet pet) => pet is Cat cat ? cat.Age : -1;

                public static string ManualFallbackUsesLocal(Pet pet)
                {
                    Cat cat = pet.Value as Cat;
                    if (cat is null)
                        return FormatNull(cat);
                    return cat.Name;
                }

                static string FormatNull(Cat? cat) => cat?.Name ?? "null";
            }
            """);

        Assert.Equal("return pet is Cat cat ? cat.Name : \"other\";",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "Ternary"));
        Assert.Equal("return pet is Cat { Age: > 3 } cat ? cat.Name : \"other\";",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "TernaryGuard"));
        Assert.Equal("return pet is Cat cat ? cat.Age : -1;",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "ConditionalNumber"));
        var manual = RenderMember(assembly.Path, "UnionFixtures.Matcher", "ManualFallbackUsesLocal");
        Assert.Contains("Cat cat = pet.Value as Cat;", manual);
        Assert.Contains("FormatNull(cat)", manual);
        Assert.DoesNotContain("pet is Cat cat ? cat.Name : FormatNull(cat)", manual);
    }

    [Fact]
    public async Task UnionSwitchExpression_RendersTypePatternArms()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; }
            public sealed class Dog { public string Name { get; } = "dog"; }
            public sealed class Bird { public string Name { get; } = "bird"; }
            public union Pet(Cat, Dog);
            public union Pet3(Cat, Dog, Bird);
            public union Box<T>(Cat, Dog, T);

            public static class Matcher
            {
                public static string Describe(Pet pet) => pet switch
                {
                    Cat cat => cat.Name,
                    Dog dog => dog.Name,
                };

                public static int Kind(Pet pet) => pet switch
                {
                    Cat => 1,
                    Dog => 2,
                };

                public static int KindWithOffset(Pet pet, int offset)
                {
                    int baseValue = offset + 1;
                    return pet switch
                    {
                        Cat => baseValue,
                        Dog dog => dog.Name.Length + baseValue,
                    };
                }

                public static string DescribeThree(Pet3 pet) => pet switch
                {
                    Cat cat => cat.Name,
                    Dog dog => dog.Name,
                    Bird bird => bird.Name,
                };

                public static int KindThree(Pet3 pet) => pet switch
                {
                    Cat => 1,
                    Dog => 2,
                    Bird => 3,
                };

                public static string Generic(Box<Bird> box) => box switch
                {
                    Cat cat => cat.Name,
                    Dog dog => dog.Name,
                    Bird bird => bird.Name,
                };
            }
            """);

        Assert.Equal("""
            return pet switch
            {
                Cat cat => cat.Name,
                Dog dog => dog.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Describe"));
        Assert.Equal("""
            return pet switch
            {
                Cat => 1,
                Dog => 2,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Kind"));
        var withOffset = RenderMember(assembly.Path, "UnionFixtures.Matcher", "KindWithOffset");
        Assert.Contains("int baseValue = offset + 1;", withOffset);
        Assert.Contains("return pet switch", withOffset);
        Assert.Contains("Dog dog => dog.Name.Length + baseValue", withOffset);
        Assert.Equal("""
            return pet switch
            {
                Cat cat => cat.Name,
                Dog dog => dog.Name,
                Bird bird => bird.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "DescribeThree"));
        Assert.Equal("""
            return pet switch
            {
                Cat => 1,
                Dog => 2,
                Bird => 3,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "KindThree"));
        Assert.Equal("""
            return box switch
            {
                Cat cat => cat.Name,
                Dog dog => dog.Name,
                Bird bird => bird.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Generic"));
    }

    [Fact]
    public async Task UnionSwitchExpression_RendersValueTypeCaseArms()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public union Result<T>(T, string);
            public union Result2(string, int);

            public static class Matcher
            {
                public static string Describe(Result<int> result) => result switch
                {
                    int value => value.ToString(),
                    string message => message,
                    null => "null",
                };

                public static string DescribeFinalValueType(Result2 result) => result switch
                {
                    string message => message,
                    int value => value.ToString(),
                    null => "null",
                };
            }
            """);

        Assert.Equal("""
            return result switch
            {
                null => "null",
                int value => value.ToString(),
                string message => message,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Describe"));
        Assert.Equal("""
            return result switch
            {
                null => "null",
                string message => message,
                int value => value.ToString(),
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "DescribeFinalValueType"));
    }

    [Fact]
    public async Task ClassUnionValueSwitchValueTypeCase_PreservesValueReceiver()
    {
        using var assembly = await CompileWithSdk("""
            using System.Runtime.CompilerServices;
            namespace UnionFixtures;

            [Union]
            public sealed class Result : IUnion
            {
                public Result(int value) => Value = value;
                public Result(string value) => Value = value;
                public object? Value { get; }
            }

            public static class Matcher
            {
                public static string Describe(Result result) => result.Value switch
                {
                    int value => value.ToString(),
                    string message => message,
                    null => "null",
                };
            }
            """);

        var source = RenderMember(assembly.Path, "UnionFixtures.Matcher", "Describe");

        Assert.Contains("result.Value", source);
        Assert.DoesNotContain("return result switch", source);
    }

    [Fact]
    public async Task UnionPatterns_RenderInAndByRefReceiverCanaries()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public int Age { get; } = 5; }
            public sealed class Dog { }
            public union Pet(Cat, Dog);

            public static class Matcher
            {
                public static bool InReceiver(in Pet pet) => pet is Cat { Age: > 3 };
                public static bool InValueReceiver(in Pet pet) => pet.Value is Cat { Age: > 3 };
                public static int InReceiverMixed(in Pet pet) => pet is Cat { Age: > 3 } ? pet.GetHashCode() : 0;

                public static bool RefLocalReceiver(Pet pet)
                {
                    ref readonly var alias = ref pet;
                    return alias is Dog;
                }
            }
            """);

        Assert.Equal("return pet is Cat { Age: > 3 };",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "InReceiver"));
        Assert.Equal("return pet is Cat { Age: > 3 };",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "InValueReceiver"));
        Assert.Equal("""
            Pet V_0 = pet;
            if (V_0 is not Cat { Age: > 3 })
            {
                return 0;
            }
            V_0 = pet;
            return V_0.GetHashCode();
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "InReceiverMixed"));
        Assert.Equal("""
            Pet V_0 = pet;
            return V_0 is Dog;
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "RefLocalReceiver"));
    }

    [Fact]
    public async Task UnionSwitchExpression_RendersGuardedPropertyPatternArms()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; public int Age { get; } = 5; }
            public sealed class Dog { public string Name { get; } = "dog"; public int Age { get; } = 7; }
            public union Pet(Cat, Dog);

            public static class Matcher
            {
                public static string DescribeByName(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string DescribeByAge(Pet pet) => pet switch
                {
                    Cat { Age: > 3 } cat => cat.Name,
                    Dog { Age: > 5 } dog => dog.Name,
                    _ => "other",
                };
            }
            """);

        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "DescribeByName"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Age > 3 => cat.Name,
                Dog dog when dog.Age > 5 => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "DescribeByAge"));
    }

    [Fact]
    public async Task UnionSwitchStatementReturnChain_RendersSwitchExpression()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; public int Age { get; } = 5; }
            public sealed class Dog { public string Name { get; } = "dog"; }
            public sealed class Bird { public string Name { get; } = "bird"; }
            public union Pet(Cat, Dog);
            public union Pet3(Cat, Dog, Bird);

            public static class Matcher
            {
                public static string Statement(Pet pet)
                {
                    switch (pet)
                    {
                        case Cat cat:
                            return cat.Name;
                        case Dog dog:
                            return dog.Name;
                    }
                    return "other";
                }

                public static string StatementDefault(Pet pet)
                {
                    switch (pet)
                    {
                        case Cat cat when cat.Age > 3:
                            return cat.Name;
                        case Dog dog:
                            return dog.Name;
                        default:
                            return "other";
                    }
                }

                public static string StatementDefaultSwapped(Pet pet)
                {
                    switch (pet)
                    {
                        case Dog dog:
                            return dog.Name;
                        case Cat cat when cat.Age > 3:
                            return cat.Name;
                        default:
                            return "other";
                    }
                }

                public static string StatementThree(Pet3 pet)
                {
                    switch (pet)
                    {
                        case Cat cat:
                            return cat.Name;
                        case Dog dog:
                            return dog.Name;
                        case Bird bird:
                            return bird.Name;
                    }
                    return "other";
                }
            }
            """);

        Assert.Equal("""
            return pet switch
            {
                Cat cat => cat.Name,
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Statement"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Age > 3 => cat.Name,
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "StatementDefault"));
        Assert.Equal("""
            return pet switch
            {
                Dog dog => dog.Name,
                Cat cat when cat.Age > 3 => cat.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "StatementDefaultSwapped"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat => cat.Name,
                Dog dog => dog.Name,
                Bird bird => bird.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "StatementThree"));
    }

    [Fact]
    public async Task UnionSwitchStatementStoreThenUse_RendersSwitchExpressionAssignment()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; public int Age { get; } = 5; }
            public sealed class Dog { public string Name { get; } = "dog"; }
            public sealed class Bird { public string Name { get; } = "bird"; }
            public union Pet(Cat, Dog, Bird);
            public union MaybeNumber(int, string);
            public union MaybeNumber2(string, int);

            public static class Matcher
            {
                public static string AssignThenUse(Pet pet)
                {
                    string result;
                    switch (pet)
                    {
                        case Cat cat:
                            result = cat.Name;
                            break;
                        case Dog dog:
                            result = dog.Name;
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    return result.ToUpperInvariant();
                }

                public static string AssignThenUseThree(Pet pet)
                {
                    string result;
                    switch (pet)
                    {
                        case Cat cat:
                            result = cat.Name;
                            break;
                        case Dog dog:
                            result = dog.Name;
                            break;
                        case Bird bird:
                            result = bird.Name;
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    return result.ToUpperInvariant();
                }

                public static string AssignThenMutate(Pet pet)
                {
                    string result;
                    switch (pet)
                    {
                        case Cat cat:
                            result = cat.Name;
                            break;
                        case Dog dog:
                            result = dog.Name;
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    result += "!";
                    return result;
                }

                public static string GuardedStaysFlat(Pet pet)
                {
                    string result;
                    switch (pet)
                    {
                        case Cat { Age: > 3 } cat:
                            result = cat.Name;
                            break;
                        case Dog dog:
                            result = dog.Name;
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    return result.ToUpperInvariant();
                }

                public static string ValueTypeCases(MaybeNumber value)
                {
                    string result;
                    switch (value)
                    {
                        case int number:
                            result = number.ToString();
                            break;
                        case string text:
                            result = text;
                            break;
                        case null:
                            result = "null";
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    return result.ToUpperInvariant();
                }

                public static string ValueTypeFinalExpression(MaybeNumber value)
                {
                    string result;
                    switch (value)
                    {
                        case int number:
                            result = number.ToString();
                            break;
                        case string text:
                            result = text.Trim();
                            break;
                        case null:
                            result = "null";
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    return result.ToUpperInvariant();
                }

                public static string ValueTypeMutate(MaybeNumber value)
                {
                    string result;
                    switch (value)
                    {
                        case int number:
                            result = number.ToString();
                            break;
                        case string text:
                            result = text;
                            break;
                        case null:
                            result = "null";
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    result += "!";
                    return result;
                }

                public static string ValueTypeFinalValueType(MaybeNumber2 value)
                {
                    string result;
                    switch (value)
                    {
                        case string text:
                            result = text.Trim();
                            break;
                        case int number:
                            result = number.ToString();
                            break;
                        case null:
                            result = "null";
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    return result.ToUpperInvariant();
                }

                public static string ValueSwitchNull(Pet pet)
                {
                    string result;
                    switch (pet.Value)
                    {
                        case null:
                            result = "null";
                            break;
                        case Cat cat:
                            result = cat.Name;
                            break;
                        case Dog dog:
                            result = dog.Name;
                            break;
                        default:
                            result = "other";
                            break;
                    }
                    return result.ToUpperInvariant();
                }
            }
            """);

        Assert.Equal("""
            string result = pet switch { Cat cat => cat.Name, Dog dog => dog.Name, _ => "other" };
            return result.ToUpperInvariant();
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "AssignThenUse"));
        Assert.Equal("""
            string result = pet switch { Cat cat => cat.Name, Dog dog => dog.Name, Bird bird => bird.Name, _ => "other" };
            return result.ToUpperInvariant();
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "AssignThenUseThree"));
        Assert.Equal("""
            string result = pet switch { Cat cat => cat.Name, Dog dog => dog.Name, _ => "other" };
            result = string.Concat(result, "!");
            return result;
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "AssignThenMutate"));

        var guarded = RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedStaysFlat");
        Assert.DoesNotContain("pet switch", guarded);
        Assert.Contains("cat.Age > 3", guarded);
        Assert.Equal("""
            string result = value switch { null => "null", int number => number.ToString(), string text => text, _ => "other" };
            return result.ToUpperInvariant();
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueTypeCases"));
        Assert.Equal("""
            string result = value switch { null => "null", int number => number.ToString(), string text => text.Trim(), _ => "other" };
            return result.ToUpperInvariant();
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueTypeFinalExpression"));
        Assert.Equal("""
            string result = value switch { null => "null", int number => number.ToString(), string text => text, _ => "other" };
            result = string.Concat(result, "!");
            return result;
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueTypeMutate"));
        Assert.Equal("""
            string result = value switch { null => "null", string text => text.Trim(), int number => number.ToString(), _ => "other" };
            return result.ToUpperInvariant();
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueTypeFinalValueType"));
        Assert.Equal("""
            string result = pet switch { null => "null", Cat cat => cat.Name, Dog dog => dog.Name, _ => "other" };
            return result.ToUpperInvariant();
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNull"));
    }

    [Fact]
    public async Task ClassUnionValueSwitchStoreThenUse_PreservesValueReceiver()
    {
        using var assembly = await CompileWithSdk("""
            using System.Runtime.CompilerServices;
            namespace UnionFixtures;

            [Union]
            public sealed class Result : IUnion
            {
                public Result(int value) => Value = value;
                public Result(string value) => Value = value;
                public object? Value { get; }
            }

            public static class Matcher
            {
                public static string AssignThenUse(Result result)
                {
                    string text;
                    switch (result.Value)
                    {
                        case int value:
                            text = value.ToString();
                            break;
                        case string message:
                            text = message;
                            break;
                        default:
                            text = "other";
                            break;
                    }
                    return text.ToUpperInvariant();
                }
            }
            """);

        var source = RenderMember(assembly.Path, "UnionFixtures.Matcher", "AssignThenUse");

        Assert.Contains("result.Value", source);
        Assert.DoesNotContain("result switch", source);
    }

    [Fact]
    public async Task UnionSwitchExpression_RendersSameTypeGuardedFallbackArms()
    {
        using var assembly = await CompileWithSdk("""
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; public int Age { get; } = 5; }
            public sealed class Dog { public string Name { get; } = "dog"; public int Age { get; } = 7; }
            public union Pet(Cat, Dog);

            public static class Matcher
            {
                public static string Defaulted(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat => "other cat",
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string DefaultedBound(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat otherCat => otherCat.Name.ToUpperInvariant(),
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string GuardedValueEqualsDefault(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => "other",
                    Cat => "z",
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string Inverted(Pet pet) => pet switch
                {
                    Cat { Age: <= 3 } => "young",
                    Cat => "old",
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string Exhaustive(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat => "other cat",
                    Dog dog => dog.Name,
                };

                public static string ExhaustiveBound(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat otherCat => otherCat.Name.ToUpperInvariant(),
                    Dog dog => dog.Name,
                };

                public static string ExhaustiveNoLocal(Pet pet) => pet switch
                {
                    Cat { Age: <= 3 } => "young",
                    Cat => "old",
                    Dog dog => dog.Name,
                };
            }
            """);

        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Cat => "other cat",
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Defaulted"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Cat cat => cat.Name.ToUpperInvariant(),
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "DefaultedBound"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when !(cat.Name == "cat") => "z",
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedValueEqualsDefault"));
        Assert.Equal("""
            return pet switch
            {
                Cat V_3 when V_3.Age <= 3 => "young",
                Cat => "old",
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Inverted"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Cat => "other cat",
                Dog dog => dog.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Exhaustive"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Cat cat => cat.Name.ToUpperInvariant(),
                Dog dog => dog.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ExhaustiveBound"));
        Assert.Equal("""
            return pet switch
            {
                Cat V_3 when V_3.Age <= 3 => "young",
                Cat => "old",
                Dog dog => dog.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ExhaustiveNoLocal"));
    }

    [Fact]
    public async Task ClassUnionSwitchExpression_RendersTypePatternArms()
    {
        using var assembly = await CompileWithSdk("""
            using System.Runtime.CompilerServices;
            namespace UnionFixtures;

            public sealed class Cat { public string Name { get; } = "cat"; }
            public sealed class Dog { public string Name { get; } = "dog"; }

            [Union]
            public sealed class Pet : IUnion
            {
                public Pet(Cat value) => Value = value;
                public Pet(Dog value) => Value = value;
                public object? Value { get; }
            }

            public static class Matcher
            {
                public static string Describe(Pet pet) => pet switch
                {
                    Cat cat => cat.Name,
                    Dog dog => dog.Name,
                };

                public static int Kind(Pet pet) => pet switch
                {
                    Cat => 1,
                    Dog => 2,
                };

                public static string GuardedDefault(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string GuardedDefaultBound(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat otherCat => otherCat.Name.ToUpperInvariant(),
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string GuardedDefaultBoundSameAsDefault(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat otherCat => "other",
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static string GuardedExhaustive(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat => "other cat",
                    Dog dog => dog.Name,
                };

                public static string GuardedExhaustiveBound(Pet pet) => pet switch
                {
                    Cat { Name: "cat" } cat => cat.Name,
                    Cat otherCat => otherCat.Name.ToUpperInvariant(),
                    Dog dog => dog.Name,
                };

                public static string Ternary(Pet pet) => pet is Cat cat ? cat.Name : "other";

                public static string TernaryGuard(Pet pet) => pet is Cat cat && cat.Name == "cat" ? cat.Name : "other";

                public static bool IsCatOrDog(Pet pet) => pet is Cat or Dog;

                public static bool IsNotCat(Pet pet) => pet is not Cat;
                public static bool ValueIsNotCat(Pet pet) => pet.Value is not Cat;
                public static bool ValueIsNotNamedCat(Pet pet) => pet.Value is not Cat { Name: "cat" };
                public static bool ValueIsDogOrNull(Pet pet) => pet.Value is Dog or null;
                public static bool ValueIsNotNull(Pet pet) => pet.Value is not null;

                public static bool IsCatWithName(Pet pet) => pet is Cat { Name: "cat" };
                public static bool IsCatWithOtherName(Pet pet) => pet is Cat { Name: not "dog" };

                public static string ValueSwitchNotNull(Pet pet) => pet.Value switch
                {
                    not null => "not null",
                    _ => "null",
                };
                public static string ValueSwitchNotNamedCat(Pet pet) => pet.Value switch
                {
                    not Cat { Name: "cat" } => "not named cat",
                    Cat => "cat",
                };
                public static string ValueSwitchNull(Pet pet) => pet.Value switch
                {
                    null => "null",
                    Cat cat => cat.Name,
                    Dog dog => dog.Name,
                    _ => "other",
                };

                public static bool SideEffect() => true;

                public static string GuardedOrSideEffect(Pet pet)
                {
                    if (pet is not null)
                    {
                        if (pet is Cat cat || SideEffect())
                            return "cat";
                    }
                    return "other";
                }

                public static string SideEffectBeforePattern(Pet pet)
                {
                    if (pet is not null)
                    {
                        if (SideEffect() && pet is Cat cat)
                            return cat.Name;
                    }
                    return "other";
                }
            }
            """);

        Assert.Equal("""
            return pet switch
            {
                Cat cat => cat.Name,
                Dog dog => dog.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Describe"));
        Assert.Equal("""
            return pet switch
            {
                Cat => 1,
                Dog => 2,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "Kind"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedDefault"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Cat cat => cat.Name.ToUpperInvariant(),
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedDefaultBound"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Dog dog => dog.Name,
                _ => "other",
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedDefaultBoundSameAsDefault"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Cat => "other cat",
                Dog dog => dog.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedExhaustive"));
        Assert.Equal("""
            return pet switch
            {
                Cat cat when cat.Name == "cat" => cat.Name,
                Cat cat => cat.Name.ToUpperInvariant(),
                Dog dog => dog.Name,
            };
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedExhaustiveBound"));
        Assert.Equal("return pet is Cat cat ? cat.Name : \"other\";",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "Ternary"));
        Assert.Equal("return pet is Cat cat && cat.Name == \"cat\" ? cat.Name : \"other\";",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "TernaryGuard"));
        Assert.Equal("return pet is Cat || pet is Dog;",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCatOrDog"));
        Assert.Equal("return pet is not null && pet is not Cat;",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsNotCat"));
        Assert.Equal("return pet.Value is not Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsNotCat"));
        var classValueNotNamedCat = RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsNotNamedCat");
        Assert.Contains("pet.Value as Cat", classValueNotNamedCat);
        Assert.Contains("V_0.Name == \"cat\"", classValueNotNamedCat);
        var classValueDogOrNull = RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsDogOrNull");
        Assert.Contains("pet.Value", classValueDogOrNull);
        Assert.DoesNotContain("return pet is Dog || pet is null", classValueDogOrNull);
        Assert.Equal("return pet.Value is not null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueIsNotNull"));
        Assert.Equal("return pet is Cat { Name: \"cat\" };",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCatWithName"));
        Assert.Equal("return pet is Cat { Name: not \"dog\" };",
            RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCatWithOtherName"));
        Assert.Equal("""
            if (pet.Value is not null)
            {
                return "not null";
            }
            else
            {
                return "null";
            }
            """, RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNotNull"));
        var classValueSwitchNotNamedCat = RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNotNamedCat");
        Assert.Contains("pet.Value as Cat", classValueSwitchNotNamedCat);
        Assert.Contains("V_1.Name == \"cat\"", classValueSwitchNotNamedCat);
        var classValueSwitchNull = RenderMember(assembly.Path, "UnionFixtures.Matcher", "ValueSwitchNull");
        Assert.Contains("pet.Value", classValueSwitchNull);
        Assert.DoesNotContain("return pet switch", classValueSwitchNull);
        var guardedOr = RenderMember(assembly.Path, "UnionFixtures.Matcher", "GuardedOrSideEffect");
        Assert.DoesNotContain("pet is Cat cat || SideEffect() ? \"cat\" : \"other\"", guardedOr);
        Assert.Contains("if (", guardedOr);
        Assert.Contains("SideEffect()", guardedOr);
        var sideEffectBefore = RenderMember(assembly.Path, "UnionFixtures.Matcher", "SideEffectBeforePattern");
        Assert.DoesNotContain("SideEffect() && pet is Cat cat ? cat.Name : \"other\"", sideEffectBefore);
        Assert.Contains("if (", sideEffectBefore);
        Assert.Contains("SideEffect()", sideEffectBefore);
    }

    [Fact]
    public void NonUnionValuePropertyTypeTest_KeepsValueAccess()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace UnionFixtures
            {
                public sealed class Cat { public string Name { get; } = "cat"; }
                public sealed class Dog { public string Name { get; } = "dog"; }

                public readonly struct PetLike
                {
                    public PetLike(Cat value) => Value = value;
                    public object? Value { get; }
                }

                public static class Matcher
                {
                    public static bool IsCat(PetLike pet) => pet.Value is Cat;
                    public static bool IsCatOrDog(PetLike pet) => pet.Value is Cat or Dog;
                    public static bool IsNull(PetLike pet) => pet.Value is null;
                    public static bool HasCat(PetLike pet)
                    {
                        Cat cat = pet.Value as Cat;
                        return cat is not null;
                    }

                    public static string Describe(PetLike pet) => pet.Value switch
                    {
                        Cat cat => cat.Name,
                        Dog dog => dog.Name,
                        _ => "other",
                    };

                    public static string ExhaustiveLooking(PetLike pet) => pet.Value switch
                    {
                        Cat cat => cat.Name,
                        Dog dog => dog.Name,
                    };

                    public static string Guarded(PetLike pet) => pet.Value switch
                    {
                        Cat { Name: "cat" } cat => cat.Name,
                        Dog dog => dog.Name,
                        _ => "other",
                    };
                }
            }
            """);

        Assert.Equal("return pet.Value is Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCat"));
        var nonUnionOr = RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCatOrDog");
        Assert.Contains("pet.Value", nonUnionOr);
        Assert.DoesNotContain("return pet is Cat || pet is Dog", nonUnionOr);
        Assert.Equal("return pet.Value is null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsNull"));
        Assert.Equal("return pet.Value is Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "HasCat"));
        Assert.DoesNotContain("return pet switch", RenderMember(assembly.Path, "UnionFixtures.Matcher", "Describe"));
        Assert.DoesNotContain("return pet switch", RenderMember(assembly.Path, "UnionFixtures.Matcher", "ExhaustiveLooking"));
        Assert.DoesNotContain("return pet switch", RenderMember(assembly.Path, "UnionFixtures.Matcher", "Guarded"));
    }

    [Fact]
    public void UnionAttributeWithoutRuntimeIUnionMatching_KeepsValueAccess()
    {
        using var assembly = Compile("""
            #nullable enable
            namespace System.Runtime.CompilerServices
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class UnionAttribute : System.Attribute;
            }

            namespace UnionFixtures
            {
                public sealed class Cat { }

                [System.Runtime.CompilerServices.Union]
                public readonly struct PetLike
                {
                    public PetLike(Cat value) => Value = value;
                    public object? Value { get; }
                }

                public static class Matcher
                {
                    public static bool IsCat(PetLike pet) => pet.Value is Cat;
                    public static bool IsNull(PetLike pet) => pet.Value is null;
                }
            }
            """);

        Assert.Equal("return pet.Value is Cat;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsCat"));
        Assert.Equal("return pet.Value is null;", RenderMember(assembly.Path, "UnionFixtures.Matcher", "IsNull"));
    }

    static string ComposeType(string path, string fullName)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(surface.Types, t => t.FullName == fullName);
        var source = TypeSourceComposer.Compose(type, path, pdbPath: null);
        Assert.NotNull(source);
        return source!;
    }

    static TempAssembly Compile(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-union-{Guid.NewGuid():N}.dll");
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return new TempAssembly(path);
    }

    static async Task<TempAssembly> CompileWithSdk(string source)
    {
        var project = TempDirectory.Create();
        File.WriteAllText(Path.Combine(project.Path, "union-fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(project.Path, "Fixture.cs"), source);

        var result = await RunDotnetBuild(project.Path);
        Assert.True(result.ExitCode == 0,
            "Union fixture must build with the preview SDK, got exit "
            + result.ExitCode + "\n--- output ---\n" + result.Output + "\n--- source ---\n" + source);

        string dll = Path.Combine(project.Path, "bin", "Release", "net11.0", "union-fixture.dll");
        return new TempAssembly(dll, project);
    }

    static string RenderMember(string assemblyPath, string typeName, string methodName)
    {
        using var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function);
        Assert.NotNull(result.Output);
        return result.Output!.Trim();
    }

    static async Task AssertSdkPreviewBuilds(string source)
    {
        const string caseTypeStubs = """
            namespace Other
            {
                public sealed class Bird { }
            }

            namespace UnionFixtures
            {
                public sealed class Cat { }
                public sealed class Dog { }
                public interface IMarker { }
            }
            """;

        using var project = TempDirectory.Create();
        File.WriteAllText(Path.Combine(project.Path, "union-bind.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(project.Path, "RenderedUnion.cs"), source);
        File.WriteAllText(Path.Combine(project.Path, "CaseTypes.cs"), caseTypeStubs);

        var result = await RunDotnetBuild(project.Path);
        Assert.True(result.ExitCode == 0,
            "Rendered union source must build with the preview SDK, got exit "
            + result.ExitCode + "\n--- output ---\n" + result.Output + "\n--- source ---\n" + source);
    }

    static async Task<(int ExitCode, string Output)> RunDotnetBuild(string workingDirectory)
    {
        var psi = new ProcessStartInfo("dotnet", "build -c Release --nologo --verbosity quiet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var waitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
        if (await Task.WhenAny(waitTask, timeoutTask) != waitTask)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            string timedOutOutput = string.Concat(await stdoutTask, await stderrTask);
            Assert.Fail("dotnet build did not complete within 30 seconds.\n--- output ---\n" + timedOutOutput);
        }

        await waitTask;
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        return (process.ExitCode, stdout + stderr);
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            references.Add(MetadataReference.CreateFromFile(path));
        }
        return references.ToImmutable();
    }

    sealed class TempAssembly(string path, TempDirectory? directory = null) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (directory is not null)
            {
                directory.Dispose();
                return;
            }
            try { File.Delete(Path); }
            catch { }
        }
    }

    sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dotnet-inspect-union-bind-{Guid.NewGuid():N}");

        TempDirectory() => Directory.CreateDirectory(Path);

        public static TempDirectory Create() => new();

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
