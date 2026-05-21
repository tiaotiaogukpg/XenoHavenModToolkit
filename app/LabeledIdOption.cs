namespace XenoHavenModToolkit;

internal sealed class LabeledIdOption
{
    public LabeledIdOption(string name, int id, bool isKnown = true)
    {
        Name = name;
        Id = id;
        IsKnown = isKnown;
    }

    public string Name { get; }

    public int Id { get; }

    public bool IsKnown { get; }

    public string Display => $"{Name}-{Id}";

    public override string ToString() => Display;
}
