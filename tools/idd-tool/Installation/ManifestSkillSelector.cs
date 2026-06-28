internal static class ManifestSkillSelector
{
    public static HashSet<string> SelectedSkills(Manifest manifest, IReadOnlyList<string> selectedPacks)
    {
        var selectedSkills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var packName in selectedPacks)
        {
            foreach (var skill in manifest.Packs[packName].Skills)
            {
                selectedSkills.Add(skill);
            }
        }

        return selectedSkills;
    }
}
