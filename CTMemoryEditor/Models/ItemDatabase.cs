namespace CTMemoryEditor.Models;

/// <summary>
/// A single selectable equipment entry with its encoded value and display name.
/// </summary>
public record EquipmentOption(ushort Encoded, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Lookup tables for Chrono Trigger item names, indexed by raw item index within each category.
/// </summary>
public static class ItemDatabase
{
    public static readonly string[] CategoryNames =
    [
        "Weapon",     // 0
        "Armor",      // 1
        "Helmet",     // 2
        "Accessory",  // 3
        "Consumable", // 4
        "Key Item",   // 5
    ];

    // Weapon indices (category 0) — ordered by internal index
    private static readonly string[] Weapons =
    [
        /*  0 */ "(None)",
        /*  1 */ "Wood Sword",
        /*  2 */ "Iron Blade",
        /*  3 */ "Steel Saber",
        /*  4 */ "Lode Sword",
        /*  5 */ "Red Katana",
        /*  6 */ "Flint Edge",
        /*  7 */ "Dark Saber",
        /*  8 */ "Aeon Blade",
        /*  9 */ "Demon Edge",
        /* 10 */ "AlloyBlade",
        /* 11 */ "Star Sword",
        /* 12 */ "VedicBlade",
        /* 13 */ "Kali Blade",
        /* 14 */ "Shiva Edge",
        /* 15 */ "Bolt Sword",
        /* 16 */ "Slasher",
        /* 17 */ "Slasher 2",
        /* 18 */ "Swallow",
        /* 19 */ "Rainbow",
        /* 20 */ "Mop",
        /* 21 */ "Bronze Bow",
        /* 22 */ "Iron Bow",
        /* 23 */ "Lode Bow",
        /* 24 */ "Robin Bow",
        /* 25 */ "Sage Bow",
        /* 26 */ "Dream Bow",
        /* 27 */ "CometArrow",
        /* 28 */ "Sonic Arrow",
        /* 29 */ "Siren",
        /* 30 */ "Valkyrie",
        /* 31 */ "Air Gun",
        /* 32 */ "Dart Gun",
        /* 33 */ "Auto Gun",
        /* 34 */ "Plasma Gun",
        /* 35 */ "Megablast",
        /* 36 */ "Shock Wave",
        /* 37 */ "WonderShot",
        /* 38 */ "Graedus",
        /* 39 */ "Tin Arm",
        /* 40 */ "Hammer Arm",
        /* 41 */ "MirageHand",
        /* 42 */ "Stone Arm",
        /* 43 */ "DoomFinger",
        /* 44 */ "Magma Hand",
        /* 45 */ "MegatonArm",
        /* 46 */ "Big Hand",
        /* 47 */ "Kaiser Arm",
        /* 48 */ "Giga Arm",
        /* 49 */ "Terra Arm",
        /* 50 */ "Crisis Arm",
        /* 51 */ "Bronze Edge",
        /* 52 */ "Iron Sword",
        /* 53 */ "Masamune",
        /* 54 */ "Flash Blade",
        /* 55 */ "Pearl Edge",
        /* 56 */ "Rune Blade",
        /* 57 */ "Demon Hit",
        /* 58 */ "Brave Sword",
        /* 59 */ "Masamune 2",
        /* 60 */ "Dark Scythe",
        /* 61 */ "Hurricane",
        /* 62 */ "Star Scythe",
        /* 63 */ "DoomSickle",
        /* 64 */ "Fist (Iron)",
        /* 65 */ "Fist (Brze)",
    ];

    private static readonly string[] Armors =
    [
        /*  0 */ "(None)",
        /*  1 */ "Hide Tunic",
        /*  2 */ "Karate Gi",
        /*  3 */ "BronzeMail",
        /*  4 */ "MaidenSuit",
        /*  5 */ "Iron Suit",
        /*  6 */ "Titan Vest",
        /*  7 */ "Gold Suit",
        /*  8 */ "Ruby Vest",
        /*  9 */ "Dark Mail",
        /* 10 */ "Mist Robe",
        /* 11 */ "MesoMail",
        /* 12 */ "Lumin Robe",
        /* 13 */ "Flash Mail",
        /* 14 */ "Lode Vest",
        /* 15 */ "Aeon Suit",
        /* 16 */ "ZodiacCape",
        /* 17 */ "Nova Armor",
        /* 18 */ "Moon Armor",
        /* 19 */ "RubyArmor",
        /* 20 */ "Gloom Cape",
        /* 21 */ "White Mail",
        /* 22 */ "Black Mail",
        /* 23 */ "Blue Mail",
        /* 24 */ "Red Mail",
        /* 25 */ "White Vest",
        /* 26 */ "Black Vest",
        /* 27 */ "Blue Vest",
        /* 28 */ "Red Vest",
        /* 29 */ "Taban Vest",
        /* 30 */ "Taban Suit",
        /* 31 */ "PrismDress",
        /* 32 */ "Raven Armor",
        /* 33 */ "Prismatic",
    ];

    private static readonly string[] Helmets =
    [
        /*  0 */ "(None)",
        /*  1 */ "Hide Cap",
        /*  2 */ "BronzeHelm",
        /*  3 */ "Iron Helm",
        /*  4 */ "Beret",
        /*  5 */ "Gold Helm",
        /*  6 */ "Rock Helm",
        /*  7 */ "CeraTopper",
        /*  8 */ "Taban Helm",
        /*  9 */ "Rainbow",
        /* 10 */ "MermaidCap",
        /* 11 */ "Vigil Hat",
        /* 12 */ "Memory Cap",
        /* 13 */ "Time Hat",
        /* 14 */ "Aeon Helm",
        /* 15 */ "Dark Helm",
        /* 16 */ "Gloom Helm",
        /* 17 */ "Safe Helm",
        /* 18 */ "Doom Helm",
        /* 19 */ "PrismHelm",
        /* 20 */ "OzziePants",
        /* 21 */ "Haste Helm",
        /* 22 */ "R'bow Helm",
    ];

    private static readonly string[] Accessories =
    [
        /*  0 */ "(None)",
        /*  1 */ "Bandana",
        /*  2 */ "Ribbon",
        /*  3 */ "PowerGlove",
        /*  4 */ "Defender",
        /*  5 */ "MagicScarf",
        /*  6 */ "Amulet",
        /*  7 */ "Dash Ring",
        /*  8 */ "Hit Ring",
        /*  9 */ "Power Ring",
        /* 10 */ "Magic Ring",
        /* 11 */ "Wall Ring",
        /* 12 */ "Silver Earring",
        /* 13 */ "Gold Earring",
        /* 14 */ "SilverStud",
        /* 15 */ "Gold Stud",
        /* 16 */ "Sight Cap",
        /* 17 */ "Charm Top",
        /* 18 */ "Rage Band",
        /* 19 */ "FrenzyBand",
        /* 20 */ "Third Eye",
        /* 21 */ "Wallet",
        /* 22 */ "Green Dream",
        /* 23 */ "Berserker",
        /* 24 */ "Power Seal",
        /* 25 */ "Magic Seal",
        /* 26 */ "Speed Belt",
        /* 27 */ "Black Rock",
        /* 28 */ "Blue Rock",
        /* 29 */ "Silver Rock",
        /* 30 */ "White Rock",
        /* 31 */ "Gold Rock",
        /* 32 */ "Hero Medal",
        /* 33 */ "Muscle Ring",
        /* 34 */ "Flea Vest",
        /* 35 */ "Magic Tab",
        /* 36 */ "Power Tab",
        /* 37 */ "Speed Tab",
        /* 38 */ "Sun Shades",
        /* 39 */ "Prism Specs",
    ];

    private static readonly string[] Consumables =
    [
        /*  0 */ "(None)",
        /*  1 */ "Tonic",
        /*  2 */ "Mid Tonic",
        /*  3 */ "Full Tonic",
        /*  4 */ "Ether",
        /*  5 */ "Mid Ether",
        /*  6 */ "Full Ether",
        /*  7 */ "Elixir",
        /*  8 */ "HyperEther",
        /*  9 */ "MegaElixir",
        /* 10 */ "Heal",
        /* 11 */ "Revive",
        /* 12 */ "Shelter",
        /* 13 */ "Power Meal",
        /* 14 */ "Lapis",
        /* 15 */ "Barrier",
        /* 16 */ "Shield",
        /* 17 */ "Power Tab",
        /* 18 */ "Magic Tab",
        /* 19 */ "Speed Tab",
    ];

    private static readonly string[] KeyItems =
    [
        /*  0 */ "(None)",
        /*  1 */ "Bike Key",
        /*  2 */ "Pendant",
        /*  3 */ "Gate Key",
        /*  4 */ "Prism Shard",
        /*  5 */ "C. Trigger",
        /*  6 */ "Tools",
        /*  7 */ "Jerky",
        /*  8 */ "DreamStone",
        /*  9 */ "Race Log",
        /* 10 */ "Moon Stone",
        /* 11 */ "Sun Stone",
        /* 12 */ "Ruby Knife",
        /* 13 */ "Yakra Key",
        /* 14 */ "Clone",
        /* 15 */ "Toma's Pop",
        /* 16 */ "GoldenSand",
    ];

    private static readonly string[][] AllCategories =
    [
        Weapons,
        Armors,
        Helmets,
        Accessories,
        Consumables,
        KeyItems,
    ];

    /// <summary>
    /// Gets the display name for an item given its category and index.
    /// </summary>
    public static string GetItemName(int category, int itemIndex)
    {
        if (category < 0 || category >= AllCategories.Length)
            return $"?Cat{category}:#{itemIndex}";

        string[] table = AllCategories[category];
        if (itemIndex < 0 || itemIndex >= table.Length)
            return $"{CategoryNames[category]} #{itemIndex}";

        return table[itemIndex];
    }

    /// <summary>
    /// Returns the full item name array for a given category (includes empty/""/dummy entries by index).
    /// </summary>
    public static string[] GetCategoryItems(int category)
    {
        if (category < 0 || category >= AllCategories.Length)
            return [];
        return AllCategories[category];
    }


    /// <summary>
    /// Gets the display name from a raw encoded equipment uint16.
    /// Format: (category &lt;&lt; 12) | item_index
    /// </summary>
    public static string GetItemNameEncoded(ushort encoded)
    {
        int category = (encoded >> 12) & 0xF;
        int itemIndex = encoded & 0xFFF;
        return GetItemName(category, itemIndex);
    }

    /// <summary>
    /// Gets the category name from a category index.
    /// </summary>
    public static string GetCategoryName(int category)
    {
        if (category >= 0 && category < CategoryNames.Length)
            return CategoryNames[category];
        return $"Unknown({category})";
    }

    /// <summary>
    /// Returns a list of all items in a category as selectable options, with encoded ushort values.
    /// Encoded format: (category &lt;&lt; 12) | item_index
    /// </summary>
    public static IReadOnlyList<EquipmentOption> GetEquipmentOptions(int category)
    {
        if (category < 0 || category >= AllCategories.Length)
            return [];

        string[] items = AllCategories[category];
        EquipmentOption[] options = new EquipmentOption[items.Length];
        for (int i = 0; i < items.Length; i++)
            options[i] = new EquipmentOption((ushort)((category << 12) | i), items[i]);
        return options;
    }

    static ItemDatabase()
    {
        string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        string? targetPath = null;
        
        System.IO.DirectoryInfo? dir = new System.IO.DirectoryInfo(baseDir);
        while (dir != null)
        {
            string path = System.IO.Path.Combine(dir.FullName, "sfc_item.txt");
            if (System.IO.File.Exists(path))
            {
                targetPath = path;
                break;
            }
            dir = dir.Parent;
        }

        if (targetPath != null)
        {
            LoadFromFile(targetPath);
        }
    }

    private static void LoadFromFile(string path)
    {
        try
        {
            string[] lines = System.IO.File.ReadAllLines(path);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',', 2);
                if (parts.Length < 1) continue;
                string key = parts[0];
                string name = parts.Length > 1 ? parts[1].Trim() : "";
                if (string.IsNullOrEmpty(name)) continue;

                if (key.Contains("_ITEM_WEAPON_")) TrySetItem(0, key, name, Weapons);
                else if (key.Contains("_ITEM_ARMOR_")) TrySetItem(1, key, name, Armors);
                else if (key.Contains("_ITEM_HELMET_")) TrySetItem(2, key, name, Helmets);
                else if (key.Contains("_ITEM_ACCSESARY_")) TrySetItem(3, key, name, Accessories);
                else if (key.Contains("_ITEM_USEITEM_")) TrySetItem(4, key, name, Consumables);
                else if (key.Contains("_ITEM_IMPORTANT_")) TrySetItem(5, key, name, KeyItems);
            }
        }
        catch { }
    }

    private static void TrySetItem(int category, string key, string name, string[] collection)
    {
        int lastUnderscore = key.LastIndexOf('_');
        if (lastUnderscore >= 0 && lastUnderscore < key.Length - 1 && int.TryParse(key.Substring(lastUnderscore + 1), out int index))
        {
            if (index >= 0 && index < collection.Length)
            {
                collection[index] = name;
            }
        }
    }
}
