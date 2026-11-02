// Dev tool: checks a set of assemblies against the libraries a stripped game ships.
//   refs   <search dirs ;-separated, first wins> <assemblies...>
//       every type and member those assemblies use from the search dirs that doesn't resolve (stripped away)
//   vtable <search dirs ;-separated, first wins> <game dir>
//       game types that implement an interface whose methods they don't all have (they'd fail to load: "VTable setup
//       failed") when the libraries in the search dirs are used
using Mono.Cecil;

static DefaultAssemblyResolver Resolver(string dirs)
{
    var r = new DefaultAssemblyResolver();
    foreach (var d in r.GetSearchDirectories()) r.RemoveSearchDirectory(d);
    foreach (var d in dirs.Split(';', StringSplitOptions.RemoveEmptyEntries)) r.AddSearchDirectory(d);
    return r;
}

static bool IsCore(string name) =>
    name is "mscorlib" or "netstandard" || name.StartsWith("System") || name.StartsWith("Mono.") || name.StartsWith("UnityEngine");

var mode = args[0];
var resolver = Resolver(args[1]);
var rp = new ReaderParameters { AssemblyResolver = resolver };

if (mode == "refs")
{
    foreach (var path in args.Skip(2))
    {
        var asm = AssemblyDefinition.ReadAssembly(path, rp);
        var missing = new SortedSet<string>();
        foreach (var module in asm.Modules)
        {
            foreach (var t in module.GetTypeReferences())
            {
                if (!IsCore(t.Scope.Name.Replace(".dll", ""))) continue;
                try { if (t.Resolve() == null) missing.Add("type " + t.FullName); }
                catch (Exception e) { missing.Add($"type {t.FullName} ({e.GetType().Name})"); }
            }

            foreach (var m in module.GetMemberReferences())
            {
                var scope = m.DeclaringType.GetElementType().Scope.Name.Replace(".dll", "");
                if (!IsCore(scope)) continue;
                try
                {
                    var ok = m switch
                    {
                        MethodReference mr => mr.Resolve() != null,
                        FieldReference fr => fr.Resolve() != null,
                        _ => true
                    };
                    if (!ok) missing.Add("member " + m.FullName);
                }
                catch (Exception e) { missing.Add($"member {m.FullName} ({e.GetType().Name})"); }
            }
        }

        Console.WriteLine($"== {Path.GetFileName(path)}: {missing.Count} missing");
        foreach (var s in missing) Console.WriteLine("  " + s);
    }
}
else if (mode == "vtable")
{
    var gameDir = args[2];
    var total = 0;
    foreach (var path in Directory.GetFiles(gameDir, "*.dll"))
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name is "mscorlib" or "netstandard" || name.StartsWith("System") || name.StartsWith("Mono.")) continue;
        AssemblyDefinition asm;
        try { asm = AssemblyDefinition.ReadAssembly(path, rp); }
        catch { continue; }
        foreach (var type in asm.MainModule.GetTypes())
        {
            if (type.IsInterface) continue;
            foreach (var problem in Missing(type))
            {
                Console.WriteLine($"{name}: {type.FullName} lacks {problem}");
                total++;
            }
        }
    }

    Console.WriteLine($"total {total}");
}

// Interface methods (from the libraries in use) that the type and its bases don't implement.
static IEnumerable<string> Missing(TypeDefinition type)
{
    var have = new HashSet<string>();
    var explicitImpl = new HashSet<string>();
    var interfaces = new List<TypeReference>();
    for (TypeDefinition t = type; t != null;)
    {
        foreach (var m in t.Methods)
        {
            have.Add(m.Name + "/" + m.Parameters.Count);
            foreach (var o in m.Overrides) explicitImpl.Add(o.DeclaringType.GetElementType().FullName + "::" + o.Name + "/" + o.Parameters.Count);
        }

        interfaces.AddRange(t.Interfaces.Select(i => i.InterfaceType));
        try { t = t.BaseType?.Resolve(); }
        catch { yield break; }
    }

    var seen = new HashSet<string>();
    var queue = new Queue<TypeReference>(interfaces);
    while (queue.Count > 0)
    {
        var iref = queue.Dequeue();
        TypeDefinition idef;
        try { idef = iref.Resolve(); }
        catch { continue; }
        if (idef == null || !seen.Add(idef.FullName)) continue;
        if (!IsCore(idef.Module.Assembly.Name.Name)) continue; // only interfaces from the swapped libraries matter
        foreach (var i in idef.Interfaces) queue.Enqueue(i.InterfaceType);
        foreach (var m in idef.Methods)
        {
            if (!m.IsAbstract || m.IsStatic) continue; // default and static interface methods need nothing from the type
            var key = m.Name + "/" + m.Parameters.Count;
            if (have.Contains(key) || explicitImpl.Contains(idef.FullName + "::" + key)) continue;
            // explicit implementations are usually named "Namespace.IFace<T>.Method"
            if (have.Any(h => h.EndsWith("." + key))) continue;
            yield return $"{idef.FullName}.{m.Name}";
        }
    }
}
