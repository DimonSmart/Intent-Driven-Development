internal static class GeneratedOutputChecker
{
    public static IReadOnlyList<string> CheckSingleFile(string fullPath, string expectedContent)
    {
        if (!File.Exists(fullPath))
        {
            return [$"Missing generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath)}"];
        }

        var actual = File.ReadAllText(fullPath);
        if (!StringComparer.Ordinal.Equals(actual, expectedContent))
        {
            return [$"Outdated generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath)}"];
        }

        return [];
    }

    public static IReadOnlyList<string> CheckFiles(string outputRoot, IReadOnlyList<GeneratedFile> expectedFiles)
    {
        var errors = new List<string>();
        var expectedByPath = expectedFiles.ToDictionary(file => PathNormalizer.Normalize(file.RelativePath), file => file.Content);

        foreach (var expected in expectedByPath)
        {
            var fullPath = Path.Combine(outputRoot, expected.Key);
            if (!File.Exists(fullPath))
            {
                errors.Add($"Missing generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath)}");
                continue;
            }

            var actual = File.ReadAllText(fullPath);
            if (!StringComparer.Ordinal.Equals(actual, expected.Value))
            {
                errors.Add($"Outdated generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath)}");
            }
        }

        if (Directory.Exists(outputRoot))
        {
            foreach (var actualPath in Directory.GetFiles(outputRoot, "*", SearchOption.AllDirectories))
            {
                var relative = PathNormalizer.Normalize(Path.GetRelativePath(outputRoot, actualPath));
                if (!expectedByPath.ContainsKey(relative))
                {
                    errors.Add($"Unexpected generated file: {Path.GetRelativePath(Directory.GetCurrentDirectory(), actualPath)}");
                }
            }
        }

        return errors;
    }
}
