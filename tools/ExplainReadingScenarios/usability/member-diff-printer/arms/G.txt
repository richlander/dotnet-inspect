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
