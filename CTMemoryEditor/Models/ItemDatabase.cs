using System.IO;
using System.Reflection;

namespace CTMemoryEditor.Models;

/// <summary>
/// A single selectable equipment entry with its encoded value and display name.
/// </summary>
public record EquipmentOption(ushort Encoded, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Lookup tables for Chrono Trigger (PC) item names, loaded from the embedded sfc_item.txt.
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

    private static readonly string[][] _categories = LoadFromResource();

    private static string[][] LoadFromResource()
    {
        var dicts = new Dictionary<int, string>[CategoryNames.Length];
        for (int i = 0; i < dicts.Length; i++)
            dicts[i] = [];

        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("CTMemoryEditor.Data.sfc_item.txt");

        if (stream != null)
        {
            using StreamReader reader = new(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                int comma = line.IndexOf(',');
                string key  = comma >= 0 ? line[..comma] : line;
                string name = comma >= 0 ? line[(comma + 1)..].Trim() : "";
                if (string.IsNullOrEmpty(name)) continue;

                int catIndex = key switch
                {
                    _ when key.Contains("_ITEM_WEAPON_")    => 0,
                    _ when key.Contains("_ITEM_ARMOR_")     => 1,
                    _ when key.Contains("_ITEM_HELMET_")    => 2,
                    _ when key.Contains("_ITEM_ACCSESARY_") => 3,
                    _ when key.Contains("_ITEM_USEITEM_")   => 4,
                    _ when key.Contains("_ITEM_IMPORTANT_") => 5,
                    _                                       => -1,
                };
                if (catIndex < 0) continue;

                int lastUnderscore = key.LastIndexOf('_');
                if (lastUnderscore < 0 || !int.TryParse(key[(lastUnderscore + 1)..], out int itemIndex))
                    continue;

                dicts[catIndex][itemIndex] = name;
            }
        }

        // Convert each dictionary to a string[] sized to the highest present index.
        string[][] result = new string[dicts.Length][];
        for (int cat = 0; cat < dicts.Length; cat++)
        {
            Dictionary<int, string> dict = dicts[cat];
            int max = dict.Count > 0 ? dict.Keys.Max() : 0;
            string[] arr = new string[max + 1];
            for (int i = 0; i <= max; i++)
                arr[i] = dict.GetValueOrDefault(i, "");
            result[cat] = arr;
        }
        return result;
    }

    /// <summary>
    /// Gets the display name for an item given its category and raw item index.
    /// </summary>
    public static string GetItemName(int category, int itemIndex)
    {
        if (category < 0 || category >= _categories.Length)
            return $"?Cat{category}:#{itemIndex}";

        string[] table = _categories[category];
        if (itemIndex < 0 || itemIndex >= table.Length || string.IsNullOrEmpty(table[itemIndex]))
            return $"{CategoryNames[category]} #{itemIndex}";

        return table[itemIndex];
    }

    /// <summary>
    /// Returns the full name array for a category, indexed by raw item index.
    /// Empty strings indicate unnamed/dummy entries.
    /// </summary>
    public static string[] GetCategoryItems(int category)
    {
        if (category < 0 || category >= _categories.Length) return [];
        return _categories[category];
    }

    /// <summary>
    /// Gets the display name from a raw encoded equipment uint16.
    /// Format: (category >> 12) | item_index
    /// </summary>
    public static string GetItemNameEncoded(ushort encoded)
    {
        int category  = (encoded >> 12) & 0xF;
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
    /// Returns selectable options for a category, skipping unnamed/dummy entries.
    /// Encoded format: (category >> 12) | item_index
    /// </summary>
    public static IReadOnlyList<EquipmentOption> GetEquipmentOptions(int category)
    {
        if (category < 0 || category >= _categories.Length) return [];

        string[] items = _categories[category];
        List<EquipmentOption> options = [];
        for (int i = 0; i < items.Length; i++)
        {
            if (!string.IsNullOrEmpty(items[i]))
                options.Add(new EquipmentOption((ushort)((category << 12) | i), items[i]));
        }
        return options;
    }
}
