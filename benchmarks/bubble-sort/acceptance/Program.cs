using System.Reflection;

if (args.Length != 1 || !Directory.Exists(args[0]))
    return Fail("Pass the console project directory to the deterministic Bubble Sort probe.");

MethodInfo? method = null;
foreach (var path in Directory.EnumerateFiles(Path.Combine(args[0], "bin"), "*.dll", SearchOption.AllDirectories)
             .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
{
    try
    {
        var assembly = Assembly.LoadFrom(path);
        method = assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .FirstOrDefault(candidate => candidate.Name.Contains("sort", StringComparison.OrdinalIgnoreCase) &&
                candidate.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(int[]) &&
                candidate.ReturnType is { } returnType && (returnType == typeof(void) || returnType == typeof(int[])));
        if (method is not null) break;
    }
    catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or ReflectionTypeLoadException) { }
}

if (method is null) return Fail("Could not locate a Bubble Sort method accepting int[] and returning void or int[].");

object? instance;
try { instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!, nonPublic: true); }
catch (Exception exception) { return Fail($"Could not create the Bubble Sort implementation: {exception.Message}"); }

var cases = new (int[] Input, int[] Expected)[]
{
    ([], []), ([7], [7]), ([1, 2, 3], [1, 2, 3]), ([3, 2, 1], [1, 2, 3]),
    ([2, 1, 2, 1], [1, 1, 2, 2]), ([-1, -3, 2, 0], [-3, -1, 0, 2]), ([9, 1, 5, 3, 7], [1, 3, 5, 7, 9])
};
foreach (var (input, expected) in cases)
{
    var working = input.ToArray();
    object? returned;
    try { returned = method.Invoke(instance, [working]); }
    catch (TargetInvocationException exception) { return Fail($"Bubble Sort threw for [{string.Join(", ", input)}]: {exception.InnerException?.Message ?? exception.Message}"); }
    var actual = method.ReturnType == typeof(void) ? working : returned as int[];
    if (actual is null || !actual.SequenceEqual(expected))
        return Fail($"Bubble Sort failed for [{string.Join(", ", input)}]. Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual ?? [])}].");
}

Console.WriteLine("Deterministic Bubble Sort probe passed all seven inputs.");
return 0;

static int Fail(string message) { Console.Error.WriteLine(message); return 1; }
