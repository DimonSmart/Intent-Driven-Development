internal sealed class ClaudePlatformAdapter : PlatformPluginBuilder
{
    public override string Platform => "claude";
    protected override string ManifestDirectory => ".claude-plugin";
    protected override string ManifestFileName => "plugin.json";
}
