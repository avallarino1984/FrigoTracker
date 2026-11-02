namespace FrigoTracker.Core;

public enum FreshnessLevel
{
    /// <summary>No date known (fridge or pantry item without a best-before date).</summary>
    Unknown,
    Fresh,

    /// <summary>Inside the warning window of its location.</summary>
    Soon,
    Expired,
}

/// <param name="Deadline">Date to eat it by, or null if unknown.</param>
/// <param name="DaysLeft">Days until the deadline (negative once passed), or null if unknown.</param>
public sealed record Freshness(DateOnly? Deadline, int? DaysLeft, FreshnessLevel Level);

/// <summary>Expiry rules: deadline per item, warning windows, and the list shown in the alert popup.</summary>
public static class FreshnessCalculator
{
    public const int FridgeWarningDays = 3;
    public const int PantryWarningDays = 7;

    /// <summary>The freezer warns a month ahead, so there is time to plan a meal around the item.</summary>
    public const int FreezerWarningDays = 30;

    public static int WarningDays(StorageLocation location)
    {
        if (location == StorageLocation.Freezer) return FreezerWarningDays;
        if (location == StorageLocation.Pantry) return PantryWarningDays;
        return FridgeWarningDays;
    }

    /// <summary>
    /// Deadline of an item: the explicit date if any; otherwise, in the freezer, freezing date + guide months.
    /// </summary>
    public static DateOnly? Deadline(InventoryItem item)
    {
        if (item.ExpiresOn is { } explicitDate)
        {
            return explicitDate;
        }
        if (item.Location == StorageLocation.Freezer)
        {
            return item.StoredOn.AddMonths(StorageGuide.For(item.Category).FreezerMonths);
        }
        return null;
    }

    public static Freshness Evaluate(InventoryItem item, DateOnly today)
    {
        if (Deadline(item) is not { } deadline)
        {
            return new Freshness(null, null, FreshnessLevel.Unknown);
        }

        int daysLeft = deadline.DayNumber - today.DayNumber;
        var level = FreshnessLevel.Fresh;
        if (daysLeft < 0)
        {
            level = FreshnessLevel.Expired;
        }
        else if (daysLeft <= WarningDays(item.Location))
        {
            level = FreshnessLevel.Soon;
        }
        return new Freshness(deadline, daysLeft, level);
    }

    /// <summary>Expired or soon-to-expire items, the most urgent first.</summary>
    public static IReadOnlyList<(InventoryItem Item, Freshness Freshness)> NeedsAttention(
        IEnumerable<InventoryItem> items, DateOnly today) =>
        items
            .Select(i => (Item: i, Freshness: Evaluate(i, today)))
            .Where(x => x.Freshness.Level is FreshnessLevel.Expired or FreshnessLevel.Soon)
            .OrderBy(x => x.Freshness.DaysLeft)
            .ThenBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Share of the storage life already used: 0 when stored today, 1 at the deadline, above 1 once expired.
    /// Null when the item has no deadline.
    /// </summary>
    public static double? ElapsedShare(InventoryItem item, DateOnly today)
    {
        if (Deadline(item) is not { } deadline)
        {
            return null;
        }
        int total = deadline.DayNumber - item.StoredOn.DayNumber;
        int elapsed = today.DayNumber - item.StoredOn.DayNumber;
        if (total <= 0)
        {
            // Deadline on or before the storage day: already at the limit.
            return elapsed >= total ? 1 : 0;
        }
        return Math.Max(0, (double)elapsed / total);
    }

    /// <summary>Whole months elapsed between two dates ("congelé depuis 8 mois").</summary>
    public static int MonthsBetween(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0;
        }
        int months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        // Not a full month yet if the day of month was not reached (Mar 15 -> Apr 14 is 0 month).
        if (from.AddMonths(months) > to)
        {
            months--;
        }
        return months;
    }

    /// <summary>Short French description of the deadline, for the item list.</summary>
    public static string Describe(Freshness freshness)
    {
        if (freshness.DaysLeft is not { } days)
        {
            return "Sans date";
        }
        if (days < -1) return $"Expiré depuis {-days} jours";
        if (days == -1) return "Expiré depuis hier";
        if (days == 0) return "À manger aujourd'hui";
        if (days == 1) return "Expire demain";
        if (days < 60) return $"Encore {days} jours";
        return $"Encore {days / 30} mois";
    }
}
