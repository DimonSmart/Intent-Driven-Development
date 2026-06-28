internal sealed class GenerateApp
{
    public int Run(string[] args)
    {
        var options = GeneratorOptionsParser.Parse(args);
        var repoRoot = RepositoryRootFinder.Find();
        var generator = new Generator(new RepositoryLayout(repoRoot));
        var result = generator.Run(options.CheckOnly, options.ManifestVersion);

        if (result.Count == 0)
        {
            Console.WriteLine(options.CheckOnly ? "Generated files are current." : "Generated files updated.");
            return 0;
        }

        foreach (var item in result)
        {
            Console.Error.WriteLine(item);
        }

        return 1;
    }
}
