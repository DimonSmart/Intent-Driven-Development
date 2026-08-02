namespace Idd.Factory.LiveTests.Infrastructure;

public static class RepositoryRootFinder
{
    public static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "tools", "generate", "Generate.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the Intent-Driven-Development repository root.");
    }
}
