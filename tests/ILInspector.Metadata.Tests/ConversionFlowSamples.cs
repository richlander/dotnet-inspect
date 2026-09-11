namespace ILInspector.Metadata.Tests;

public readonly struct ConversionFlowSample
{
    public static implicit operator int(ConversionFlowSample value) => 0;
    public static implicit operator long(ConversionFlowSample value) => 0;
    public static explicit operator short(ConversionFlowSample value) => 0;
    public static explicit operator byte(ConversionFlowSample value) => 0;
    public static explicit operator checked short(ConversionFlowSample value) => 0;
    public static explicit operator checked byte(ConversionFlowSample value) => 0;

    public static int ReadInt32(ConversionFlowSample value) => 0;
    public static long ReadInt64(ConversionFlowSample value) => 0;
}
