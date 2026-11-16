You are a fresh participant in a reading trial. Everything you need is in this message. Do not use tools, read files, or search. Answer only from the outputs shown below, and say "not stated" when the outputs do not establish something. Reply with one JSON object and nothing else.

Each scenario below is the complete output of one command that compares one Member of the Example library between version 1.0.0 (baseline) and version 2.0.0 (current).

Scenario S1: Example.Widget.Run
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -1 +1 @@ public class Widget
-public void Run(int count);
+public void Run(long count);
```

Scenario S2: Example.Gauge.Read
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -0,0 +1 @@
+public double Read(TimeSpan window);
```

Scenario S3: Example.Widget.Name
```text
(no output)
```

Scenario S4: Example.Widget.Count
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -1,2 +1,2 @@ public class Widget
-[Tag("a")]
+[Tag("b")]
 public int Count { get; }
```

Scenario S5: Example.Widget.Flush (Member Body)
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -1,5 +1,5 @@ public class Widget
 public void Flush(long count)
 {
-    var buffer = new byte[count];
+    var buffer = ArrayPool<byte>.Shared.Rent((int)count);
     Write(buffer);
 }
```

Scenario S6: Example.Widget.Reset
```text
--- Example 1.0.0
+++ Example 2.0.0
@@ -1 +0,0 @@ public class Widget
-public void Reset();
```

For each scenario (S1 to S6), report:
- "changed": what changed in this Member between 1.0.0 and 2.0.0, specifically; "nothing" if nothing changed.
- "classification": the classification the output gives this Member's change, quoted exactly, or "none".
- "type_declaration_1_0_0": the containing Type's complete declaration line in version 1.0.0 according to the output; "did not exist" if the output establishes the Type did not exist in 1.0.0; or "not stated".
- "type_changed": whether the output establishes that the containing Type's own declaration changed between 1.0.0 and 2.0.0: "changed", "unchanged", or "not stated".

Also report "format_notes": anything in the output format you found ambiguous or had to guess.

Reply with exactly this JSON shape:
{"S1": {"changed": "", "classification": "", "type_declaration_1_0_0": "", "type_changed": ""}, "S2": {...}, "S3": {...}, "S4": {...}, "S5": {...}, "S6": {...}, "format_notes": ""}
