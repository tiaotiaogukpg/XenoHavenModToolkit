namespace XenoHavenModToolkit;

internal sealed class LabeledIdOption
{
    public LabeledIdOption(string name, int id, bool isKnown = true)
        : this(name, id, englishName: null, isKnown)
    {
    }

    public LabeledIdOption(string name, int id, string? englishName, bool isKnown = true)
    {
        Name = name;
        EnglishName = NormalizeEnglishName(englishName);
        Id = id;
        IsKnown = isKnown;
    }

    public string Name { get; }

    public string? EnglishName { get; }

    public int Id { get; }

    public bool IsKnown { get; }

    public string LocalizedName
    {
        get
        {
            if (LocalizationManager.Current == AppLanguage.En &&
                !string.IsNullOrWhiteSpace(EnglishName))
            {
                return EnglishName;
            }

            return Name;
        }
    }

    public string Display => $"{LocalizedName}-{Id}";

    public override string ToString() => Display;

    private static string? NormalizeEnglishName(string? englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName))
        {
            return null;
        }

        return englishName.Trim().TrimEnd(':').Trim();
    }
}
