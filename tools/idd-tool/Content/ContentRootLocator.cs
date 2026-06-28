internal static class ContentRootLocator
{
    public static string Find()
    {
        var installedContentRoot = Path.Combine(AppContext.BaseDirectory, "package-content");
        if (Directory.Exists(installedContentRoot))
        {
            return installedContentRoot;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "canonical")) &&
                Directory.Exists(Path.Combine(current.FullName, "generated")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new ToolException("Could not locate bundled Intent-Driven Development content.");
    }
}
