namespace Idd.Factory.Runtime;

internal sealed class WorkspaceChangeCalculator
{
    public IReadOnlyList<string> Calculate(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after) =>
        after.Where(x => !before.TryGetValue(x.Key, out var prior) || prior != x.Value)
            .Select(x => x.Key)
            .Concat(before.Keys.Where(path => !after.ContainsKey(path)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
}
