Each scenario below is the complete output of one command that compares one Member of the Example library between version 1.0.0 (baseline) and version 2.0.0 (current).

Scenario S1: Example.Widget.Run
```text
# -  baseline only   +  current only   (space)  compared, equal   ·  enclosure: shows location, not compared
·  namespace Example;
·  public sealed class Widget
·  {
@@ -1 +1 @@
-      public void Run(int count);
+      public void Run(long count);         Breaking: parameter type changed
                       ^^^^ changed from int
·      … 5 other members
·  }
```

Scenario S2: Example.Gauge.Read
```text
# -  baseline only   +  current only   (space)  compared, equal   ·  enclosure: shows location, not compared
·  namespace Example;
·  public sealed class Gauge                Type added in 2.0.0
·  {
@@ -0,0 +1 @@
+      public double Read(TimeSpan window);
·      … 2 other members
·  }
```

Scenario S3: Example.Widget.Name
```text
# -  baseline only   +  current only   (space)  compared, equal   ·  enclosure: shows location, not compared
·  namespace Example;
·  public sealed class Widget
·  {
       public string Name { get; }          Unchanged since 1.0.0
·      … 5 other members
·  }
```

Scenario S4: Example.Widget.Count
```text
# -  baseline only   +  current only   (space)  compared, equal   ·  enclosure: shows location, not compared
·  namespace Example;
·  public sealed class Widget
·  {
@@ -1,2 +1,2 @@
-      [Tag("a")]
+      [Tag("b")]
            ^^^ changed from "a"
       public int Count { get; }
·      … 5 other members
·  }
```

Scenario S5: Example.Widget.Flush (Member Body)
```text
# -  baseline only   +  current only   (space)  compared, equal   ·  enclosure: shows location, not compared
·  namespace Example;
·  public sealed class Widget
·  {
@@ -1,5 +1,5 @@
       public void Flush(long count)
       {
-          var buffer = new byte[count];
+          var buffer = ArrayPool<byte>.Shared.Rent((int)count);
                        ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ allocation removed
           Write(buffer);
       }
·      … 5 other members
·  }
```

Scenario S6: Example.Widget.Reset
```text
# -  baseline only   +  current only   (space)  compared, equal   ·  enclosure: shows location, not compared
·  namespace Example;
·  public sealed class Widget
·  {
@@ -1 +0,0 @@
-      public void Reset();                 Breaking: member removed
·      … 6 other members
·  }
```
