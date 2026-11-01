namespace FrigoTracker.Core;

/// <summary>Storage rules for one food family.</summary>
/// <param name="Label">French label shown in the app.</param>
/// <param name="FreezerMonths">Maximum recommended time in the freezer (quality limit at -18 °C).</param>
/// <param name="ThawedFridgeDays">Days the item keeps in the fridge once thawed.</param>
public sealed record CategoryInfo(FoodCategory Category, string Label, int FreezerMonths, int ThawedFridgeDays);

/// <summary>
/// Indicative freezer times, based on the usual MAPAQ / Health Canada charts, rounded to the conservative side.
/// Adjust the numbers here: everything else (alerts, deadlines, the guide shown in Settings) follows.
/// </summary>
public static class StorageGuide
{
    public static readonly IReadOnlyList<CategoryInfo> All =
    [
        new(FoodCategory.Meat, "Viande (bœuf, porc, agneau, veau)", 8, 3),
        new(FoodCategory.GroundMeat, "Viande hachée", 3, 1),
        new(FoodCategory.Poultry, "Volaille", 9, 2),
        new(FoodCategory.LeanFish, "Poisson maigre (morue, tilapia, sole)", 6, 1),
        new(FoodCategory.FattyFish, "Poisson gras (saumon, truite, maquereau)", 3, 1),
        new(FoodCategory.Seafood, "Fruits de mer", 3, 1),
        new(FoodCategory.ColdCuts, "Charcuterie, saucisses, bacon", 2, 3),
        new(FoodCategory.CookedDish, "Plat cuisiné, restes", 3, 3),
        new(FoodCategory.Dairy, "Lait, beurre, crème", 3, 5),
        new(FoodCategory.Cheese, "Fromage", 3, 7),
        new(FoodCategory.Bread, "Pain, viennoiseries", 3, 4),
        new(FoodCategory.Vegetables, "Légumes", 10, 3),
        new(FoodCategory.Fruits, "Fruits", 10, 3),
        new(FoodCategory.Sauces, "Sauces, bouillons, soupes", 4, 4),
        new(FoodCategory.Other, "Autre", 6, 3),
    ];

    private static readonly Dictionary<FoodCategory, CategoryInfo> ByCategory = All.ToDictionary(c => c.Category);

    public static CategoryInfo For(FoodCategory category) =>
        ByCategory.TryGetValue(category, out var info) ? info : ByCategory[FoodCategory.Other];

    public static string LocationLabel(StorageLocation location)
    {
        if (location == StorageLocation.Freezer) return "Congélateur";
        if (location == StorageLocation.Pantry) return "Garde-manger";
        return "Frigo";
    }
}
