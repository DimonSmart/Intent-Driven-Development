internal sealed class ListPacksCommand
{
    public int Run()
    {
        foreach (var pack in new ManifestReader().Read().Packs.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            Console.WriteLine(pack);
        }

        return 0;
    }
}
