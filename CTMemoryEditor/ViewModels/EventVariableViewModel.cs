using System.ComponentModel;
using System.Runtime.CompilerServices;
using CTMemoryEditor.Models;
using CTMemoryEditor.Services;

namespace CTMemoryEditor.ViewModels;

public class EventVariableViewModel : INotifyPropertyChanged
{
    private readonly GameMemoryService _memory;
    private readonly EventVariable _model;

    public EventVariableViewModel(GameMemoryService memory, EventVariable model)
    {
        _memory = memory;
        _model = model;
    }

    public int ByteIndex => _model.ByteIndex;
    public string Name => _model.Name;
    public string Description => _model.Description;
    public string HexOffset => $"7F{_model.ByteIndex:X4}";

    private byte _value;
    public byte Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged();
                _memory.WriteEventByte(_model.ByteIndex, value);
            }
        }
    }

    public void Refresh()
    {
        var newVal = _memory.ReadEventByte(_model.ByteIndex);
        if (_value != newVal)
        {
            _value = newVal;
            OnPropertyChanged(nameof(Value));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
