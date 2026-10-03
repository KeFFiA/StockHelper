using CommunityToolkit.Mvvm.ComponentModel;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.Core.Entities;
using StockHelper.Core.Services;

namespace StockHelper.App.ViewModels;

/// <summary>
/// Quantity entered in the item's unit or in one of its packages ("2 × Банка 10 л" = 20 л).
/// Converts to the item's unit, which is what gets stored.
/// </summary>
public sealed partial class QuantityInput : ObservableObject
{
    private readonly IReadOnlyList<Unit> _allUnits;

    public QuantityInput(IReadOnlyList<Unit> allUnits, string? label = null)
    {
        _allUnits = allUnits;
        Label = label ?? Strings.Receipts_Quantity;
    }

    public string Label { get; }

    public event EventHandler? Changed;

    public IReadOnlyList<Unit> Units { get; private set; } = [];

    public Unit? ItemUnit { get; private set; }

    /// <summary>The item has package units to choose from.</summary>
    public bool HasChoice => Units.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint), nameof(IsItemUnit))]
    public partial Unit? Unit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint))]
    public partial string Text { get; set; } = string.Empty;

    public bool IsItemUnit => Unit is null || Unit.Id == ItemUnit?.Id;

    /// <summary>How many item units one selected unit contains.</summary>
    public decimal Factor => Unit is null || ItemUnit is null ? 1 : UnitConverter.ToItemUnits(1, Unit, ItemUnit);

    /// <summary>"= 20 л" when a package is selected.</summary>
    public string Hint => !IsItemUnit && TryGetItemQuantity(out var q) && ItemUnit is not null
        ? string.Format(Strings.Quantity_Converted, NumberInput.Format(q), ItemUnit.Name)
        : string.Empty;

    /// <summary>Unit to store as "entered in" (null when it is the item's own unit).</summary>
    public int? EnteredUnitId => IsItemUnit ? null : Unit?.Id;

    public decimal? EnteredQuantity => IsItemUnit ? null : NumberInput.TryParse(Text, out var v) ? v : null;

    /// <summary>Configures the input for an item; keeps the previous unit when it is still applicable.</summary>
    public void SetItem(Item? item, int? unitId = null, decimal? enteredQuantity = null, decimal? itemQuantity = null)
    {
        var itemUnit = item?.Unit is null ? null : _allUnits.FirstOrDefault(u => u.Id == item.UnitId) ?? item.Unit;
        ItemUnit = itemUnit;
        Units = itemUnit is null ? [] : UnitConverter.CompatibleUnits(itemUnit, _allUnits);
        OnPropertyChanged(nameof(Units));
        OnPropertyChanged(nameof(HasChoice));

        var preferred = unitId ?? Unit?.Id;
        Unit = Units.FirstOrDefault(u => u.Id == preferred) ?? itemUnit;

        if (enteredQuantity is { } entered && unitId is not null)
        {
            Text = NumberInput.Format(entered);
        }
        else if (itemQuantity is { } quantity)
        {
            Text = NumberInput.Format(IsItemUnit || itemUnit is null || Unit is null ? quantity : UnitConverter.FromItemUnits(quantity, Unit, itemUnit));
        }
    }

    public bool TryGetItemQuantity(out decimal quantity)
    {
        quantity = 0;
        if (!NumberInput.TryParse(Text, out var value))
        {
            return false;
        }

        quantity = Unit is null || ItemUnit is null ? value : UnitConverter.ToItemUnits(value, Unit, ItemUnit);
        return true;
    }

    partial void OnUnitChanged(Unit? value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnTextChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}
