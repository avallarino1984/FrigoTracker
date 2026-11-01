namespace FrigoTracker.Core;

/// <summary>Pure inventory operations, kept out of the UI so they can be tested.</summary>
public static class InventoryRules
{
    /// <summary>Removes an amount; returns true when nothing is left (the item should be deleted).</summary>
    public static bool Consume(InventoryItem item, double amount)
    {
        if (amount <= 0)
        {
            return false;
        }
        // Round to avoid 0.30000000000000004-style leftovers.
        item.Quantity = Math.Round(Math.Max(0, item.Quantity - amount), 3);
        return item.Quantity <= 0;
    }

    /// <summary>
    /// Moves an item and resets its dates:
    /// into the freezer, the freezing date starts the clock and the package date no longer applies;
    /// out of the freezer (thawing), the item gets the short fridge life of its category.
    /// </summary>
    public static void MoveTo(InventoryItem item, StorageLocation target, DateOnly today)
    {
        if (item.Location == target)
        {
            return;
        }

        var previous = item.Location;
        item.Location = target;
        item.StoredOn = today;

        if (target == StorageLocation.Freezer)
        {
            item.ExpiresOn = null;
        }
        else if (previous == StorageLocation.Freezer)
        {
            item.ExpiresOn = today.AddDays(StorageGuide.For(item.Category).ThawedFridgeDays);
        }
    }
}

/// <summary>Shared grocery list helpers.</summary>
public static class GroceryRules
{
    /// <summary>Lower-case, trimmed, accents kept: "  Lait " and "lait" are the same line.</summary>
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    /// <summary>
    /// Returns the unchecked line with the same name if there is one (so it is not added twice), otherwise null.
    /// </summary>
    public static GroceryItem? FindOpenDuplicate(IEnumerable<GroceryItem> list, string name)
    {
        string key = Normalize(name);
        return list.FirstOrDefault(g => !g.Checked && Normalize(g.Name) == key);
    }
}
