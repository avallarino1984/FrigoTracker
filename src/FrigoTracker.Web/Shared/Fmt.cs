using System.Globalization;
using FrigoTracker.Core;

namespace FrigoTracker.Web.Shared;

/// <summary>French display helpers shared by the pages.</summary>
public static class Fmt
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", Fr);

    /// <summary>"2", "0,5", "1,25": no useless decimals.</summary>
    public static string Qty(double quantity) => quantity.ToString("0.##", Fr);

    public static string QtyUnit(InventoryItem item) =>
        string.IsNullOrWhiteSpace(item.Unit) ? $"× {Qty(item.Quantity)}" : $"{Qty(item.Quantity)} {item.Unit}";

    /// <summary>"Congélateur · congelé il y a 8 mois", "Frigo · depuis 3 jours".</summary>
    public static string Where(InventoryItem item, DateOnly today)
    {
        string place = StorageGuide.LocationLabel(item.Location);
        int days = today.DayNumber - item.StoredOn.DayNumber;
        if (item.Location == StorageLocation.Freezer)
        {
            int months = FreshnessCalculator.MonthsBetween(item.StoredOn, today);
            return months > 0 ? $"{place} · congelé il y a {months} mois" : $"{place} · congelé il y a {Math.Max(days, 0)} j";
        }
        if (days <= 0) return $"{place} · ajouté aujourd'hui";
        return $"{place} · depuis {days} j";
    }

    /// <summary>Quantity on the label: "×3" for counted things, "500 g" when the unit is a measure.</summary>
    public static string LabelQty(InventoryItem item)
    {
        string unit = item.Unit.Trim().ToLowerInvariant();
        bool counted = unit is "" or "unité" or "unités" or "paquet" or "paquets" or "portion" or "portions";
        return counted ? $"×{Qty(item.Quantity)}" : $"{Qty(item.Quantity)} {item.Unit.Trim()}";
    }

    /// <summary>"Congelé le 20 août", "Rangé le 3 nov. 2025" (year only when not the current one), "Rangé aujourd'hui".</summary>
    public static string Age(InventoryItem item, DateOnly today)
    {
        string verb = item.Location == StorageLocation.Freezer ? "Congelé" : "Rangé";
        if (item.StoredOn == today) return $"{verb} aujourd'hui";
        return $"{verb} le {ShortDate(item.StoredOn, today)}";
    }

    /// <summary>"20 août" this year, "20 août 2025" otherwise.</summary>
    public static string ShortDate(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "d MMM" : "d MMM yyyy", Fr);

    public static string LevelCss(FreshnessLevel level)
    {
        if (level == FreshnessLevel.Expired) return "expired";
        if (level == FreshnessLevel.Soon) return "soon";
        if (level == FreshnessLevel.Fresh) return "fresh";
        return "unknown";
    }

    public static string LocationIcon(StorageLocation location)
    {
        if (location == StorageLocation.Freezer) return "❄︎";
        if (location == StorageLocation.Pantry) return "▤";
        return "◫";
    }
}
