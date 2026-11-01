using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FrigoTracker.Core;
using Microsoft.JSInterop;

namespace FrigoTracker.Web.Services;

/// <summary>The shared household this device works in.</summary>
public sealed class Household
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string JoinCode { get; set; } = "";
}

/// <summary>Local copy of everything, kept in localStorage so the app opens instantly and works offline.</summary>
public sealed class LocalData
{
    public int Version { get; set; } = 1;
    public Household? Household { get; set; }
    public List<InventoryItem> Items { get; set; } = [];
    public List<GroceryItem> Grocery { get; set; } = [];

    /// <summary>Changes made on this device and not yet accepted by Supabase.</summary>
    public List<PendingChange> Outbox { get; set; } = [];

    /// <summary>Day the expiry popup was last closed, so it shows at most once a day.</summary>
    public DateOnly? AlertsDismissedOn { get; set; }
}

public enum SyncStatus
{
    /// <summary>Not signed in (or Supabase not configured): data stays on this device.</summary>
    LocalOnly,

    /// <summary>Signed in but not part of a household yet.</summary>
    NoHousehold,
    Syncing,
    Synced,

    /// <summary>Some changes could not be sent (offline, expired session...). Retried on the next sync.</summary>
    Pending,
}

/// <summary>
/// App state: every change is applied locally first, queued in the outbox, then sent row by row to Supabase.
/// A pull replaces the local rows with the household's rows, except rows with a change still pending.
/// Pages only talk to this class.
/// </summary>
public sealed class AppState(IJSRuntime js, SupabaseClient cloud)
{
    private const string StorageKey = "frigo-tracker:v1";
    public const string ItemsTable = "frigo_items";
    public const string GroceryTable = "frigo_grocery";

    /// <summary>Local storage format (PascalCase, enums as text).</summary>
    private static readonly JsonSerializerOptions LocalJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Database row format: snake_case columns, enums as text (never rename an enum member).</summary>
    private static readonly JsonSerializerOptions RowJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
    };

    private LocalData _data = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public bool IsLoaded { get; private set; }
    public SyncStatus Sync { get; private set; } = SyncStatus.LocalOnly;
    public string? SyncError { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public bool CloudAvailable => cloud.IsConfigured;
    public string? CloudEmail => cloud.Email;
    public Household? Household => _data.Household;
    public int PendingCount => _data.Outbox.Count;

    /// <summary>Raised after any change so components can re-render.</summary>
    public event Action? Changed;

    public IReadOnlyList<InventoryItem> Items => _data.Items;
    public IReadOnlyList<GroceryItem> Grocery => _data.Grocery;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public IReadOnlyList<(InventoryItem Item, Freshness Freshness)> Alerts =>
        FreshnessCalculator.NeedsAttention(_data.Items, Today);

    /// <summary>True when the expiry popup should open: something to look at, and not closed yet today.</summary>
    public bool ShouldShowAlertPopup => _data.AlertsDismissedOn != Today && Alerts.Count > 0;

    public async Task LoadAsync()
    {
        if (IsLoaded)
        {
            return;
        }
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _data = JsonSerializer.Deserialize<LocalData>(json, LocalJson) ?? new LocalData();
                // Lists missing from older saves.
                _data.Items ??= [];
                _data.Grocery ??= [];
                _data.Outbox ??= [];
            }
        }
        catch (Exception ex)
        {
            // Corrupt or unavailable storage: start fresh rather than crash.
            Console.Error.WriteLine($"Failed to load data: {ex.Message}");
            _data = new LocalData();
        }
        IsLoaded = true;

        // Show local data immediately; the cloud pull happens in the background.
        _ = SyncAsync();
    }

    public InventoryItem? FindItem(Guid id) => _data.Items.FirstOrDefault(i => i.Id == id);

    // ---------- Inventory ----------

    /// <summary>Adds a new item or replaces the existing one with the same id.</summary>
    public async Task SaveItemAsync(InventoryItem item)
    {
        item.Name = item.Name.Trim();
        item.UpdatedAt = DateTimeOffset.UtcNow;
        int index = _data.Items.FindIndex(i => i.Id == item.Id);
        if (index >= 0)
        {
            _data.Items[index] = item;
        }
        else
        {
            _data.Items.Add(item);
        }
        await CommitAsync(new PendingChange(ItemsTable, item.Id, ChangeKind.Upsert));
    }

    public async Task DeleteItemAsync(Guid id)
    {
        _data.Items.RemoveAll(i => i.Id == id);
        await CommitAsync(new PendingChange(ItemsTable, id, ChangeKind.Delete));
    }

    /// <summary>Eats some of an item; returns true when it is finished (and removed).</summary>
    public async Task<bool> ConsumeAsync(Guid id, double amount)
    {
        if (FindItem(id) is not { } item)
        {
            return false;
        }
        if (InventoryRules.Consume(item, amount))
        {
            await DeleteItemAsync(id);
            return true;
        }
        await SaveItemAsync(item);
        return false;
    }

    public async Task MoveItemAsync(Guid id, StorageLocation target)
    {
        if (FindItem(id) is { } item)
        {
            InventoryRules.MoveTo(item, target, Today);
            await SaveItemAsync(item);
        }
    }

    public async Task DismissAlertsAsync()
    {
        _data.AlertsDismissedOn = Today;
        await SaveLocalAsync();
        Changed?.Invoke();
    }

    // ---------- Grocery list ----------

    /// <summary>Adds a line, unless the same name is already on the list and not checked.</summary>
    public async Task AddGroceryAsync(string name, string? quantity = null)
    {
        if (string.IsNullOrWhiteSpace(name) || GroceryRules.FindOpenDuplicate(_data.Grocery, name) is not null)
        {
            return;
        }
        var line = new GroceryItem
        {
            Name = name.Trim(),
            Quantity = string.IsNullOrWhiteSpace(quantity) ? null : quantity.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _data.Grocery.Add(line);
        await CommitAsync(new PendingChange(GroceryTable, line.Id, ChangeKind.Upsert));
    }

    public async Task ToggleGroceryAsync(Guid id)
    {
        if (_data.Grocery.FirstOrDefault(g => g.Id == id) is { } line)
        {
            line.Checked = !line.Checked;
            line.UpdatedAt = DateTimeOffset.UtcNow;
            await CommitAsync(new PendingChange(GroceryTable, id, ChangeKind.Upsert));
        }
    }

    public async Task DeleteGroceryAsync(Guid id)
    {
        _data.Grocery.RemoveAll(g => g.Id == id);
        await CommitAsync(new PendingChange(GroceryTable, id, ChangeKind.Delete));
    }

    public async Task ClearCheckedGroceryAsync()
    {
        var done = _data.Grocery.Where(g => g.Checked).Select(g => g.Id).ToList();
        _data.Grocery.RemoveAll(g => g.Checked);
        foreach (var id in done)
        {
            Outbox.Enqueue(_data.Outbox, new PendingChange(GroceryTable, id, ChangeKind.Delete));
        }
        await CommitAsync(null);
    }

    // ---------- Account and household ----------

    public async Task SignInAsync(string email, string password)
    {
        await cloud.SignInWithPasswordAsync(email, password);
        await SyncAsync();
    }

    /// <summary>Signs out and forgets the household's data on this device (the cloud keeps it).</summary>
    public async Task SignOutAsync()
    {
        await cloud.SignOutAsync();
        _data = new LocalData();
        await SaveLocalAsync();
        SetStatus(SyncStatus.LocalOnly);
    }

    /// <summary>Creates a household; items entered before signing in are uploaded into it.</summary>
    public async Task CreateHouseholdAsync(string name)
    {
        var row = await cloud.RpcAsync("frigo_create_household", new { p_name = name });
        await UseHouseholdAsync(row);
    }

    public async Task JoinHouseholdAsync(string code)
    {
        var row = await cloud.RpcAsync("frigo_join_household", new { p_code = code.Trim() });
        await UseHouseholdAsync(row);
    }

    private async Task UseHouseholdAsync(JsonNode? row)
    {
        _data.Household = row?.Deserialize<Household>(RowJson) ?? throw new SupabaseException("Foyer introuvable.");
        await SaveLocalAsync();
        await SyncAsync();
    }

    // ---------- Sync ----------

    /// <summary>Sends pending changes, then pulls the household's rows. Safe to call at any time.</summary>
    public async Task SyncAsync()
    {
        if (!cloud.IsConfigured || !await cloud.IsSignedInAsync())
        {
            SetStatus(SyncStatus.LocalOnly);
            return;
        }

        await _syncLock.WaitAsync();
        try
        {
            SetStatus(SyncStatus.Syncing);

            if (_data.Household is null && !await FindMyHouseholdAsync())
            {
                SetStatus(SyncStatus.NoHousehold);
                return;
            }

            await FlushOutboxAsync();
            await PullAsync();

            LastSyncedAt = DateTimeOffset.Now;
            SetStatus(SyncStatus.Synced);
        }
        catch (Exception ex)
        {
            SetStatus(await cloud.IsSignedInAsync() ? SyncStatus.Pending : SyncStatus.LocalOnly, ex.Message);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // Signed in on a new device: RLS only returns the households the user belongs to.
    private async Task<bool> FindMyHouseholdAsync()
    {
        var rows = await cloud.SelectAsync("frigo_households", "select=id,name,join_code&order=created_at&limit=1");
        if (rows.Count == 0)
        {
            return false;
        }
        _data.Household = rows[0].Deserialize<Household>(RowJson);
        await SaveLocalAsync();
        return _data.Household is not null;
    }

    private async Task FlushOutboxAsync()
    {
        var householdId = _data.Household!.Id;
        foreach (var change in _data.Outbox.ToList())
        {
            if (change.Kind == ChangeKind.Delete)
            {
                await cloud.DeleteAsync(change.Table, change.Id);
            }
            else if (FindRow(change) is { } row)
            {
                row["household_id"] = householdId.ToString();
                // Set by the database trigger.
                row.Remove("updated_at");
                await cloud.UpsertAsync(change.Table, row);
            }

            // Remove only this exact instance: if the row was edited again meanwhile, Enqueue added a new one.
            _data.Outbox.RemoveAll(c => ReferenceEquals(c, change));
            await SaveLocalAsync();
        }
    }

    private JsonObject? FindRow(PendingChange change)
    {
        object? entity = change.Table == ItemsTable
            ? _data.Items.FirstOrDefault(i => i.Id == change.Id)
            : _data.Grocery.FirstOrDefault(g => g.Id == change.Id);
        return entity is null ? null : JsonSerializer.SerializeToNode(entity, entity.GetType(), RowJson) as JsonObject;
    }

    private async Task PullAsync()
    {
        string query = $"household_id=eq.{_data.Household!.Id}&select=*";
        var items = (await cloud.SelectAsync(ItemsTable, query)).Deserialize<List<InventoryItem>>(RowJson) ?? [];
        var grocery = (await cloud.SelectAsync(GroceryTable, query + "&order=updated_at")).Deserialize<List<GroceryItem>>(RowJson) ?? [];

        _data.Items = Outbox.MergePulled(_data.Items, items, _data.Outbox, ItemsTable, i => i.Id);
        _data.Grocery = Outbox.MergePulled(_data.Grocery, grocery, _data.Outbox, GroceryTable, g => g.Id);
        await SaveLocalAsync();
    }

    // ---------- Persistence ----------

    private async Task CommitAsync(PendingChange? change)
    {
        if (change is not null)
        {
            Outbox.Enqueue(_data.Outbox, change);
        }
        await SaveLocalAsync();
        Changed?.Invoke();

        // Send in the background; a failure leaves the status "Pending" and the next sync retries.
        _ = SyncAsync();
    }

    private Task SaveLocalAsync() =>
        js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(_data, LocalJson)).AsTask();

    private void SetStatus(SyncStatus status, string? error = null)
    {
        Sync = status;
        SyncError = error;
        Changed?.Invoke();
    }
}
