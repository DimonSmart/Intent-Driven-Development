using System.Text;

internal static class GeneratedOutputWriter
{
    public static void Write(string outputRoot, IReadOnlyList<GeneratedFile> expectedFiles)
    {
        Directory.CreateDirectory(outputRoot);
        CleanOutput(outputRoot);
        foreach (var file in expectedFiles)
        {
            var fullPath = Path.Combine(outputRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void CleanOutput(string outputRoot)
    {
        foreach (var entry in Directory.GetFileSystemEntries(outputRoot))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }
}
