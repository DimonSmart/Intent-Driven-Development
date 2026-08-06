namespace Idd.Factory.LiveTests.Infrastructure;

public sealed class CodexHomeLocator
{
    private readonly Func<string?> environment;
    private readonly Func<string> userProfile;

    public CodexHomeLocator() : this(() => Environment.GetEnvironmentVariable("CODEX_HOME"), () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }
    internal CodexHomeLocator(Func<string?> environment, Func<string> userProfile) { this.environment = environment; this.userProfile = userProfile; }

    public string? FindSessionsDirectory()
    {
        var home = environment();
        if (string.IsNullOrWhiteSpace(home))
            home = Path.Combine(userProfile(), ".codex");

        var sessions = Path.Combine(home, "sessions");
        return Directory.Exists(sessions) ? sessions : null;
    }
}
