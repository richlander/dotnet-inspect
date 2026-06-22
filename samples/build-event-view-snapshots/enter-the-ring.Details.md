Showing 1 of 29 diagnostic(s). Use --cards 29 to show all, --tail-cards N to show the end, or filter with --code.

/home/rich/git/bad-code/EnterTheRing/SignalProcessor.cs(22,16): error CS0029: Cannot implicitly convert type 'System.Threading.Tasks.Task<EnterTheRing.Signal>' to 'EnterTheRing.Signal'
  20 |     public Signal WaitForSignal()
  21 |     {
  22 |         return WaitForSignalAsync().GetAwaiter().GetResult();
     |                ^
  23 |     }
  24 |
