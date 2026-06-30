using System.Text;

internal sealed class Generator(RepositoryLayout layout)
{
    public IReadOnlyList<string> Run(bool checkOnly, string manifestVersion)
    {
        if (Environment.ExitCode != 0)
        {
            return ["Invalid generator arguments."];
        }

        var errors = new List<string>();
        var adapterReader = new AdapterReader();
        var adapterDefinitions = Directory
            .GetDirectories(layout.AdaptersRoot)
            .OrderBy(Path.GetFileName)
            .Select(adapterDir => new AdapterDefinition(adapterDir, adapterReader.Read(adapterDir)))
            .ToArray();
        var supportedCodingAgents = adapterDefinitions
            .Select(definition => definition.Config.CodingAgent)
            .ToHashSet(StringComparer.Ordinal);
        var skillDescriptions = new SkillDescriptionReader().Read(layout.SkillDescriptionsPath, supportedCodingAgents);
        var packManifest = new PackManifestReader(layout).Read();
        new PackManifestValidator(layout).Validate(packManifest);

        foreach (var adapterDefinition in adapterDefinitions)
        {
            var expectedFiles = new AdapterFilePlanner(layout).BuildFiles(adapterDefinition, supportedCodingAgents, packManifest);
            var outputRoot = Path.Combine(layout.GeneratedRoot, adapterDefinition.Config.CodingAgent);

            if (checkOnly)
            {
                errors.AddRange(GeneratedOutputChecker.CheckFiles(outputRoot, expectedFiles));
                continue;
            }

            GeneratedOutputWriter.Write(outputRoot, expectedFiles);
        }

        var manifest = ManifestBuilder.Build(adapterDefinitions, packManifest, skillDescriptions, manifestVersion);
        if (checkOnly)
        {
            errors.AddRange(GeneratedOutputChecker.CheckSingleFile(layout.ManifestPath, manifest));
        }
        else
        {
            File.WriteAllText(layout.ManifestPath, manifest, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return errors;
    }
}
