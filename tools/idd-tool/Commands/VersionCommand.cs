internal sealed class VersionCommand
{
    public int Run()
    {
        var manifest = new ManifestReader().Read();
        var packageVersion = typeof(IntentDrivenDevelopmentToolApp).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        Console.WriteLine($"package: {packageVersion}");
        Console.WriteLine($"manifest: {manifest.Version}");
        return 0;
    }
}
