namespace CTMemoryEditor.ViewModels;

/// <summary>
/// Represents a selectable character for party roster combo boxes.
/// </summary>
public sealed class PartyOption
{
    public byte Id { get; }
    public string Name { get; }

    public PartyOption(byte id, string name)
    {
        Id = id;
        Name = name;
    }

    public override string ToString() => Name;
}
