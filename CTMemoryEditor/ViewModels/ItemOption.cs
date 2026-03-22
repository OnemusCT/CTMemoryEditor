namespace CTMemoryEditor.ViewModels;

/// <summary>
/// Represents a selectable item in the Add-item picker.
/// </summary>
public sealed class ItemOption
{
    public byte Id { get; }
    public string Name { get; }

    public ItemOption(byte id, string name)
    {
        Id = id;
        Name = name;
    }
}
