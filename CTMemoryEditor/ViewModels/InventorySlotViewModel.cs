using CTMemoryEditor.Models;
using CTMemoryEditor.Services;

namespace CTMemoryEditor.ViewModels;

public sealed class InventorySlotViewModel : ViewModelBase
{
    private readonly GameMemoryService _memory;
    private bool _suppressWrites;

    public InventorySlotViewModel(GameMemoryService memory, int slotIndex)
    {
        _memory = memory;
        SlotIndex = slotIndex;
    }

    public int SlotIndex { get; }

    private byte _itemIndex;
    public byte ItemIndex
    {
        get => _itemIndex;
        set
        {
            if (SetProperty(ref _itemIndex, value))
            {
                OnPropertyChanged(nameof(ItemName));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private byte _category;
    public byte Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, value))
            {
                OnPropertyChanged(nameof(CategoryName));
                OnPropertyChanged(nameof(ItemName));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private byte _quantity;
    public byte Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value) && !_suppressWrites)
                _memory.WriteInventorySlotQuantity(SlotIndex, value);
        }
    }

    public bool IsEmpty => _itemIndex == 0 && _category == 0;
    public string ItemName => ItemDatabase.GetItemName(_category >> 4, _itemIndex);
    public string CategoryName => ItemDatabase.GetCategoryName(_category >> 4);

    public void UpdateFromSlot(InventorySlot slot)
    {
        _suppressWrites = true;
        try
        {
            ItemIndex = slot.ItemIndex;
            Category = slot.Category;
            Quantity = slot.Quantity;
        }
        finally
        {
            _suppressWrites = false;
        }
    }
}
