using FrigoTracker.Core;

namespace FrigoTracker.Core.Tests;

public class InventoryRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    [Fact]
    public void Consume_decrements_and_reports_empty()
    {
        var eggs = new InventoryItem { Quantity = 2 };

        Assert.False(InventoryRules.Consume(eggs, 1));
        Assert.Equal(1, eggs.Quantity);
        Assert.True(InventoryRules.Consume(eggs, 1));
        Assert.Equal(0, eggs.Quantity);
    }

    [Fact]
    public void Consume_never_goes_negative_and_rounds()
    {
        var cheese = new InventoryItem { Quantity = 0.3 };

        Assert.False(InventoryRules.Consume(cheese, 0.1));
        Assert.Equal(0.2, cheese.Quantity);
        Assert.True(InventoryRules.Consume(cheese, 5));
        Assert.Equal(0, cheese.Quantity);
    }

    [Fact]
    public void Moving_to_freezer_starts_the_freezer_clock_and_drops_the_package_date()
    {
        var chicken = new InventoryItem
        {
            Category = FoodCategory.Poultry,
            Location = StorageLocation.Fridge,
            StoredOn = Today.AddDays(-2),
            ExpiresOn = Today.AddDays(1),
        };

        InventoryRules.MoveTo(chicken, StorageLocation.Freezer, Today);

        Assert.Equal(StorageLocation.Freezer, chicken.Location);
        Assert.Equal(Today, chicken.StoredOn);
        Assert.Null(chicken.ExpiresOn);
        Assert.Equal(Today.AddMonths(9), FreshnessCalculator.Deadline(chicken));
    }

    [Fact]
    public void Thawing_gives_the_short_fridge_life_of_the_category()
    {
        var ground = new InventoryItem
        {
            Category = FoodCategory.GroundMeat,
            Location = StorageLocation.Freezer,
            StoredOn = Today.AddMonths(-1),
        };

        InventoryRules.MoveTo(ground, StorageLocation.Fridge, Today);

        Assert.Equal(Today.AddDays(1), ground.ExpiresOn);
    }

    [Fact]
    public void Moving_to_the_same_place_changes_nothing()
    {
        var stored = Today.AddDays(-5);
        var item = new InventoryItem { Location = StorageLocation.Fridge, StoredOn = stored };

        InventoryRules.MoveTo(item, StorageLocation.Fridge, Today);

        Assert.Equal(stored, item.StoredOn);
    }

    [Fact]
    public void Grocery_duplicate_ignores_case_spaces_and_checked_lines()
    {
        var list = new List<GroceryItem>
        {
            new() { Name = "Beurre", Checked = true },
            new() { Name = "Lait" },
        };

        Assert.Same(list[1], GroceryRules.FindOpenDuplicate(list, "  lait "));
        Assert.Null(GroceryRules.FindOpenDuplicate(list, "beurre"));
    }
}
