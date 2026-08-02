namespace MiniCatalog;

public sealed class Catalog
{
    private readonly List<string> codes = [];

    public void Add(string code) => codes.Add(code);

    public IReadOnlyList<string> Codes => codes;
}
