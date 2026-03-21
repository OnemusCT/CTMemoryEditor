namespace CTMemoryEditor.Models;

/// <summary>
/// Plain data object for one inventory slot.
/// </summary>
public class InventorySlot
{
    public int SlotIndex { get; set; }
    public byte ItemIndex { get; set; }
    public byte Category { get; set; }
    public byte Quantity { get; set; }

    public bool IsEmpty => ItemIndex == 0 && Category == 0;

    public string ItemName => ItemDatabase.GetItemName(Category >> 4, ItemIndex);
    public string CategoryName => ItemDatabase.GetCategoryName(Category >> 4);
}
