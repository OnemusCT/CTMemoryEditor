namespace CTMemoryEditor.Models;

/// <summary>
/// A point-in-time capture of all editable game state, suitable for serialization
/// and reapplication to live game memory.
/// </summary>
public class GameSnapshot
{
    public DateTime CapturedAt { get; set; }

    /// <summary>All 7 character records (indices 0-6).</summary>
    public CharacterRecord[] Characters { get; set; } = new CharacterRecord[GameOffsets.CharacterCount];

    /// <summary>Party roster: character IDs for slots 0, 1, 2.</summary>
    public byte[] PartyRoster { get; set; } = new byte[3];

    /// <summary>All 347 inventory slots; empty slots are included to preserve exact layout.</summary>
    public InventorySlot[] Inventory { get; set; } = Array.Empty<InventorySlot>();

    public uint Gold { get; set; }
    public int BattleSpeed { get; set; }
    public byte Storyline { get; set; }

    /// <summary>Raw 512-byte array containing SNES WRAM 7F0000 - 7F01FF</summary>
    public byte[]? EventFlags { get; set; }
}
