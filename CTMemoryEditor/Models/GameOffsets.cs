namespace CTMemoryEditor.Models;

/// <summary>
/// All memory offsets for the Steam version of Chrono Trigger (32-bit x86, no ASLR).
/// Values recovered via Ghidra reverse engineering.
/// </summary>
public static class GameOffsets
{
    // Process and base pointer
    public const string ProcessName = "Chrono Trigger";
    public const uint GameDataPointer = 0x0041B4C4; // expanded game state object

    // Character records: 7 slots, each 0x120 bytes
    public const uint CharacterArrayBase = 0x10C0;
    public const uint CharacterStride = 0x120;
    public const int CharacterCount = 7;

    // Per-character field offsets (relative to character base)
    public static class Character
    {
        public const uint Id = 0x00;
        public const uint HPMax = 0x10;
        public const uint HPCurrent = 0x14;
        public const uint MPMax = 0x18;
        public const uint MPCurrent = 0x1C;
        public const uint HPBase = 0x20;
        public const uint Strength = 0x24;
        public const uint Stamina = 0x28;
        public const uint Speed = 0x2C;
        public const uint Magic = 0x30;
        public const uint Accuracy = 0x34;
        public const uint Evasion = 0x38;
        public const uint MagicDefense = 0x3C;
        public const uint Level = 0x40;
        public const uint TotalXP = 0x44;
        public const uint Weapon = 0x84;
        public const uint Armor = 0x88;
        public const uint Helmet = 0x8C;
        public const uint Accessory = 0x90;
        public const uint XPToNextLevel = 0x94;
        public const uint ComputedStrength = 0xB4;
        public const uint ComputedStamina = 0xB8;
        public const uint ComputedSpeed = 0xBC;
        public const uint ComputedMagic = 0xC0;
        public const uint ComputedAccuracy = 0xC4;
        public const uint ComputedEvasion = 0xC8;
        public const uint ComputedMagicDefense = 0xCC;
        public const uint AttackPower = 0xD4;
        public const uint Defense = 0xDC;
    }

    public const uint Gold = 0x2AB4;
    public const uint PlayTime = 0x2AC0;

    // Battle speed: byte in low 8 bits of uint32.
    // 0=Speed 1 (fastest) … 7=Speed 8 (slowest).
    public const uint BattleSpeed = 0x10B4;
    
    // Live UI Config sync copy of Battle Speed in BattleData (updated by settings menu)
    public const uint BattleSpeedExpanded = 0x13F94;

    // Storyline counter: gameStateBase+0x10000 (low byte only).
    // The PC engine emulates SNES WRAM directly using a dense byte array:
    // 0x00000..0x0FFFF = SNES 7E0000..7EFFFF (Work RAM)
    // 0x10000..0x1FFFF = SNES 7F0000..7FFFFF (Save RAM / Event flags)
    // The Room Scripting Engine pulls the live Storyline Counter from 0x10000 (SNES 7F0000).
    // (Note: The sparsely packed uint32 array at 0x0800 ONLY syncs to this live array during load/save).
    public const uint StorylineCounter = 0x10000;

    // On room transitions this copy overwrites StorylineCounter, overwriting any in-session patch.
    // WriteStoryline must therefore update both locations simultaneously.
    public const uint StorylineCounterExpanded = 0x110b0;
    public const uint InventoryBase = 0x18A0;
    public const uint InventorySlotSize = 12;
    public const int InventorySlotCount = 347;


    /// <summary>
    /// Per-category slot ranges within the 347-slot inventory array.
    /// Slots are contiguous blocks, one per category, in category-ID order.
    /// </summary>
    public static class CategorySlots
    {
        // (startSlot, count) per category ID 0–5
        public static readonly (int Start, int Count)[] Ranges =
        [
            (  0, 111),  // 0 = Weapon
            (111,  50),  // 1 = Armor
            (161,  39),  // 2 = Helmet
            (200,  59),  // 3 = Accessory
            (259,  43),  // 4 = Consumable
            (302,  45),  // 5 = Key Item
        ];
    }


    // Party roster - game state (save/load source of truth)
    public static readonly uint[] PartySlots = [0x28E4, 0x28E8, 0x28EC];

    // Party roster - expanded battle data (active runtime copy, read/write from here)
    // Written by SNES event-script opcode 0x2D at runtime whenever the
    // active party changes.
    public static readonly uint[] BattlePartySlots = [0x1324c, 0x13250, 0x13254];

    // Party roster - load-time snapshot
    public static readonly uint[] BattlePartySlotSnapshots = [0x1854, 0x1858, 0x185C];
}
