namespace CTMemoryEditor.Models;

/// <summary>
/// All memory offsets for the Steam version of Chrono Trigger (32-bit x86, no ASLR).
/// Values recovered via Ghidra reverse engineering.
/// Ghidra image base: 0x00100000 (calibrated: Ghidra VA 0x001ae693 == process VA 0x00E6E693,
/// module load base 0x00DC0000, delta 0x00CC0000 → imagebase = 0x00DC0000 - 0x00CC0000).
/// RVA formula: processVA = moduleBase + (ghidraVA - 0x00100000).
/// </summary>
public static class GameOffsets
{
    // Process and base pointer
    public const string ProcessName = "Chrono Trigger";
    public const uint GameStatePointerVA  = 0x0051B4BC; // → SOME_BATTLE_OFFSET (0x60000-byte SNES RAM image)
    public const uint BattleDataPointerVA = 0x0051B4C4; // → BATTLE_DATA_OFFSET (expanded game state object)

    // Character records: 7 slots, each 0x120 bytes
    public const uint CharacterArrayBase = 0x10C0;
    public const uint CharacterStride = 0x120;
    public const int CharacterCount = 7;

    // Per-character field offsets (relative to character base)
    public static class Character
    {
        // Identity
        public const uint Id = 0x00;
        public const uint Flag1 = 0x04;
        public const uint Flag2 = 0x08;
        public const uint Flag3 = 0x0C;

        // HP / MP
        public const uint HPMax = 0x10;
        public const uint HPCurrent = 0x14;
        public const uint MPMax = 0x18;
        public const uint MPCurrent = 0x1C;
        public const uint HPBase = 0x20;

        // Base stats (byte values in uint32 cells)
        public const uint Strength = 0x24;
        public const uint Stamina = 0x28;
        public const uint Speed = 0x2C;
        public const uint Magic = 0x30;
        public const uint Accuracy = 0x34;
        public const uint Evasion = 0x38;
        public const uint MagicDefense = 0x3C;

        // Level and XP
        public const uint Level = 0x40;
        public const uint TotalXP = 0x44;

        // Equipment (uint16 encoded: category << 12 | item_index)
        public const uint Weapon = 0x84;
        public const uint Armor = 0x88;
        public const uint Helmet = 0x8C;
        public const uint Accessory = 0x90;
        public const uint XPToNextLevel = 0x94;

        // Computed stats (with equipment bonuses applied)
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

    // Base stat to computed stat offset mapping
    public static readonly (uint BaseOffset, uint ComputedOffset)[] StatPairs =
    [
        (Character.Strength, Character.ComputedStrength),
        (Character.Stamina, Character.ComputedStamina),
        (Character.Speed, Character.ComputedSpeed),
        (Character.Magic, Character.ComputedMagic),
        (Character.Accuracy, Character.ComputedAccuracy),
        (Character.Evasion, Character.ComputedEvasion),
        (Character.MagicDefense, Character.ComputedMagicDefense),
    ];

    // Global fields (relative to game state base)
    public const uint Gold = 0x2AB4;
    public const uint PlayTime = 0x2AC0;

    // Battle speed: byte in low 8 bits of uint32.
    // 0=Speed 1 (fastest) … 7=Speed 8 (slowest). Confirmed via CL_SerializeSaveBuffer
    // (tail section: puVar4[iVar11+2] = param_1[0x10B4] → save file byte 0x1EBD, 0-indexed).
    // 34 active code references confirm this is the live runtime field (not a save-only staging copy).
    public const uint BattleSpeed = 0x10B4;
    
    // Live UI Config sync copy of Battle Speed in BattleData (updated by settings menu)
    public const uint BattleSpeedExpanded = 0x13F94; // BATTLE_DATA_OFFSET + 0x13F94

    // World state flags / storyline
    // World-state event flags begin at 0x0800 (each SNES byte stored as a uint32).
    // These are NOT the storyline counter — they are the script/event flag bitfield.
    public const uint WorldFlagsBase = 0x0800;

    // Storyline counter: gameStateBase+0x10000 (low byte only).
    // The PC engine emulates SNES WRAM directly using a dense byte array:
    // 0x00000..0x0FFFF = SNES 7E0000..7EFFFF (Work RAM)
    // 0x10000..0x1FFFF = SNES 7F0000..7FFFFF (Save RAM / Event flags)
    // The active Room Scripting Engine pulls the live Storyline Counter from 0x10000 (SNES 7F0000).
    // (Note: The sparsely packed uint32 array at 0x0800 ONLY syncs to this live array during load/save).
    public const uint StorylineCounter = 0x10000;

    // On room transitions FUN_00370720 reads the expanded copy at BattleData+0x110b0 and writes
    // the byte back to SOME_BATTLE_OFFSET+0x10000, overwriting any in-session patch.
    // WriteStoryline must therefore update both locations simultaneously.
    public const uint StorylineCounterExpanded = 0x110b0; // BATTLE_DATA_OFFSET+0x110b0, low byte of DWORD

    // Inventory
    public const uint InventoryBase = 0x18A0;
    public const uint InventorySlotSize = 12; // 3 x uint32
    public const int InventorySlotCount = 347;


    /// <summary>
    /// Per-category slot ranges within the 347-slot inventory array.
    /// Slots are contiguous blocks, one per category, in category-ID order.
    /// Counts derived from sfc_item.txt item table sizes; accessory start at slot 200 confirmed in-game.
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


    // Party roster — game state (save/load source of truth)
    public const uint PartySlot0 = 0x28E4;
    public const uint PartySlot1 = 0x28E8;
    public const uint PartySlot2 = 0x28EC;

    // Party roster — expanded battle data (active runtime copy, read/write from here)
    // Written by SNES event-script opcode 0x2D (FUN_00267b20) at runtime whenever the
    // active party changes. Confirmed live via memory dump: matches in-game party.
    // The older copy at 0x1854 is only a load-time snapshot and goes stale after party changes.
    public const uint BattlePartySlot0 = 0x1324c;
    public const uint BattlePartySlot1 = 0x13250;
    public const uint BattlePartySlot2 = 0x13254;

    // World position
    public const uint PartyMapId = 0x1000;
    public const uint PartyX = 0x1004;
    public const uint PartyY = 0x1008;

    // MSVC rand() seed location.
    // rand() and srand() are dynamically imported; _ranseed lives in the CRT DLL.
    // This is the RVA (relative to the CT module base) of the IAT entry that holds
    // the runtime address of rand(). Reading that pointer, then parsing rand()'s
    // machine code, yields the address of _ranseed.
    // Ghidra VA of IAT entry: 0x00485380; Ghidra image base: 0x00100000.
    public const uint RandIatRva = 0x00385380;
}
