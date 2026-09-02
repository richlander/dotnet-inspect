using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

_ = Assembly.Load([]);
_ = Assembly.LoadFile("inspected.dll");
_ = Assembly.LoadFrom("inspected.dll");
_ = typeof(object).Assembly.LoadModule("inspected.netmodule", []);
_ = Assembly.UnsafeLoadFrom("inspected.dll");
_ = typeof(object).Assembly.CreateInstance("Inspected.Type");
_ = Type.GetType("Inspected.Type, Inspected");
_ = AssemblyLoadContext.Default;
_ = new DynamicMethod("ExecuteInspectedCode", typeof(void), Type.EmptyTypes);
_ = AppDomain.CurrentDomain;
_ = Activator.CreateInstance(typeof(object));
