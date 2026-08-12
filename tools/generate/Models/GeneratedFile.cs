internal sealed record GeneratedFile(string RelativePath, string? Content = null, byte[]? BinaryContent = null)
{
    public static GeneratedFile Binary(string relativePath, string sourcePath) => new(relativePath, BinaryContent: File.ReadAllBytes(sourcePath));
}
