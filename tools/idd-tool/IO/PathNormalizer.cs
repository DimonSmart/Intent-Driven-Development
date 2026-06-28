internal static class PathNormalizer
{
    public static string Normalize(string value) => value.Replace('\\', '/');
}
