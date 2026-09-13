using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal sealed record ResolvedIntentDocument(string Id, string Path, string Content);

internal enum IntentResolutionFailureKind
{
    MalformedId,
    Unknown,
    Ambiguous
}

internal sealed class IntentResolutionException(
    string intentId,
    IntentResolutionFailureKind kind,
    string message) : Exception(message)
{
    public string IntentId { get; } = intentId;
    public IntentResolutionFailureKind Kind { get; } = kind;
}

internal sealed class IntentDocumentResolver(string workspace)
{
    private readonly string intentDirectory = Path.Combine(workspace, ".idd", "intent");

    public string ResolvePath(string intentId)
    {
        if (!DurableIntentId.IsCanonical(intentId))
        {
            throw new IntentResolutionException(
                intentId,
                IntentResolutionFailureKind.MalformedId,
                $"Durable intent reference '{intentId}' is not a canonical IDD-NNNN identifier.");
        }

        var matches = Directory.Exists(intentDirectory)
            ? Directory.EnumerateFiles(intentDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Matches(intentId, Path.GetFileName(path)))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray()
            : [];

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new IntentResolutionException(
                intentId,
                IntentResolutionFailureKind.Unknown,
                $"Durable intent reference '{intentId}' does not resolve to a current document directly under .idd/intent/."),
            _ => throw new IntentResolutionException(
                intentId,
                IntentResolutionFailureKind.Ambiguous,
                $"Durable intent reference '{intentId}' resolves to more than one current document directly under .idd/intent/."),
        };
    }

    public async Task<ResolvedIntentDocument> ResolveAsync(
        string intentId,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(intentId);
        return new(
            intentId,
            path,
            await File.ReadAllTextAsync(path, cancellationToken));
    }

    private static bool Matches(string intentId, string fileName)
    {
        var prefix = intentId + ".";
        const string suffix = ".md";
        return fileName.StartsWith(prefix, StringComparison.Ordinal)
               && fileName.EndsWith(suffix, StringComparison.Ordinal)
               && fileName.Length > prefix.Length + suffix.Length;
    }
}
