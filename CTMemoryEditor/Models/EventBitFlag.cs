namespace CTMemoryEditor.Models;

public class EventBitFlag
{
    public int ByteIndex { get; init; }
    public byte BitMask { get; init; }
    public string Name { get; init; } = string.Empty;
}
