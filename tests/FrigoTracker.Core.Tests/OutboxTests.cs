using FrigoTracker.Core;

namespace FrigoTracker.Core.Tests;

public class OutboxTests
{
    private const string Items = "frigo_items";

    private sealed record Row(Guid Id, string Value);

    [Fact]
    public void Last_change_of_a_row_wins()
    {
        var id = Guid.NewGuid();
        var pending = new List<PendingChange>();

        Outbox.Enqueue(pending, new(Items, id, ChangeKind.Upsert));
        Outbox.Enqueue(pending, new(Items, id, ChangeKind.Upsert));
        Outbox.Enqueue(pending, new(Items, id, ChangeKind.Delete));

        Assert.Equal(new PendingChange(Items, id, ChangeKind.Delete), Assert.Single(pending));
    }

    [Fact]
    public void Same_id_in_another_table_is_a_different_row()
    {
        var id = Guid.NewGuid();
        var pending = new List<PendingChange>();

        Outbox.Enqueue(pending, new(Items, id, ChangeKind.Upsert));
        Outbox.Enqueue(pending, new("frigo_grocery", id, ChangeKind.Upsert));

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public void Pull_keeps_pending_local_edits_and_pending_deletes()
    {
        var edited = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        var createdOffline = Guid.NewGuid();

        var local = new List<Row> { new(edited, "local"), new(untouched, "old"), new(createdOffline, "new") };
        var remote = new List<Row> { new(edited, "cloud"), new(deleted, "cloud"), new(untouched, "cloud") };
        var pending = new List<PendingChange>
        {
            new(Items, edited, ChangeKind.Upsert),
            new(Items, deleted, ChangeKind.Delete),
            new(Items, createdOffline, ChangeKind.Upsert),
        };

        var merged = Outbox.MergePulled(local, remote, pending, Items, r => r.Id);

        Assert.DoesNotContain(merged, r => r.Id == deleted);
        Assert.Contains(new Row(edited, "local"), merged);
        Assert.Contains(new Row(untouched, "cloud"), merged);
        Assert.Contains(new Row(createdOffline, "new"), merged);
        Assert.Equal(3, merged.Count);
    }
}
