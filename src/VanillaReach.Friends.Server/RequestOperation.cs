using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace VanillaReach.Friends.Server;

// One SQLite writer at a time. External I/O runs outside the gate, followed by a fresh transaction.
internal sealed class RequestOperation(FriendsDb db, RequestGate gate) : IAsyncDisposable
{
    private IDbContextTransaction? transaction;
    private bool held;
    public async Task Begin(CancellationToken ct)
    { await gate.Lock.WaitAsync(ct); held = true; transaction = await db.Database.BeginTransactionAsync(ct); }
    public async Task Commit() { if (transaction is not null) await transaction.CommitAsync(); }
    public async Task Rollback() { if (transaction is not null) await transaction.RollbackAsync(); }
    public async Task External(Func<Task> action)
    {
        if (transaction is not null) { await transaction.CommitAsync(); await transaction.DisposeAsync(); transaction = null; }
        if (held) { held = false; gate.Lock.Release(); }
        try { await action(); }
        finally
        {
            await gate.Lock.WaitAsync(); held = true;
            // Another request may have deleted, blocked, or logged out this account during I/O.
            db.ChangeTracker.Clear(); transaction = await db.Database.BeginTransactionAsync();
        }
    }
    public async ValueTask DisposeAsync()
    { if (transaction is not null) { await transaction.DisposeAsync(); transaction = null; } if (held) { held = false; gate.Lock.Release(); } }
}
