using System.CodeDom.Compiler;

namespace ILInspector.Research.NameFamilyFixtures;

public sealed class Validator
{
    public bool Validate() => true;
}

public sealed class CustomerValidator
{
    public bool Validate() => true;
}

public sealed class CustomerValidators
{
    public bool Validate() => true;
}

internal sealed class OrderValidator
{
    public bool Validate() => true;
}

public sealed class ValidatorOptions
{
    public bool Enabled => true;
}

public sealed class ValidationContext
{
    public string Name => nameof(ValidationContext);
}

[GeneratedCode("Fixture.Generator", "1.0")]
public sealed class ValidatorJsonContext;

[GeneratedCode("Fixture.Generator", "1.0")]
public sealed class MixedValidator
{
    public bool Validate() => true;
}

public interface UnknownValidator;

public sealed class GenericValidator<T>
{
    public T? Value { get; init; }
}

public sealed class JSONContext
{
    public string Name => nameof(JSONContext);
}

public sealed class FooBar
{
    public int Value => 1;
}

public sealed class Foo_Bar
{
    public int Value => 1;
}

public sealed class DelegateInvoker1;
public sealed class DelegateInvoker2;
public sealed class DelegateInvoker3;

public sealed class ZZQ1;
public sealed class ZZQ2;
public sealed class ZZQ3;

public sealed class Outer
{
    public sealed class Validator
    {
        public bool Validate() => true;
    }
}
