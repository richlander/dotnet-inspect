namespace ILInspector.Decompiler.Tests;

// The CfgSampleClass. filename keeps this file immediately after CfgSampleClass.cs
// in compiler item order; exact-base render A/B guards the anonymous-type ordinals.
// #3371 follow-up witnesses: a record `with` expression and an anonymous object
// wide enough that the printer's brace-body width wrapper breaks them Allman-style
// (one entry per line). These related top-level types stay together as one
// compiler-fixture group.
public sealed record MeasuredRecord(
    int FirstMeasuredValue,
    int SecondMeasuredValue,
    int ThirdMeasuredValue,
    int FourthMeasuredValue);

public static class BraceBodyWrappingSamples
{
    // `source with { A = .., B = .., C = .., D = .. }` — flat form exceeds 120 cols.
    public static MeasuredRecord WidenMeasuredRecord(MeasuredRecord source, int first, int second, int third, int fourth)
        => source with
        {
            FirstMeasuredValue = first,
            SecondMeasuredValue = second,
            ThirdMeasuredValue = third,
            FourthMeasuredValue = fourth,
        };

    // Anonymous types are reference types, so returning one as `object` needs no
    // box/cast; the anonymous object stays the bare return value. Explicit
    // `Name = value` form (value names differ from property names), flat > 120 cols.
    public static object ProjectMeasuredValues(int first, int second, int third, int fourth)
        => new
        {
            FirstMeasuredProjection = first,
            SecondMeasuredProjection = second,
            ThirdMeasuredProjection = third,
            FourthMeasuredProjection = fourth,
        };
}
