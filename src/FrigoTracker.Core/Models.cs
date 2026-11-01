namespace FrigoTracker.Core;

/// <summary>Where an item is stored. Stored as text in Supabase, so never rename a member.</summary>
public enum StorageLocation
{
    Fridge,
    Freezer,
    Pantry,
}

/// <summary>Food family, used to pick the maximum freezer time. Stored as text: never rename a member.</summary>
public enum FoodCategory
{
    Meat,
    GroundMeat,
    Poultry,
    LeanFish,
    FattyFish,
    Seafood,
    ColdCuts,
    CookedDish,
    Dairy,
    Cheese,
    Bread,
    Vegetables,
    Fruits,
    Sauces,
    Other,
}

/// <summary>One line of the household inventory (one row of the frigo_items table).</summary>
public sealed class InventoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public FoodCategory Category { get; set; } = FoodCategory.Other;

    public StorageLocation Location { get; set; } = StorageLocation.Fridge;

    public double Quantity { get; set; } = 1;

    /// <summary>Free text: "unité", "g", "paquet", "portion"...</summary>
    public string Unit { get; set; } = "";

    /// <summary>Date the item entered its current location. For the freezer, this is the freezing date.</summary>
    public DateOnly StoredOn { get; set; }

    /// <summary>
    /// Best-before date from the package, or a manual deadline. In the freezer it is usually empty and the
    /// deadline comes from <see cref="StorageGuide"/>; when set there, it overrides the guide.
    /// </summary>
    public DateOnly? ExpiresOn { get; set; }

    public string? Notes { get; set; }

    /// <summary>Last change, set by the client and overwritten by the database trigger.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public InventoryItem Clone() => (InventoryItem)MemberwiseClone();
}

/// <summary>One line of the shared grocery list (one row of the frigo_grocery table).</summary>
public sealed class GroceryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Free text ("2", "1 kg"...), optional.</summary>
    public string? Quantity { get; set; }

    public bool Checked { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
