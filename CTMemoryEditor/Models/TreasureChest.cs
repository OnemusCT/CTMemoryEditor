using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CTMemoryEditor.Models;

public class TreasureChest : INotifyPropertyChanged
{
    public int GlobalIndex { get; init; }
    public int MapId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    private bool _isOpened;
    public bool IsOpened
    {
        get => _isOpened;
        set
        {
            if (_isOpened != value)
            {
                _isOpened = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
