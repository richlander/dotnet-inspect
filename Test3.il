.assembly Test3 {}
.class public auto ansi beforefieldinit Program
       extends [System.Runtime]System.Object
{
  .method public hidebysig static uint32 Main(bool c) cil managed
  {
    .maxstack 2
    .locals init (uint32 V_0)
    ldarg.0
    brtrue.s TRUE_ARM
    ldc.i4.m1
    br.s DONE
  TRUE_ARM:
    ldloc.0
  DONE:
    ret
  }
}
