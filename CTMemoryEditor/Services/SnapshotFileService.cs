using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CTMemoryEditor.Models;

namespace CTMemoryEditor.Services;

public static class SnapshotFileService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(GameSnapshot snapshot, string filePath)
    {
        SnapshotDto dto = ToDto(snapshot);
        string json = JsonSerializer.Serialize(dto, SerializerOptions);
        File.WriteAllText(filePath, json);
    }

    public static GameSnapshot Load(string filePath)
    {
        string json = File.ReadAllText(filePath);
        SnapshotDto dto = JsonSerializer.Deserialize<SnapshotDto>(json, SerializerOptions)
            ?? throw new InvalidDataException("Snapshot file is empty or invalid.");

        return FromDto(dto);
    }

    private static SnapshotDto ToDto(GameSnapshot snap)
    {
        CharacterDto[] characters = new CharacterDto[snap.Characters.Length];
        for (int i = 0; i < snap.Characters.Length; i++)
            characters[i] = CharacterToDto(snap.Characters[i]);

        List<InventorySlotDto> inventory = new();
        foreach (InventorySlot slot in snap.Inventory)
        {
            if (!slot.IsEmpty)
                inventory.Add(SlotToDto(slot));
        }

        return new SnapshotDto
        {
            Version     = 1,
            CapturedAt  = snap.CapturedAt,
            Gold        = snap.Gold,
            BattleSpeed = snap.BattleSpeed,
            Storyline   = $"0x{snap.Storyline:X2}",
            PartyRoster = snap.PartyRoster,
            Characters  = characters,
            Inventory   = inventory.ToArray(),
        };
    }

    private static GameSnapshot FromDto(SnapshotDto dto)
    {
        // Parse storyline — accept both "0xAB" hex strings and plain decimal
        byte storyline;
        string storylineStr = dto.Storyline ?? "0";
        if (storylineStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            storyline = Convert.ToByte(storylineStr, 16);
        else
            storyline = byte.Parse(storylineStr);

        // Reconstruct all 347 slots; slots missing from the file are treated as empty
        InventorySlot[] allSlots = new InventorySlot[GameOffsets.InventorySlotCount];
        for (int i = 0; i < allSlots.Length; i++)
            allSlots[i] = new InventorySlot { SlotIndex = i };

        if (dto.Inventory != null)
        {
            foreach (InventorySlotDto slotDto in dto.Inventory)
            {
                int idx = slotDto.SlotIndex;
                if (idx >= 0 && idx < allSlots.Length)
                    allSlots[idx] = SlotFromDto(slotDto);
            }
        }

        CharacterRecord[] characters = new CharacterRecord[GameOffsets.CharacterCount];
        for (int i = 0; i < characters.Length; i++)
        {
            characters[i] = dto.Characters != null && i < dto.Characters.Length
                ? CharacterFromDto(dto.Characters[i])
                : new CharacterRecord();
        }

        return new GameSnapshot
        {
            CapturedAt  = dto.CapturedAt,
            Gold        = dto.Gold,
            BattleSpeed = dto.BattleSpeed,
            Storyline   = storyline,
            PartyRoster = dto.PartyRoster ?? new byte[3],
            Characters  = characters,
            Inventory   = allSlots,
        };
    }

    private static CharacterDto CharacterToDto(CharacterRecord c) => new()
    {
        Name              = CharacterRecord.GetName(c.CharacterId),
        CharacterId       = c.CharacterId,
        Level             = c.Level,
        HPMax             = c.HPMax,
        HPCurrent         = c.HPCurrent,
        MPMax             = c.MPMax,
        MPCurrent         = c.MPCurrent,
        HPBase            = c.HPBase,
        Strength          = c.Strength,
        Stamina           = c.Stamina,
        Speed             = c.Speed,
        Magic             = c.Magic,
        Accuracy          = c.Accuracy,
        Evasion           = c.Evasion,
        MagicDefense      = c.MagicDefense,
        TotalXP           = c.TotalXP,
        XPToNextLevel     = c.XPToNextLevel,
        Weapon            = c.Weapon,
        Armor             = c.Armor,
        Helmet            = c.Helmet,
        Accessory         = c.Accessory,
        ComputedStrength  = c.ComputedStrength,
        ComputedStamina   = c.ComputedStamina,
        ComputedSpeed     = c.ComputedSpeed,
        ComputedMagic     = c.ComputedMagic,
        ComputedAccuracy  = c.ComputedAccuracy,
        ComputedEvasion   = c.ComputedEvasion,
        ComputedMagicDefense = c.ComputedMagicDefense,
        AttackPower       = c.AttackPower,
        Defense           = c.Defense,
    };

    private static CharacterRecord CharacterFromDto(CharacterDto d) => new()
    {
        CharacterId       = d.CharacterId,
        Level             = d.Level,
        HPMax             = d.HPMax,
        HPCurrent         = d.HPCurrent,
        MPMax             = d.MPMax,
        MPCurrent         = d.MPCurrent,
        HPBase            = d.HPBase,
        Strength          = d.Strength,
        Stamina           = d.Stamina,
        Speed             = d.Speed,
        Magic             = d.Magic,
        Accuracy          = d.Accuracy,
        Evasion           = d.Evasion,
        MagicDefense      = d.MagicDefense,
        TotalXP           = d.TotalXP,
        XPToNextLevel     = d.XPToNextLevel,
        Weapon            = d.Weapon,
        Armor             = d.Armor,
        Helmet            = d.Helmet,
        Accessory         = d.Accessory,
        ComputedStrength  = d.ComputedStrength,
        ComputedStamina   = d.ComputedStamina,
        ComputedSpeed     = d.ComputedSpeed,
        ComputedMagic     = d.ComputedMagic,
        ComputedAccuracy  = d.ComputedAccuracy,
        ComputedEvasion   = d.ComputedEvasion,
        ComputedMagicDefense = d.ComputedMagicDefense,
        AttackPower       = d.AttackPower,
        Defense           = d.Defense,
    };

    private static InventorySlotDto SlotToDto(InventorySlot s) => new()
    {
        SlotIndex = s.SlotIndex,
        ItemIndex = s.ItemIndex,
        Category  = s.Category,
        Quantity  = s.Quantity,
        ItemName  = s.ItemName,
    };

    private static InventorySlot SlotFromDto(InventorySlotDto d) => new()
    {
        SlotIndex = d.SlotIndex,
        ItemIndex = d.ItemIndex,
        Category  = d.Category,
        Quantity  = d.Quantity,
    };

    private sealed class SnapshotDto
    {
        public int         Version     { get; set; }
        public DateTime    CapturedAt  { get; set; }
        public uint        Gold        { get; set; }
        public int         BattleSpeed { get; set; }
        public string?     Storyline   { get; set; }
        public byte[]?     PartyRoster { get; set; }
        public CharacterDto[]?   Characters { get; set; }
        public InventorySlotDto[]? Inventory { get; set; }
    }

    private sealed class CharacterDto
    {
        [JsonPropertyName("_name")]
        public string?  Name              { get; set; }
        public byte     CharacterId       { get; set; }
        public byte     Level             { get; set; }
        public ushort   HPMax             { get; set; }
        public ushort   HPCurrent         { get; set; }
        public ushort   MPMax             { get; set; }
        public ushort   MPCurrent         { get; set; }
        public ushort   HPBase            { get; set; }
        public byte     Strength          { get; set; }
        public byte     Stamina           { get; set; }
        public byte     Speed             { get; set; }
        public byte     Magic             { get; set; }
        public byte     Accuracy          { get; set; }
        public byte     Evasion           { get; set; }
        public byte     MagicDefense      { get; set; }
        public uint     TotalXP           { get; set; }
        public ushort   XPToNextLevel     { get; set; }
        public ushort   Weapon            { get; set; }
        public ushort   Armor             { get; set; }
        public ushort   Helmet            { get; set; }
        public ushort   Accessory         { get; set; }
        public byte     ComputedStrength  { get; set; }
        public byte     ComputedStamina   { get; set; }
        public byte     ComputedSpeed     { get; set; }
        public byte     ComputedMagic     { get; set; }
        public byte     ComputedAccuracy  { get; set; }
        public byte     ComputedEvasion   { get; set; }
        public byte     ComputedMagicDefense { get; set; }
        public byte     AttackPower       { get; set; }
        public byte     Defense           { get; set; }
    }

    private sealed class InventorySlotDto
    {
        public int    SlotIndex { get; set; }
        public byte   ItemIndex { get; set; }
        public byte   Category  { get; set; }
        public byte   Quantity  { get; set; }
        [JsonPropertyName("_itemName")]
        public string? ItemName { get; set; }
    }
}
