using System.ComponentModel;
using System.Runtime.CompilerServices;
using CTMemoryEditor.Models;
using CTMemoryEditor.Services;

namespace CTMemoryEditor.ViewModels;

public class EventFlagViewModel : INotifyPropertyChanged
{
    private readonly GameMemoryService _memory;
    private readonly EventBitFlag _model;

    public EventFlagViewModel(GameMemoryService memory, EventBitFlag model)
    {
        _memory = memory;
        _model = model;
    }

    public string Name => _model.Name;
    public string HexOffset => $"7F{_model.ByteIndex:X4} (Bit {_model.BitMask:X2})";

    private bool _isSet;
    public bool IsSet
    {
        get => _isSet;
        set
        {
            if (_isSet != value)
            {
                _isSet = value;
                OnPropertyChanged();
                _memory.WriteEventBit(_model.ByteIndex, _model.BitMask, value);
            }
        }
    }

    public void Refresh()
    {
        var newVal = _memory.ReadEventBit(_model.ByteIndex, _model.BitMask);
        if (_isSet != newVal)
        {
            _isSet = newVal;
            OnPropertyChanged(nameof(IsSet));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
