using CTMemoryEditor.Models;
using CTMemoryEditor.Services;

namespace CTMemoryEditor.ViewModels;

/// <summary>
/// ViewModel for a single character slot. Exposes editable stat properties that
/// write to game memory immediately on change.
/// </summary>
public sealed class CharacterViewModel : ViewModelBase
{
    private readonly GameMemoryService _memory;
    private readonly int _charIndex;
    private bool _suppressWrites;

    public CharacterViewModel(GameMemoryService memory, int charIndex)
    {
        _memory = memory;
        _charIndex = charIndex;
        Name = CharacterRecord.GetName(charIndex);
    }

    public string Name { get; }
    public int CharacterIndex => _charIndex;

    // --- HP / MP ---

    private ushort _hpCurrent;
    public ushort HPCurrent
    {
        get => _hpCurrent;
        set
        {
            if (SetProperty(ref _hpCurrent, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.HPCurrent, value);
        }
    }

    private ushort _hpMax;
    public ushort HPMax
    {
        get => _hpMax;
        set
        {
            if (SetProperty(ref _hpMax, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.HPMax, value);
        }
    }

    private ushort _hpBase;
    public ushort HPBase
    {
        get => _hpBase;
        set
        {
            if (SetProperty(ref _hpBase, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.HPBase, value);
        }
    }

    private ushort _mpCurrent;
    public ushort MPCurrent
    {
        get => _mpCurrent;
        set
        {
            if (SetProperty(ref _mpCurrent, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.MPCurrent, value);
        }
    }

    private ushort _mpMax;
    public ushort MPMax
    {
        get => _mpMax;
        set
        {
            if (SetProperty(ref _mpMax, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.MPMax, value);
        }
    }

    // --- Base Stats (write both base + computed) ---

    private byte _strength;
    public byte Strength
    {
        get => _strength;
        set
        {
            if (SetProperty(ref _strength, value) && !_suppressWrites)
                _memory.WriteBaseAndComputedStat(_charIndex,
                    GameOffsets.Character.Strength, GameOffsets.Character.ComputedStrength, value);
        }
    }

    private byte _stamina;
    public byte Stamina
    {
        get => _stamina;
        set
        {
            if (SetProperty(ref _stamina, value) && !_suppressWrites)
                _memory.WriteBaseAndComputedStat(_charIndex,
                    GameOffsets.Character.Stamina, GameOffsets.Character.ComputedStamina, value);
        }
    }

    private byte _speed;
    public byte Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, value) && !_suppressWrites)
                _memory.WriteBaseAndComputedStat(_charIndex,
                    GameOffsets.Character.Speed, GameOffsets.Character.ComputedSpeed, value);
        }
    }

    private byte _magic;
    public byte Magic
    {
        get => _magic;
        set
        {
            if (SetProperty(ref _magic, value) && !_suppressWrites)
                _memory.WriteBaseAndComputedStat(_charIndex,
                    GameOffsets.Character.Magic, GameOffsets.Character.ComputedMagic, value);
        }
    }

    private byte _accuracy;
    public byte Accuracy
    {
        get => _accuracy;
        set
        {
            if (SetProperty(ref _accuracy, value) && !_suppressWrites)
                _memory.WriteBaseAndComputedStat(_charIndex,
                    GameOffsets.Character.Accuracy, GameOffsets.Character.ComputedAccuracy, value);
        }
    }

    private byte _evasion;
    public byte Evasion
    {
        get => _evasion;
        set
        {
            if (SetProperty(ref _evasion, value) && !_suppressWrites)
                _memory.WriteBaseAndComputedStat(_charIndex,
                    GameOffsets.Character.Evasion, GameOffsets.Character.ComputedEvasion, value);
        }
    }

    private byte _magicDefense;
    public byte MagicDefense
    {
        get => _magicDefense;
        set
        {
            if (SetProperty(ref _magicDefense, value) && !_suppressWrites)
                _memory.WriteBaseAndComputedStat(_charIndex,
                    GameOffsets.Character.MagicDefense, GameOffsets.Character.ComputedMagicDefense, value);
        }
    }

    // --- Level / XP ---

    private byte _level;
    public byte Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.Level, value);
        }
    }

    private uint _totalXP;
    public uint TotalXP
    {
        get => _totalXP;
        set
        {
            if (SetProperty(ref _totalXP, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.TotalXP, value);
        }
    }

    private ushort _xpToNextLevel;
    public ushort XPToNextLevel
    {
        get => _xpToNextLevel;
        set => SetProperty(ref _xpToNextLevel, value); // read-only display
    }

    // --- Computed Stats (read-only display) ---

    private byte _computedStrength;
    public byte ComputedStrength
    {
        get => _computedStrength;
        set => SetProperty(ref _computedStrength, value);
    }

    private byte _computedStamina;
    public byte ComputedStamina
    {
        get => _computedStamina;
        set => SetProperty(ref _computedStamina, value);
    }

    private byte _computedSpeed;
    public byte ComputedSpeed
    {
        get => _computedSpeed;
        set => SetProperty(ref _computedSpeed, value);
    }

    private byte _computedMagic;
    public byte ComputedMagic
    {
        get => _computedMagic;
        set => SetProperty(ref _computedMagic, value);
    }

    private byte _computedAccuracy;
    public byte ComputedAccuracy
    {
        get => _computedAccuracy;
        set => SetProperty(ref _computedAccuracy, value);
    }

    private byte _computedEvasion;
    public byte ComputedEvasion
    {
        get => _computedEvasion;
        set => SetProperty(ref _computedEvasion, value);
    }

    private byte _computedMagicDefense;
    public byte ComputedMagicDefense
    {
        get => _computedMagicDefense;
        set => SetProperty(ref _computedMagicDefense, value);
    }

    private byte _attackPower;
    public byte AttackPower
    {
        get => _attackPower;
        set => SetProperty(ref _attackPower, value);
    }

    private byte _defense;
    public byte Defense
    {
        get => _defense;
        set => SetProperty(ref _defense, value);
    }

    // --- Equipment ---

    private static readonly IReadOnlyList<EquipmentOption> _weaponOptions    = ItemDatabase.GetEquipmentOptions(0);
    private static readonly IReadOnlyList<EquipmentOption> _armorOptions     = ItemDatabase.GetEquipmentOptions(1);
    private static readonly IReadOnlyList<EquipmentOption> _helmetOptions    = ItemDatabase.GetEquipmentOptions(2);
    private static readonly IReadOnlyList<EquipmentOption> _accessoryOptions = ItemDatabase.GetEquipmentOptions(3);

    public IReadOnlyList<EquipmentOption> WeaponOptions    => _weaponOptions;
    public IReadOnlyList<EquipmentOption> ArmorOptions     => _armorOptions;
    public IReadOnlyList<EquipmentOption> HelmetOptions    => _helmetOptions;
    public IReadOnlyList<EquipmentOption> AccessoryOptions => _accessoryOptions;

    private ushort _weapon;
    public ushort Weapon
    {
        get => _weapon;
        set
        {
            if (SetProperty(ref _weapon, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.Weapon, value);
        }
    }

    private ushort _armor;
    public ushort Armor
    {
        get => _armor;
        set
        {
            if (SetProperty(ref _armor, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.Armor, value);
        }
    }

    private ushort _helmet;
    public ushort Helmet
    {
        get => _helmet;
        set
        {
            if (SetProperty(ref _helmet, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.Helmet, value);
        }
    }

    private ushort _accessory;
    public ushort Accessory
    {
        get => _accessory;
        set
        {
            if (SetProperty(ref _accessory, value) && !_suppressWrites)
                _memory.WriteCharacterField(_charIndex, GameOffsets.Character.Accessory, value);
        }
    }

    /// <summary>
    /// Updates all properties from a CharacterRecord without triggering memory writes.
    /// </summary>
    public void UpdateFromRecord(CharacterRecord record)
    {
        _suppressWrites = true;
        try
        {
            HPCurrent = record.HPCurrent;
            HPMax = record.HPMax;
            HPBase = record.HPBase;
            MPCurrent = record.MPCurrent;
            MPMax = record.MPMax;

            Strength = record.Strength;
            Stamina = record.Stamina;
            Speed = record.Speed;
            Magic = record.Magic;
            Accuracy = record.Accuracy;
            Evasion = record.Evasion;
            MagicDefense = record.MagicDefense;

            Level = record.Level;
            TotalXP = record.TotalXP;
            XPToNextLevel = record.XPToNextLevel;

            ComputedStrength = record.ComputedStrength;
            ComputedStamina = record.ComputedStamina;
            ComputedSpeed = record.ComputedSpeed;
            ComputedMagic = record.ComputedMagic;
            ComputedAccuracy = record.ComputedAccuracy;
            ComputedEvasion = record.ComputedEvasion;
            ComputedMagicDefense = record.ComputedMagicDefense;
            AttackPower = record.AttackPower;
            Defense = record.Defense;

            Weapon = record.Weapon;
            Armor = record.Armor;
            Helmet = record.Helmet;
            Accessory = record.Accessory;
        }
        finally
        {
            _suppressWrites = false;
        }
    }

}
