internal static class PathNormalizer
{
    public static string Normalize(string path) => path.Replace('\\', '/');
}
