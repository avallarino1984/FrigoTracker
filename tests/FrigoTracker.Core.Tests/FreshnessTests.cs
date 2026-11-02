using FrigoTracker.Core;

namespace FrigoTracker.Core.Tests;

public class FreshnessTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    private static InventoryItem Item(StorageLocation location, FoodCategory category, DateOnly storedOn, DateOnly? expiresOn = null) =>
        new() { Name = "Test", Location = location, Category = category, StoredOn = storedOn, ExpiresOn = expiresOn };

    [Fact]
    public void Freezer_meat_deadline_is_freezing_date_plus_guide_months()
    {
        var steak = Item(StorageLocation.Freezer, FoodCategory.Meat, new DateOnly(2026, 3, 1));

        Assert.Equal(new DateOnly(2026, 11, 1), FreshnessCalculator.Deadline(steak));
    }

    [Fact]
    public void Freezer_meat_frozen_eight_months_ago_is_expired()
    {
        // Frozen Feb 27: the 8-month limit was Oct 27, four days ago.
        var steak = Item(StorageLocation.Freezer, FoodCategory.Meat, new DateOnly(2026, 2, 27));

        var f = FreshnessCalculator.Evaluate(steak, Today);

        Assert.Equal(FreshnessLevel.Expired, f.Level);
        Assert.Equal(-4, f.DaysLeft);
    }

    [Fact]
    public void Freezer_warns_a_month_ahead()
    {
        // 7 months and a few days in the freezer: 8-month limit reached in less than 30 days.
        var steak = Item(StorageLocation.Freezer, FoodCategory.Meat, Today.AddMonths(-7).AddDays(-10));

        Assert.Equal(FreshnessLevel.Soon, FreshnessCalculator.Evaluate(steak, Today).Level);
    }

    [Fact]
    public void Ground_meat_has_a_shorter_freezer_life_than_steak()
    {
        var frozenOn = Today.AddMonths(-4);
        var ground = Item(StorageLocation.Freezer, FoodCategory.GroundMeat, frozenOn);
        var steak = Item(StorageLocation.Freezer, FoodCategory.Meat, frozenOn);

        Assert.Equal(FreshnessLevel.Expired, FreshnessCalculator.Evaluate(ground, Today).Level);
        Assert.Equal(FreshnessLevel.Fresh, FreshnessCalculator.Evaluate(steak, Today).Level);
    }

    [Fact]
    public void Explicit_date_overrides_the_freezer_guide()
    {
        var item = Item(StorageLocation.Freezer, FoodCategory.Meat, Today, expiresOn: Today.AddDays(2));

        Assert.Equal(Today.AddDays(2), FreshnessCalculator.Deadline(item));
    }

    [Fact]
    public void Fridge_item_without_date_is_unknown()
    {
        var milk = Item(StorageLocation.Fridge, FoodCategory.Dairy, Today);

        var f = FreshnessCalculator.Evaluate(milk, Today);

        Assert.Equal(FreshnessLevel.Unknown, f.Level);
        Assert.Null(f.DaysLeft);
    }

    [Theory]
    [InlineData(4, FreshnessLevel.Fresh)]
    [InlineData(3, FreshnessLevel.Soon)]
    [InlineData(0, FreshnessLevel.Soon)]
    [InlineData(-1, FreshnessLevel.Expired)]
    public void Fridge_warning_window_is_three_days(int daysLeft, FreshnessLevel expected)
    {
        var yogurt = Item(StorageLocation.Fridge, FoodCategory.Dairy, Today, expiresOn: Today.AddDays(daysLeft));

        Assert.Equal(expected, FreshnessCalculator.Evaluate(yogurt, Today).Level);
    }

    [Fact]
    public void Needs_attention_lists_most_urgent_first_and_skips_fresh_items()
    {
        var items = new[]
        {
            Item(StorageLocation.Fridge, FoodCategory.Dairy, Today, Today.AddDays(2)),
            Item(StorageLocation.Fridge, FoodCategory.Dairy, Today, Today.AddDays(-3)),
            Item(StorageLocation.Fridge, FoodCategory.Dairy, Today, Today.AddDays(20)),
        };

        var list = FreshnessCalculator.NeedsAttention(items, Today);

        Assert.Equal(new[] { -3, 2 }, list.Select(x => x.Freshness.DaysLeft!.Value));
    }

    [Theory]
    [InlineData("2026-03-15", "2026-04-14", 0)]
    [InlineData("2026-03-15", "2026-04-15", 1)]
    [InlineData("2026-01-31", "2026-02-28", 1)]
    [InlineData("2026-02-28", "2026-10-31", 8)]
    [InlineData("2026-11-01", "2026-10-31", 0)]
    public void Months_between_counts_whole_months(string from, string to, int expected)
    {
        Assert.Equal(expected, FreshnessCalculator.MonthsBetween(DateOnly.Parse(from), DateOnly.Parse(to)));
    }

    [Fact]
    public void Elapsed_share_goes_from_zero_to_past_one()
    {
        // Salmon frozen May 10, 3-month limit on Aug 10: well past the limit on Oct 31.
        var salmon = Item(StorageLocation.Freezer, FoodCategory.FattyFish, new DateOnly(2026, 5, 10));
        var fresh = Item(StorageLocation.Freezer, FoodCategory.Meat, Today);

        Assert.True(FreshnessCalculator.ElapsedShare(salmon, Today) > 1);
        Assert.Equal(0, FreshnessCalculator.ElapsedShare(fresh, Today));
    }

    [Fact]
    public void Elapsed_share_is_null_without_a_deadline()
    {
        var milk = Item(StorageLocation.Fridge, FoodCategory.Dairy, Today);

        Assert.Null(FreshnessCalculator.ElapsedShare(milk, Today));
    }

    [Fact]
    public void Every_category_has_a_guide_entry()
    {
        foreach (var category in Enum.GetValues<FoodCategory>())
        {
            Assert.Equal(category, StorageGuide.For(category).Category);
        }
    }
}
