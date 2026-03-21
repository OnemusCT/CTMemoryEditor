namespace CTMemoryEditor.Models;

/// <summary>
/// Plain data object holding all mapped fields for one character slot.
/// </summary>
public class CharacterRecord
{
    // Identity
    public byte CharacterId { get; set; }

    // HP / MP
    public ushort HPMax { get; set; }
    public ushort HPCurrent { get; set; }
    public ushort MPMax { get; set; }
    public ushort MPCurrent { get; set; }
    public ushort HPBase { get; set; }

    // Base stats
    public byte Strength { get; set; }
    public byte Stamina { get; set; }
    public byte Speed { get; set; }
    public byte Magic { get; set; }
    public byte Accuracy { get; set; }
    public byte Evasion { get; set; }
    public byte MagicDefense { get; set; }

    // Level / XP
    public byte Level { get; set; }
    public uint TotalXP { get; set; }
    public ushort XPToNextLevel { get; set; }

    // Equipment (encoded: category << 12 | item_index)
    public ushort Weapon { get; set; }
    public ushort Armor { get; set; }
    public ushort Helmet { get; set; }
    public ushort Accessory { get; set; }

    // Computed stats (with equipment)
    public byte ComputedStrength { get; set; }
    public byte ComputedStamina { get; set; }
    public byte ComputedSpeed { get; set; }
    public byte ComputedMagic { get; set; }
    public byte ComputedAccuracy { get; set; }
    public byte ComputedEvasion { get; set; }
    public byte ComputedMagicDefense { get; set; }
    public byte AttackPower { get; set; }
    public byte Defense { get; set; }

    /// <summary>
    /// Returns the display name for a character ID.
    /// </summary>
    public static string GetName(int id) => id switch
    {
        0 => "Crono",
        1 => "Marle",
        2 => "Lucca",
        3 => "Robo",
        4 => "Frog",
        5 => "Ayla",
        6 => "Magus",
        _ => $"Unknown ({id})"
    };
}
