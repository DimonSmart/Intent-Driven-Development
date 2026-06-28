internal static class RequiredFileReader
{
    public static string Read(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : throw new FileNotFoundException("Required file not found.", path);
}
