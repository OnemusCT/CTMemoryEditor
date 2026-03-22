using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CTMemoryEditor.Models;
using CTMemoryEditor.Services;
using Microsoft.Win32;

namespace CTMemoryEditor.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly GameMemoryService _memory = new();
    private readonly DispatcherTimer _refreshTimer;

    public MainViewModel()
    {
        Characters = new ObservableCollection<CharacterViewModel>();
        for (int i = 0; i < GameOffsets.CharacterCount; i++)
            Characters.Add(new CharacterViewModel(_memory, i));

        SelectedCharacter = Characters[0];

        PartyCharacterOptions = new ObservableCollection<PartyOption>();
        for (int i = 0; i < GameOffsets.CharacterCount; i++)
            PartyCharacterOptions.Add(new PartyOption((byte)i, CharacterRecord.GetName(i)));

        ConnectCommand = new RelayCommand(OnConnect, () => !IsConnected);
        DisconnectCommand = new RelayCommand(OnDisconnect, () => IsConnected);
        AddItemCommand = new RelayCommand(OnAddItem, () => IsConnected && (PendingItem?.Id ?? 0) > 0);
        RemoveItemCommand = new RelayCommand<InventorySlotViewModel>(OnRemoveItem, vm => IsConnected && vm != null);
        TakeSnapshotCommand = new RelayCommand(OnTakeSnapshot, () => IsConnected);
        ApplySnapshotCommand = new RelayCommand(OnApplySnapshot, () => IsConnected && HasSnapshot);
        SaveSnapshotCommand = new RelayCommand(OnSaveSnapshot, () => HasSnapshot);
        LoadSnapshotCommand = new RelayCommand(OnLoadSnapshot);

        AvailableCategoryNames = new ObservableCollection<string>(ItemDatabase.CategoryNames);
        _pendingCategoryIndex = 0;
        RefreshPendingItems();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += OnRefreshTick;
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    private string _statusText = "Not connected.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private bool _autoRefresh = true;
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (SetProperty(ref _autoRefresh, value))
            {
                if (value && IsConnected)
                    _refreshTimer.Start();
                else
                    _refreshTimer.Stop();
            }
        }
    }

    public ObservableCollection<CharacterViewModel> Characters { get; }

    private CharacterViewModel _selectedCharacter = null!;
    public CharacterViewModel SelectedCharacter
    {
        get => _selectedCharacter;
        set => SetProperty(ref _selectedCharacter, value);
    }

    public ObservableCollection<PartyOption> PartyCharacterOptions { get; }

    private bool _suppressPartyWrite;

    private byte _partySlot0;
    public byte PartySlot0
    {
        get => _partySlot0;
        set
        {
            if (SetProperty(ref _partySlot0, value) && !_suppressPartyWrite)
                _memory.WritePartySlot(0, value);
        }
    }

    private byte _partySlot1;
    public byte PartySlot1
    {
        get => _partySlot1;
        set
        {
            if (SetProperty(ref _partySlot1, value) && !_suppressPartyWrite)
                _memory.WritePartySlot(1, value);
        }
    }

    private byte _partySlot2;
    public byte PartySlot2
    {
        get => _partySlot2;
        set
        {
            if (SetProperty(ref _partySlot2, value) && !_suppressPartyWrite)
                _memory.WritePartySlot(2, value);
        }
    }

    public IReadOnlyList<int> BattleSpeedOptions { get; } = Enumerable.Range(1, 8).ToList();

    private int _battleSpeed = 1;
    private bool _suppressBattleSpeedWrite;
    public int BattleSpeed
    {
        get => _battleSpeed;
        set
        {
            if (SetProperty(ref _battleSpeed, value) && !_suppressBattleSpeedWrite)
                _memory.WriteBattleSpeed(value);
        }
    }

    private uint _gold;
    private bool _suppressGoldWrite;
    public uint Gold
    {
        get => _gold;
        set
        {
            if (SetProperty(ref _gold, value) && !_suppressGoldWrite)
                _memory.WriteGold(value);
        }
    }


    private byte _storyline;
    private bool _suppressStorylineWrite;
    public byte Storyline
    {
        get => _storyline;
        set
        {
            if (SetProperty(ref _storyline, value))
            {
                OnPropertyChanged(nameof(StorylineHex));
                if (!_suppressStorylineWrite)
                    _memory.WriteStoryline(value);
            }
        }
    }

    public string StorylineHex
    {
        get => _storyline.ToString("X2");
        set
        {
            if (byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out byte parsed))
            {
                Storyline = parsed;
            }
            else
            {
                OnPropertyChanged(nameof(StorylineHex));
            }
        }
    }

    private string _storylineGameStateHex = "--";
    public string StorylineGameStateHex
    {
        get => _storylineGameStateHex;
        private set => SetProperty(ref _storylineGameStateHex, value);
    }

    private string _storylineBattleDataHex = "--";
    public string StorylineBattleDataHex
    {
        get => _storylineBattleDataHex;
        private set => SetProperty(ref _storylineBattleDataHex, value);
    }

    private bool _rngSeedAvailable;
    public bool RngSeedAvailable
    {
        get => _rngSeedAvailable;
        private set => SetProperty(ref _rngSeedAvailable, value);
    }

    private uint _rngSeed;
    private bool _suppressRngWrite;

    public string RngSeedHex
    {
        get => _rngSeed.ToString("X8");
        set
        {
            if (uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
            {
                if (_rngSeed != parsed)
                {
                    _rngSeed = parsed;
                    OnPropertyChanged(nameof(RngSeedHex));
                    if (!_suppressRngWrite)
                        _memory.WriteRngSeed(parsed);
                }
            }
            else
            {
                OnPropertyChanged(nameof(RngSeedHex)); // revert display to last valid value
            }
        }
    }

    private uint _playTimeSeconds;
    public uint PlayTimeSeconds
    {
        get => _playTimeSeconds;
        private set
        {
            if (SetProperty(ref _playTimeSeconds, value))
                OnPropertyChanged(nameof(PlayTimeDisplay));
        }
    }

    public string PlayTimeDisplay
    {
        get
        {
            uint s = _playTimeSeconds;
            uint hours = s / 3600;
            uint minutes = (s % 3600) / 60;
            uint seconds = s % 60;
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }
    }

    public ObservableCollection<InventorySlotViewModel> InventorySlots { get; } = new();

    private bool _showEmptySlots;
    public bool ShowEmptySlots
    {
        get => _showEmptySlots;
        set
        {
            if (SetProperty(ref _showEmptySlots, value))
                RefreshInventoryView();
        }
    }

    public ObservableCollection<string> AvailableCategoryNames { get; }

    public ObservableCollection<ItemOption> AvailableItems { get; } = new();

    private int _pendingCategoryIndex;
    public int PendingCategoryIndex
    {
        get => _pendingCategoryIndex;
        set
        {
            if (SetProperty(ref _pendingCategoryIndex, value))
            {
                RefreshPendingItems();
                PendingItem = null;
            }
        }
    }

    private ItemOption? _pendingItem;
    public ItemOption? PendingItem
    {
        get => _pendingItem;
        set => SetProperty(ref _pendingItem, value);
    }

    private GameSnapshot? _snapshot;

    public bool HasSnapshot => _snapshot != null;

    public string SnapshotLabel => _snapshot != null
        ? $"Snapshot @ {_snapshot.CapturedAt:HH:mm:ss}"
        : "No snapshot";


    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand TakeSnapshotCommand { get; }
    public ICommand ApplySnapshotCommand { get; }
    public ICommand SaveSnapshotCommand { get; }
    public ICommand LoadSnapshotCommand { get; }

    private void OnConnect()
    {
        (bool success, string message) = _memory.TryAttach();
        StatusText = message;
        IsConnected = success;

        if (success)
        {
            RngSeedAvailable = _memory.IsRngSeedAvailable;
            RefreshAllData();
            if (AutoRefresh)
                _refreshTimer.Start();
        }
    }

    private void OnDisconnect()
    {
        _refreshTimer.Stop();
        _memory.Detach();
        IsConnected = false;
        StatusText = "Disconnected.";
    }


    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!_memory.CheckProcessAlive())
        {
            _refreshTimer.Stop();
            IsConnected = false;
            StatusText = "Game process exited. Disconnected.";
            return;
        }

        RefreshAllData();
    }

    private void RefreshAllData()
    {
        for (int i = 0; i < GameOffsets.CharacterCount; i++)
        {
            CharacterRecord? record = _memory.ReadCharacter(i);
            if (record != null)
                Characters[i].UpdateFromRecord(record);
        }

        _suppressBattleSpeedWrite = true;
        BattleSpeed = _memory.ReadBattleSpeed();
        _suppressBattleSpeedWrite = false;

        _suppressGoldWrite = true;
        Gold = _memory.ReadGold();
        _suppressGoldWrite = false;

        PlayTimeSeconds = _memory.ReadPlayTime();

        _suppressStorylineWrite = true;
        Storyline = _memory.ReadStoryline();
        StorylineGameStateHex = _memory.ReadStorylineGameState().ToString("X2");
        StorylineBattleDataHex = _memory.ReadStorylineBattleData().ToString("X2");
        _suppressStorylineWrite = false;

        _suppressPartyWrite = true;
        byte[] roster = _memory.ReadPartyRoster();
        PartySlot0 = roster[0];
        PartySlot1 = roster[1];
        PartySlot2 = roster[2];
        _suppressPartyWrite = false;

        // RNG Seed - retry finding the address each tick until rand() has been called
        // (CT only calls rand() on room transitions / battle starts / tech heals, so it
        // may be uninitialised at attach time).
        if (!_memory.IsRngSeedAvailable)
        {
            if (_memory.TryRefindRngSeed())
            {
                RngSeedAvailable = true;
                StatusText = $"RNG seed found: 0x{_memory.ReadRngSeed():X8}  [{_memory.RngDiagnostic}]";
            }
        }

        if (_memory.IsRngSeedAvailable)
        {
            _suppressRngWrite = true;
            _rngSeed = _memory.ReadRngSeed();
            OnPropertyChanged(nameof(RngSeedHex));
            _suppressRngWrite = false;
        }

        RefreshInventoryView();
    }

    private void RefreshInventoryView()
    {
        if (!_memory.IsAttached) return;

        List<InventorySlot> allSlots = _memory.ReadAllInventory();

        List<InventorySlot> visibleSlots = _showEmptySlots
            ? allSlots
            : allSlots.Where(s => !s.IsEmpty).ToList();

        while (InventorySlots.Count > visibleSlots.Count)
            InventorySlots.RemoveAt(InventorySlots.Count - 1);

        for (int i = 0; i < visibleSlots.Count; i++)
        {
            InventorySlot slot = visibleSlots[i];
            if (i < InventorySlots.Count)
            {
                InventorySlotViewModel existing = InventorySlots[i];
                if (existing.SlotIndex == slot.SlotIndex)
                {
                    existing.UpdateFromSlot(slot);
                }
                else
                {
                    InventorySlotViewModel vm = new(_memory, slot.SlotIndex);
                    vm.UpdateFromSlot(slot);
                    InventorySlots[i] = vm;
                }
            }
            else
            {
                InventorySlotViewModel vm = new(_memory, slot.SlotIndex);
                vm.UpdateFromSlot(slot);
                InventorySlots.Add(vm);
            }
        }
    }

    private void OnTakeSnapshot()
    {
        GameSnapshot snap = new()
        {
            CapturedAt  = DateTime.Now,
            PartyRoster = _memory.ReadPartyRoster(),
            Inventory   = _memory.ReadAllInventory().ToArray(),
            Gold        = _memory.ReadGold(),
            BattleSpeed = _memory.ReadBattleSpeed(),
            Storyline   = _memory.ReadStoryline(),
        };

        for (int i = 0; i < GameOffsets.CharacterCount; i++)
            snap.Characters[i] = _memory.ReadCharacter(i) ?? new CharacterRecord();

        _snapshot = snap;
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(SnapshotLabel));
        StatusText = $"Snapshot taken at {snap.CapturedAt:HH:mm:ss}.";
    }

    private void OnApplySnapshot()
    {
        if (_snapshot == null) return;

        for (int i = 0; i < GameOffsets.CharacterCount; i++)
            _memory.WriteCharacterRecord(i, _snapshot.Characters[i]);

        for (int i = 0; i < 3; i++)
            _memory.WritePartySlot(i, _snapshot.PartyRoster[i]);

        // Inventory: write each slot from the snapshot at its original index,
        // clearing slots that were empty in the snapshot.
        foreach (InventorySlot slot in _snapshot.Inventory)
        {
            if (slot.IsEmpty)
                _memory.ClearInventorySlot(slot.SlotIndex);
            else
            {
                _memory.WriteInventorySlot(slot.SlotIndex, slot.ItemIndex, slot.Category);
                _memory.WriteInventorySlotQuantity(slot.SlotIndex, slot.Quantity);
            }
        }

        _memory.WriteGold(_snapshot.Gold);
        _memory.WriteBattleSpeed(_snapshot.BattleSpeed);
        _memory.WriteStoryline(_snapshot.Storyline);

        StatusText = $"Snapshot from {_snapshot.CapturedAt:HH:mm:ss} applied.";
        RefreshAllData();
    }

    private void OnSaveSnapshot()
    {
        if (_snapshot == null) return;

        SaveFileDialog dlg = new()
        {
            Title            = "Save Snapshot",
            Filter           = "CT Snapshots (*.ctsnapshot)|*.ctsnapshot",
            DefaultExt       = ".ctsnapshot",
            FileName         = $"snapshot_{_snapshot.CapturedAt:yyyyMMdd_HHmmss}",
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            SnapshotFileService.Save(_snapshot, dlg.FileName);
            StatusText = $"Snapshot saved to {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private void OnLoadSnapshot()
    {
        OpenFileDialog dlg = new()
        {
            Title  = "Load Snapshot",
            Filter = "CT Snapshots (*.ctsnapshot)|*.ctsnapshot",
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            _snapshot = SnapshotFileService.Load(dlg.FileName);
            OnPropertyChanged(nameof(HasSnapshot));
            OnPropertyChanged(nameof(SnapshotLabel));
            StatusText = $"Snapshot loaded from {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _memory.Dispose();
    }

    private void RefreshPendingItems()
    {
        AvailableItems.Clear();
        int catId = _pendingCategoryIndex;
        string[] names = ItemDatabase.GetCategoryItems(catId);
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            if (!string.IsNullOrEmpty(name) && !name.Equals("dummy", StringComparison.OrdinalIgnoreCase))
                AvailableItems.Add(new ItemOption((byte)i, name));
        }
    }

    private void OnAddItem()
    {
        if (PendingItem == null || PendingItem.Id == 0) return;
        int catId = _pendingCategoryIndex;
        byte itemId = PendingItem.Id;

        int slotIndex = _memory.FindFirstEmptySlotForCategory(catId);
        if (slotIndex < 0)
        {
            StatusText = $"Inventory full for category: {ItemDatabase.CategoryNames[catId]}";
            return;
        }

        byte categoryByte = (byte)(catId << 4);
        bool ok = _memory.WriteInventorySlot(slotIndex, itemId, categoryByte);
        if (ok)
        {
            StatusText = $"Added {ItemDatabase.GetItemName(catId, itemId)} to slot {slotIndex}.";
            RefreshInventoryView();
        }
        else
        {
            StatusText = "Failed to write inventory slot.";
        }
    }

    private void OnRemoveItem(InventorySlotViewModel? vm)
    {
        if (vm == null) return;
        bool ok = _memory.ClearInventorySlot(vm.SlotIndex);
        if (ok)
        {
            StatusText = $"Removed {vm.ItemName} from slot {vm.SlotIndex}.";
            RefreshInventoryView();
        }
        else
        {
            StatusText = "Failed to clear inventory slot.";
        }
    }
}
