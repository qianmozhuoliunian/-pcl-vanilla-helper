using System.ComponentModel;
using System.Net;

namespace PclVersionBridge.Core.Friends;

/// <summary>UI-facing authoritative state. All mutations wait for the service; notes never enter the service transport.</summary>
public sealed class FriendsViewModel(IFriendService service, IFriendNotesStore notesStore) : INotifyPropertyChanged, IDisposable
{
    private readonly IFriendService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly IFriendNotesStore _notesStore = notesStore ?? throw new ArgumentNullException(nameof(notesStore));
    private CancellationTokenSource? _pending;
    private int _generation;
    private string? _accountId;
    private bool _disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsLoggedIn => Me is not null && _service.Session?.Me.UserId == Me.UserId;
    public FriendProfile? Me { get; private set; }
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "登录后可添加好友并发送指定版本的整合包邀请。";
    public string Error { get; private set; } = "";
    public DateTimeOffset? LastSynchronizedAtUtc { get; private set; }
    public FriendProfile? LookupResult { get; private set; }
    public IReadOnlyList<FriendProfile> Friends { get; private set; } = [];
    public IReadOnlyList<FriendRequest> Requests { get; private set; } = [];
    public IReadOnlyList<FriendProfile> Blocks { get; private set; } = [];
    public IReadOnlyList<PackInvitation> Invitations { get; private set; } = [];
    public IReadOnlyDictionary<string, string> Notes { get; private set; } = new Dictionary<string, string>();
    public int FriendsPage { get; private set; }
    public int RequestsPage { get; private set; }
    public int BlocksPage { get; private set; }
    public int InvitationsPage { get; private set; }
    public int FriendsTotal { get; private set; }
    public int RequestsTotal { get; private set; }
    public int BlocksTotal { get; private set; }
    public int InvitationsTotal { get; private set; }

    public Task InitializeAsync(CancellationToken token = default) => RunAsync(async ct =>
    {
        await _service.RestoreAsync(ct);
        return _service.Session is null ? () => { } : await FetchSnapshotAsync(ct);
    }, "好友资料已同步。", token, true);

    public Task LoginAsync(FriendLogin request, bool remember, CancellationToken token = default)
    {
        CancelPending(); ClearCaches(); Notify();
        return RunAsync(async ct => { await _service.LoginAsync(request, remember, ct); return await FetchSnapshotAsync(ct); }, "已登录，好友资料已同步。", token, true);
    }

    public Task RegisterAsync(RegisterFriendAccount request, CancellationToken token = default) => PublicAction(ct => _service.RegisterAsync(request, ct), "注册请求已提交，请查看验证邮件。", token);
    public Task ConfirmEmailAsync(ConfirmFriendEmail request, CancellationToken token = default) => PublicAction(ct => _service.ConfirmEmailAsync(request, ct), "邮箱已验证，可以登录。", token);
    public Task ResendConfirmationAsync(FriendEmail request, CancellationToken token = default) => PublicAction(ct => _service.ResendConfirmationAsync(request, ct), "若邮箱可接收验证邮件，请查看收件箱。", token);
    public Task ForgotPasswordAsync(FriendEmail request, CancellationToken token = default) => PublicAction(ct => _service.ForgotPasswordAsync(request, ct), "若邮箱可用于找回密码，请查看收件箱。", token);
    public Task ResetPasswordAsync(ResetFriendPassword request, CancellationToken token = default) => PublicAction(ct => _service.ResetPasswordAsync(request, ct), "密码已重置，请重新登录。", token);

    public Task LogoutAsync(CancellationToken token = default) => LogoutCoreAsync(false, token);
    public Task LogoutAllAsync(CancellationToken token = default) => LogoutCoreAsync(true, token);
    private async Task LogoutCoreAsync(bool all, CancellationToken token)
    {
        if (_disposed) return;
        CancelPending(); ClearCaches();
        var generation = _generation;
        IsBusy = true; Error = ""; Status = "正在退出…"; Notify();
        Exception? failure = null;
        try { if (all) await _service.LogoutAllAsync(token); else await _service.LogoutAsync(token); }
        catch (Exception e) { failure = e; }
        try { await _notesStore.ClearAsync(CancellationToken.None); }
        catch (Exception e) { failure ??= e; }
        if (_disposed || generation != _generation) return;
        IsBusy = false;
        Status = failure is null ? "已退出登录。" : "已清除本机登录，服务端退出未能确认。";
        Error = failure is null ? "" : SafeError(failure); Notify();
    }

    public Task ChangePasswordAsync(ChangeFriendPassword request, CancellationToken token = default) => EndAccountAsync(ct => _service.ChangePasswordAsync(request, ct), "密码已修改，请重新登录。", token);
    public Task DeleteAccountAsync(DeleteFriendAccount request, CancellationToken token = default) => EndAccountAsync(ct => _service.DeleteAccountAsync(request, ct), "账号已删除。", token);
    private Task EndAccountAsync(Func<CancellationToken, Task> action, string status, CancellationToken token) => RunAsync(async ct =>
    {
        await action(ct); await _notesStore.ClearAsync(ct); return ClearCaches;
    }, status, token, true);

    public Task RefreshAsync(CancellationToken token = default) => RunAsync(FetchSnapshotAsync, "好友资料已同步。", token);
    public Task LookupAsync(string code, CancellationToken token = default) => RunAsync(async ct =>
    {
        var result = await _service.LookupAsync(code, ct); return () => LookupResult = result;
    }, "请核对昵称和完整好友码，再发送申请。", token);

    public Task UpdateProfileAsync(UpdateFriendProfile request, CancellationToken token = default) => RunAsync(async ct =>
    {
        var result = await _service.UpdateProfileAsync(request, ct); return () => Me = result;
    }, "昵称已更新。", token);
    public Task ResetFriendCodeAsync(CancellationToken token = default) => RunAsync(async ct =>
    {
        var result = await _service.ResetFriendCodeAsync(ct); return () => Me = result;
    }, "好友码已重置，旧码已失效。", token);

    public Task LoadFriendsAsync(int page, CancellationToken token = default) => RunAsync(async ct =>
    {
        var result = await _service.GetFriendsAsync(page, ct); return () => ApplyFriends(result);
    }, "好友列表已同步。", token);
    public Task LoadRequestsAsync(int page, CancellationToken token = default) => RunAsync(async ct =>
    {
        var result = await _service.GetRequestsAsync(page, ct); return () => ApplyRequests(result);
    }, "申请列表已同步。", token);
    public Task LoadBlocksAsync(int page, CancellationToken token = default) => RunAsync(async ct =>
    {
        var result = await _service.GetBlocksAsync(page, ct); return () => ApplyBlocks(result);
    }, "屏蔽列表已同步。", token);
    public Task LoadInvitationsAsync(int page, CancellationToken token = default) => RunAsync(async ct =>
    {
        var result = await _service.GetInvitationsAsync(page, ct); return () => ApplyInvitations(result);
    }, "整合包邀请已同步。", token);

    public Task SendRequestAsync(string code, CancellationToken token = default) => Mutation(ct => _service.SendRequestAsync(code, ct), "申请已提交，等待对方同意。", token);
    public Task ResolveRequestAsync(string id, string action, CancellationToken token = default) => Mutation(ct => _service.ResolveRequestAsync(id, action, ct), "申请状态已更新。", token);
    public Task RemoveFriendAsync(string userId, CancellationToken token = default) => Mutation(ct => _service.RemoveFriendAsync(userId, ct), "已删除好友。", token);
    public Task BlockAsync(string userId, CancellationToken token = default) => Mutation(ct => _service.BlockAsync(userId, ct), "已屏蔽该账号。", token);
    public Task UnblockAsync(string userId, CancellationToken token = default) => Mutation(ct => _service.UnblockAsync(userId, ct), "已解除屏蔽；好友关系需要重新申请。", token);
    public Task SendInvitationAsync(SendPackInvitation request, CancellationToken token = default) => Mutation(async ct => { await _service.SendInvitationAsync(request, ct); }, "整合包邀请已发送。", token);
    public Task ResolveInvitationAsync(string id, string action, CancellationToken token = default) => Mutation(ct => _service.ResolveInvitationAsync(id, action, ct), "邀请状态已更新。", token);
    public async Task<PackInvitation?> GetInvitationAsync(string id, CancellationToken token = default)
    {
        PackInvitation? result = null;
        await RunAsync(async ct =>
        {
            var invitation = await _service.GetInvitationAsync(id, ct);
            if (invitation.State != "pending" || invitation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                throw new FriendServiceException("invitation_inactive", "此邀请已失效，请刷新列表。");
            return () => result = invitation;
        }, "邀请权限已核对，继续检查指定版本。", token);
        return result;
    }
    public Task SetNoteAsync(string userId, string note, CancellationToken token = default) => RunAsync(async ct =>
    {
        var owner = _service.Session?.Me.UserId ?? throw new FriendServiceException("unauthorized", "请先登录好友账号。", HttpStatusCode.Unauthorized);
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 200 || note.Length > 200) throw new ArgumentException("备注最多 200 字。请检查好友与备注。");
        var updated = new Dictionary<string, string>(Notes);
        if (string.IsNullOrWhiteSpace(note)) updated.Remove(userId); else updated[userId] = note.Trim();
        await _notesStore.WriteAsync(owner, updated, ct); return () => Notes = updated;
    }, "本机备注已保存。", token);

    private Task PublicAction(Func<CancellationToken, Task> action, string status, CancellationToken token) => RunAsync(async ct => { await action(ct); return () => { }; }, status, token);
    private Task Mutation(Func<CancellationToken, Task> action, string status, CancellationToken token) => RunAsync(async ct =>
    {
        await action(ct); return await FetchSnapshotAsync(ct);
    }, status, token);

    private async Task<Action> FetchSnapshotAsync(CancellationToken token)
    {
        var me = await _service.GetMeAsync(token);
        var friends = await _service.GetFriendsAsync(FriendsPage, token);
        var requests = await _service.GetRequestsAsync(RequestsPage, token);
        var blocks = await _service.GetBlocksAsync(BlocksPage, token);
        var invitations = await _service.GetInvitationsAsync(InvitationsPage, token);
        var notes = await _notesStore.ReadAsync(me.UserId, token);
        return () =>
        {
            Me = me; ApplyFriends(friends); ApplyRequests(requests); ApplyBlocks(blocks); ApplyInvitations(invitations);
            Notes = new Dictionary<string, string>(notes); LastSynchronizedAtUtc = DateTimeOffset.UtcNow;
        };
    }
    private void ApplyFriends(FriendPage<FriendProfile> page) { Friends = page.Items; FriendsPage = page.Page; FriendsTotal = page.Total; }
    private void ApplyRequests(FriendPage<FriendRequest> page) { Requests = page.Items; RequestsPage = page.Page; RequestsTotal = page.Total; }
    private void ApplyBlocks(FriendPage<FriendProfile> page) { Blocks = page.Items; BlocksPage = page.Page; BlocksTotal = page.Total; }
    private void ApplyInvitations(FriendPage<PackInvitation> page) { Invitations = page.Items; InvitationsPage = page.Page; InvitationsTotal = page.Total; }

    private async Task RunAsync(Func<CancellationToken, Task<Action>> action, string success, CancellationToken token, bool changesIdentity = false)
    {
        if (_disposed || IsBusy) return;
        if (!changesIdentity) SyncIdentity();
        var account = _service.Session?.Me.UserId;
        var generation = ++_generation;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token);
        _pending = pending; IsBusy = true; Error = ""; Status = "正在连接好友服务…"; Notify();
        try
        {
            var apply = await action(pending.Token);
            if (!Current(generation)) return;
            pending.Token.ThrowIfCancellationRequested();
            if (!changesIdentity && account != _service.Session?.Me.UserId) { SyncIdentity(); Status = "账号已改变，请重新同步。"; return; }
            SyncIdentity(); apply(); Status = success;
        }
        catch (OperationCanceledException)
        { if (Current(generation)) { SyncIdentity(); Status = "操作已取消。"; } }
        catch (Exception e)
        {
            if (!Current(generation)) return;
            SyncIdentity(); Error = SafeError(e);
            Status = LastSynchronizedAtUtc.HasValue && IsLoggedIn ? "同步未完成，显示上次同步的资料。" : "操作未完成。";
        }
        finally
        {
            if (Current(generation)) { IsBusy = false; _pending = null; Notify(); }
        }
    }
    private static string SafeError(Exception e) => e switch
    {
        FriendServiceException friend => friend.Message,
        ArgumentException => "提交的信息无效，请检查后重试。",
        OperationCanceledException => "操作已取消。",
        _ => "操作未完成，请稍后重试。"
    };
    private bool Current(int generation) => !_disposed && generation == _generation;
    private void SyncIdentity()
    {
        var session = _service.Session;
        if (session?.Me.UserId != _accountId) { ClearCaches(); _accountId = session?.Me.UserId; }
        Me = session?.Me;
    }
    private void ClearCaches()
    {
        _accountId = null; Me = null; LookupResult = null;
        Friends = []; Requests = []; Blocks = []; Invitations = []; Notes = new Dictionary<string, string>();
        FriendsPage = RequestsPage = BlocksPage = InvitationsPage = 0;
        FriendsTotal = RequestsTotal = BlocksTotal = InvitationsTotal = 0;
        LastSynchronizedAtUtc = null; Error = "";
    }
    public void CancelPending()
    {
        _generation++; _pending?.Cancel(); _pending = null; IsBusy = false;
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    public void Dispose()
    {
        if (_disposed) return;
        CancelPending(); _disposed = true; ClearCaches(); Notify();
    }
}
