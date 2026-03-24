using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CTMemoryEditor.Models;

namespace CTMemoryEditor.Services;

public class TreasureDataService
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();
    private const string ResourcePrefix = "CTMemoryEditor.Data.";

    private Dictionary<int, string> _weaponNames = new();
    private Dictionary<int, string> _armorNames = new();
    private Dictionary<int, string> _helmetNames = new();
    private Dictionary<int, string> _accessoryNames = new();
    private Dictionary<int, string> _useItemNames = new();
    private Dictionary<int, string> _importantNames = new();

    public TreasureDataService()
    {
        LoadItemNames();
    }

    private static Stream? OpenResource(string fileName)
    {
        return _assembly.GetManifestResourceStream(ResourcePrefix + fileName);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private void LoadItemNames()
    {
        using Stream? stream = OpenResource("sfc_item.txt");
        if (stream == null) return;

        using StreamReader reader = new(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split(',', 2);
            if (parts.Length < 2) continue;
            string key = parts[0].Trim();
            string name = parts[1].Trim();

            if (key.Contains("_ITEM_WEAPON_")) ExtractAndAdd(_weaponNames, key, name);
            else if (key.Contains("_ITEM_ARMOR_")) ExtractAndAdd(_armorNames, key, name);
            else if (key.Contains("_ITEM_HELMET_")) ExtractAndAdd(_helmetNames, key, name);
            else if (key.Contains("_ITEM_ACCSESARY_")) ExtractAndAdd(_accessoryNames, key, name);
            else if (key.Contains("_ITEM_USEITEM_")) ExtractAndAdd(_useItemNames, key, name);
            else if (key.Contains("_ITEM_IMPORTANT_")) ExtractAndAdd(_importantNames, key, name);
        }
    }

    private void ExtractAndAdd(Dictionary<int, string> dict, string key, string name)
    {
        var split = key.Split('_');
        if (int.TryParse(split[^1], out int id))
        {
            dict[id] = name;
        }
    }

    public List<TreasureChest> LoadChests()
    {
        var chests = new List<TreasureChest>();
        using Stream? offsetStream = OpenResource("TakaraOffsetTbl.dat");
        using Stream? dataStream = OpenResource("TakaraDataTbl.dat");

        if (offsetStream == null || dataStream == null)
            return chests;

        byte[] offsetBytes = ReadAllBytes(offsetStream);
        byte[] dataBytes = ReadAllBytes(dataStream);

        int totalEntries = dataBytes.Length / 6;
        ushort[] offsets = new ushort[offsetBytes.Length / 2];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = BitConverter.ToUInt16(offsetBytes, i * 2);

        Dictionary<int, int> entryOwner = new();
        int maxTable = offsets.Length - 1;

        for (int tableIdx = 2; tableIdx <= maxTable; tableIdx++)
        {
            int o = offsets[tableIdx];
            int o_next = totalEntries;
            for (int j = tableIdx + 1; j <= maxTable; j++)
            {
                if (offsets[j] != o)
                {
                    o_next = offsets[j];
                    break;
                }
            }
            if (o_next > o)
            {
                entryOwner[o] = tableIdx - 2; // MapId
            }
        }

        HashSet<int> seenOffsets = new();
        for (int tableIdx = 2; tableIdx <= maxTable; tableIdx++)
        {
            int o = offsets[tableIdx];
            if (!seenOffsets.Add(o)) continue;

            int o_next = totalEntries;
            for (int j = tableIdx + 1; j <= maxTable; j++)
            {
                if (offsets[j] != o)
                {
                    o_next = offsets[j];
                    break;
                }
            }

            int nTotal = o_next - o;
            if (nTotal == 0 || !entryOwner.ContainsKey(o)) continue;

            int mapId = entryOwner[o];

            for (int i = 0; i < nTotal; i++)
            {
                int absIdx = o + i;
                int byteOff = 4 + absIdx * 6;
                if (byteOff + 6 > dataBytes.Length) break;

                byte raw2 = dataBytes[byteOff + 2];
                byte raw3 = dataBytes[byteOff + 3];

                if (raw2 == 0xFF && (raw3 & 0x7F) == 0x7F) continue;

                if ((raw3 & 0x80) != 0)
                {
                    int gold = (raw2 | ((raw3 & 0x7F) << 8)) * 2;
                    chests.Add(new TreasureChest { GlobalIndex = absIdx, MapId = mapId, ItemName = $"{gold} G", Category = "Gold" });
                }
                else
                {
                    int itemIdx = raw2;
                    int catId = (raw3 >> 4) & 0xF;
                    string name = GetItemName(catId, itemIdx);
                    chests.Add(new TreasureChest { GlobalIndex = absIdx, MapId = mapId, ItemName = name, Category = GetCategoryName(catId) });
                }
            }
        }

        return chests;
    }

    private string GetCategoryName(int catId) => catId switch
    {
        0 => "Weapon", 1 => "Armor", 2 => "Helmet", 3 => "Accessory", 4 => "UseItem", 5 => "Important", _ => "Unknown"
    };

    private string GetItemName(int catId, int id)
    {
        var dict = catId switch
        {
            0 => _weaponNames, 1 => _armorNames, 2 => _helmetNames,
            3 => _accessoryNames, 4 => _useItemNames, 5 => _importantNames, _ => null
        };
        if (dict != null && dict.TryGetValue(id, out string? name) && name != "dummy" && !string.IsNullOrEmpty(name))
            return name;
        return $"#{id}";
    }
}
