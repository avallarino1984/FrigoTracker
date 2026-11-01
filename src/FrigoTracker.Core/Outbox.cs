namespace FrigoTracker.Core;

public enum ChangeKind
{
    Upsert,
    Delete,
}

/// <summary>
/// A local change not yet sent to Supabase. Only the row id is kept: the content is read from the local
/// state at send time, so several edits of the same row become a single upload.
/// </summary>
public sealed record PendingChange(string Table, Guid Id, ChangeKind Kind);

/// <summary>Queue of local changes, sent in order when the network is available.</summary>
public static class Outbox
{
    /// <summary>Adds a change, replacing any older pending change of the same row (the last one wins).</summary>
    public static void Enqueue(List<PendingChange> pending, PendingChange change)
    {
        pending.RemoveAll(c => c.Table == change.Table && c.Id == change.Id);
        pending.Add(change);
    }

    /// <summary>Ids of rows with a pending change in a table: a cloud pull must not overwrite them.</summary>
    public static HashSet<Guid> PendingIds(IEnumerable<PendingChange> pending, string table) =>
        pending.Where(c => c.Table == table).Select(c => c.Id).ToHashSet();

    /// <summary>
    /// Merges rows pulled from the cloud with local rows: cloud rows win, except rows with a pending local change
    /// (kept as they are locally, or kept deleted).
    /// </summary>
    public static List<T> MergePulled<T>(
        IReadOnlyList<T> local, IReadOnlyList<T> remote, IReadOnlyList<PendingChange> pending, string table,
        Func<T, Guid> idOf)
    {
        var pendingIds = PendingIds(pending, table);
        var result = remote.Where(r => !pendingIds.Contains(idOf(r))).ToList();
        var deleted = pending.Where(c => c.Table == table && c.Kind == ChangeKind.Delete).Select(c => c.Id).ToHashSet();
        result.AddRange(local.Where(l => pendingIds.Contains(idOf(l)) && !deleted.Contains(idOf(l))));
        return result;
    }
}
