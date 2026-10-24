using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PclVersionBridge.Core.Friends;

/// <summary>The supplied HTTP handler must disable automatic redirects. The store must be scoped to ServiceUri.</summary>
public sealed class FriendApiClient : IFriendService, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    private readonly HttpClient _http;
    private readonly FriendServiceOptions _options;
    private readonly IFriendSessionStore _store;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private FriendSession? _session;
    private int _generation;
    private bool _remember;
    private bool _disposed;
    public FriendSession? Session => Volatile.Read(ref _session);

    public FriendApiClient(HttpClient http, FriendServiceOptions options, IFriendSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(http); ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(store);
        if (http.DefaultRequestHeaders.Authorization is not null)
            throw new ArgumentException("好友专用 HTTP 客户端不能使用默认身份凭据。", nameof(http));
        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(options));
        _http = http; _options = options; _store = store;
    }

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    public async Task RestoreAsync(CancellationToken token)
    {
        var generation = await BeginIdentityAsync(token);
        try
        {
            var saved = await _store.ReadAsync(token);
            if (saved is null) return;
            ValidateSession(saved, false);
            // Always check the saved refresh credential with the authority before restoring a UI account.
            var session = await SendAsync<FriendSession>(HttpMethod.Post, "api/account/refresh", new FriendRefresh(saved.RefreshToken), null, token);
            if (session!.Me.UserId != saved.Me.UserId) throw InvalidResponse();
            await SaveSessionAsync(session!, true, generation, token);
        }
        catch (FriendServiceException e) when (e.StatusCode == HttpStatusCode.Unauthorized || e.Code == "invalid_response")
        { await InvalidateAsync(generation); throw; }
    }

    public Task RegisterAsync(RegisterFriendAccount request, CancellationToken token) => PublicMutation("register", request, token);
    public Task ConfirmEmailAsync(ConfirmFriendEmail request, CancellationToken token) => PublicMutation("confirm", request, token);
    public Task ResendConfirmationAsync(FriendEmail request, CancellationToken token) => PublicMutation("resend-confirmation", request, token);
    public Task ForgotPasswordAsync(FriendEmail request, CancellationToken token) => PublicMutation("forgot-password", request, token);
    public Task ResetPasswordAsync(ResetFriendPassword request, CancellationToken token) => PublicMutation("reset-password", request, token);
    private Task PublicMutation(string route, object body, CancellationToken token) => SendAsync<object>(HttpMethod.Post, "api/account/" + route, body, null, token, false);

    public async Task LoginAsync(FriendLogin request, bool remember, CancellationToken token)
    {
        var generation = await BeginIdentityAsync(token);
        await ClearStoreAsync(generation);
        var session = await SendAsync<FriendSession>(HttpMethod.Post, "api/account/login", request, null, token);
        await SaveSessionAsync(session!, remember, generation, token);
    }

    public Task LogoutAsync(CancellationToken token) => EndSessionAsync("logout", new { }, token);
    public Task LogoutAllAsync(CancellationToken token) => EndSessionAsync("logout-all", new { }, token);
    public Task ChangePasswordAsync(ChangeFriendPassword request, CancellationToken token) => EndSessionAsync("change-password", request, token);
    public Task DeleteAccountAsync(DeleteFriendAccount request, CancellationToken token) => EndSessionAsync("delete", request, token);

    private async Task EndSessionAsync(string route, object body, CancellationToken token)
    {
        var oldSession = Session;
        // Logout is local immediately, even if the remote server is unreachable.
        if (route is "logout" or "logout-all")
        {
            var generation = await BeginIdentityAsync(CancellationToken.None);
            await ClearStoreAsync(generation);
            if (oldSession is not null) await RevokeRetiringSessionAsync(route, body, oldSession, token);
        }
        else
        {
            var generation = Volatile.Read(ref _generation);
            await AuthAsync<object>(HttpMethod.Post, "api/account/" + route, body, token, false);
            await InvalidateAsync(generation);
        }
    }

    private async Task RevokeRetiringSessionAsync(string route, object body, FriendSession retiring, CancellationToken token)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Post, "api/account/" + route, body, retiring.AccessToken, token, false);
            return;
        }
        catch (FriendServiceException e) when (e.StatusCode == HttpStatusCode.Unauthorized) { }

        // Refresh only the credentials being retired. Never restore them locally or borrow
        // a later login's credentials while this revocation is still awaiting the server.
        var fresh = await SendAsync<FriendSession>(HttpMethod.Post, "api/account/refresh", new FriendRefresh(retiring.RefreshToken), null, token);
        if (fresh!.Me.UserId != retiring.Me.UserId) throw InvalidResponse();
        await SendAsync<object>(HttpMethod.Post, "api/account/" + route, body, fresh.AccessToken, token, false);
    }

    public Task<FriendProfile> GetMeAsync(CancellationToken token) => Required<FriendProfile>(HttpMethod.Get, "api/me", null, token);
    public async Task<FriendProfile> UpdateProfileAsync(UpdateFriendProfile request, CancellationToken token)
    {
        var generation = Volatile.Read(ref _generation);
        var profile = await Required<FriendProfile>(HttpMethod.Patch, "api/me", request, token);
        await UpdateMeAsync(profile, generation, token); return profile;
    }
    public async Task<FriendProfile> ResetFriendCodeAsync(CancellationToken token)
    {
        var generation = Volatile.Read(ref _generation);
        var profile = await Required<FriendProfile>(HttpMethod.Post, "api/me/reset-code", new { }, token);
        await UpdateMeAsync(profile, generation, token); return profile;
    }
    public Task<FriendProfile> LookupAsync(string code, CancellationToken token) => Required<FriendProfile>(HttpMethod.Get, "api/profiles/" + NormalizeCode(code), null, token);
    public Task<FriendPage<FriendProfile>> GetFriendsAsync(int page, CancellationToken token) => GetPageAsync<FriendProfile>("friends", page, token);
    public Task<FriendPage<FriendRequest>> GetRequestsAsync(int page, CancellationToken token) => GetPageAsync<FriendRequest>("friend-requests", page, token);
    public Task<FriendPage<FriendProfile>> GetBlocksAsync(int page, CancellationToken token) => GetPageAsync<FriendProfile>("blocks", page, token);
    public Task<FriendPage<PackInvitation>> GetInvitationsAsync(int page, CancellationToken token) => GetPageAsync<PackInvitation>("invitations", page, token);
    public Task SendRequestAsync(string code, CancellationToken token) => Mutation(HttpMethod.Post, "api/friend-requests", new CreateFriendRequest(NormalizeCode(code)), token);
    public Task ResolveRequestAsync(string id, string action, CancellationToken token) => Mutation(HttpMethod.Post, "api/friend-requests/" + Id(id) + "/" + Action(action, "accept", "reject", "withdraw"), new { }, token);
    public Task RemoveFriendAsync(string userId, CancellationToken token) => Mutation(HttpMethod.Delete, "api/friends/" + Id(userId), null, token);
    public Task BlockAsync(string userId, CancellationToken token) => Mutation(HttpMethod.Put, "api/blocks/" + Id(userId), new { }, token);
    public Task UnblockAsync(string userId, CancellationToken token) => Mutation(HttpMethod.Delete, "api/blocks/" + Id(userId), null, token);
    public Task<PackInvitation> GetInvitationAsync(string id, CancellationToken token) => Required<PackInvitation>(HttpMethod.Get, "api/invitations/" + Id(id), null, token);
    public Task<PackInvitation> SendInvitationAsync(SendPackInvitation request, CancellationToken token) => Required<PackInvitation>(HttpMethod.Post, "api/invitations", request, token);
    public Task ResolveInvitationAsync(string id, string action, CancellationToken token) => Mutation(HttpMethod.Post, "api/invitations/" + Id(id) + "/" + Action(action, "ignore", "withdraw"), new { }, token);

    private async Task<FriendPage<T>> GetPageAsync<T>(string path, int page, CancellationToken token)
    {
        if (page is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(page));
        var result = await Required<FriendPage<T>>(HttpMethod.Get, "api/" + path + "?page=" + page, null, token);
        if (result.Page != page || result.Total < 0 || result.Items is null || result.Items.Count > 20 || result.Items.Any(x => x is null)) throw InvalidResponse();
        return result;
    }
    private async Task<T> Required<T>(HttpMethod method, string path, object? body, CancellationToken token) => (await AuthAsync<T>(method, path, body, token))!;
    private Task Mutation(HttpMethod method, string path, object? body, CancellationToken token) => AuthAsync<object>(method, path, body, token, false);
    private async Task<T?> AuthAsync<T>(HttpMethod method, string path, object? body, CancellationToken token, bool readBody = true)
    {
        var generation = Volatile.Read(ref _generation);
        var session = await GetValidSessionAsync(token);
        // Waiting for another refresh can span logout/login. Never submit the old action under a new account.
        EnsureCurrent(generation);
        try
        {
            var result = await SendAsync<T>(method, path, body, session.AccessToken, token, readBody);
            EnsureCurrent(generation); return result;
        }
        catch (FriendServiceException e) when (e.StatusCode == HttpStatusCode.Unauthorized)
        { await InvalidateAsync(generation); throw; }
    }

    private async Task<FriendSession> GetValidSessionAsync(CancellationToken token)
    {
        var current = Session ?? throw Unauthorized();
        if (current.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30)) return current;
        await _refreshGate.WaitAsync(token);
        try
        {
            current = Session ?? throw Unauthorized();
            if (current.ExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30)) return current;
            var generation = Volatile.Read(ref _generation);
            try
            {
                var fresh = await SendAsync<FriendSession>(HttpMethod.Post, "api/account/refresh", new FriendRefresh(current.RefreshToken), null, token);
                if (fresh!.Me.UserId != current.Me.UserId) throw InvalidResponse();
                await SaveSessionAsync(fresh, _remember, generation, token); return fresh;
            }
            catch (FriendServiceException e) when (e.StatusCode == HttpStatusCode.Unauthorized || e.Code == "invalid_response")
            { await InvalidateAsync(generation); throw; }
        }
        finally { _refreshGate.Release(); }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string? accessToken, CancellationToken token, bool readBody = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var uri = new Uri(_options.ServiceUri, path);
        if (!SameOrigin(uri)) throw new InvalidOperationException("Invalid friends service origin.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (body is not null) request.Content = JsonContent.Create(body, options: Json);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400 || response.RequestMessage?.RequestUri is { } final && (!SameOrigin(final) || final != uri))
                throw new FriendServiceException("redirect", "好友服务返回了不允许的跳转。");
            if (!response.IsSuccessStatusCode)
            {
                // Invalid authentication is authoritative at the headers. A broken error body must not keep credentials alive.
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new FriendServiceException("unauthorized", SafeMessage(response.StatusCode), response.StatusCode);
                var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                var error = await ReadErrorAsync(response, timeout.Token);
                throw new FriendServiceException(error?.Code is { Length: <= 64 } code ? code : "service_error", SafeMessage(response.StatusCode), response.StatusCode,
                    retry is >= 0 and <= 86400 ? (int)retry.Value : error?.RetryAfterSeconds is >= 0 and <= 86400 ? error.RetryAfterSeconds : null);
            }
            if (!readBody) return default;
            var bytes = await ReadBoundedAsync(response.Content, 262_144, timeout.Token);
            if (response.Content.Headers.ContentType?.MediaType is not "application/json") throw InvalidResponse();
            var value = JsonSerializer.Deserialize<T>(bytes, Json);
            if (value is null) throw InvalidResponse();
            if (value is FriendSession session) ValidateSession(session);
            if (value is FriendProfile profile) ValidateProfile(profile);
            return value;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        { throw new FriendServiceException("timeout", "好友服务响应超时，请稍后重试。"); }
        catch (HttpRequestException) { throw new FriendServiceException("network", "好友服务暂时无法连接，请稍后重试。"); }
        catch (JsonException) { throw InvalidResponse(); }
        catch (IOException) { throw new FriendServiceException("network", "好友服务暂时无法连接，请稍后重试。"); }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw InvalidResponse();
        await using var stream = await content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, token);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > limit) throw InvalidResponse();
            buffer.Write(chunk, 0, count);
        }
    }
    private static async Task<FriendApiError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken token)
    {
        try { return JsonSerializer.Deserialize<FriendApiError>(await ReadBoundedAsync(response.Content, 8192, token), Json); }
        catch (Exception e) when (e is JsonException or FriendServiceException) { return null; }
    }
    private bool SameOrigin(Uri uri) => uri.Scheme == _options.ServiceUri.Scheme && uri.Host == _options.ServiceUri.Host && uri.Port == _options.ServiceUri.Port && uri.UserInfo.Length == 0;
    private static string SafeMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "登录已失效，请重新登录。",
        HttpStatusCode.Forbidden => "当前账号无权进行此操作。",
        HttpStatusCode.NotFound => "未找到该资料或邀请，可能已失效。",
        HttpStatusCode.Conflict => "状态已发生变化，请刷新后重试。",
        HttpStatusCode.TooManyRequests => "操作过于频繁，请稍后重试。",
        HttpStatusCode.BadRequest => "提交的信息无效，请检查后重试。",
        _ => "好友服务暂时无法完成操作，请稍后重试。"
    };
    private static FriendServiceException InvalidResponse() => new("invalid_response", "好友服务返回的数据无效，请稍后重试。");
    private static FriendServiceException Unauthorized() => new("unauthorized", "请先登录好友账号。", HttpStatusCode.Unauthorized);
    private static void ValidateProfile(FriendProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.UserId) || profile.UserId.Length > 200 || string.IsNullOrWhiteSpace(profile.DisplayName) || profile.DisplayName.Length > 100 ||
            profile.FriendCode is null || profile.FriendCode.Length != 12 || profile.FriendCode.Any(c => !"0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c))) throw InvalidResponse();
    }
    private static void ValidateSession(FriendSession session, bool requireFresh = true)
    {
        if (string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken.Length > 16384 || session.AccessToken.Any(char.IsWhiteSpace) ||
            string.IsNullOrWhiteSpace(session.RefreshToken) || session.RefreshToken.Length > 16384 || session.Me is null || (requireFresh && session.ExpiresAtUtc <= DateTimeOffset.UtcNow)) throw InvalidResponse();
        ValidateProfile(session.Me);
    }
    private static string NormalizeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var value = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (value.Length != 12 || value.Any(c => !"0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c))) throw new ArgumentException("请输入完整的 12 位好友码。", nameof(code));
        return value;
    }
    private static string Id(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("无效的好友记录标识。", nameof(id));
        return Uri.EscapeDataString(id);
    }
    private static string Action(string action, params string[] allowed) => allowed.Contains(action) ? action : throw new ArgumentException("无效的好友操作。", nameof(action));

    private async Task<int> BeginIdentityAsync(CancellationToken token)
    {
        await _stateGate.WaitAsync(token);
        try { ObjectDisposedException.ThrowIf(_disposed, this); _session = null; _remember = false; return ++_generation; }
        finally { _stateGate.Release(); }
    }
    private void EnsureCurrent(int generation)
    { if (generation != Volatile.Read(ref _generation) || _disposed) throw new OperationCanceledException("好友账号已改变。"); }
    private async Task SaveSessionAsync(FriendSession session, bool remember, int generation, CancellationToken token)
    {
        ValidateSession(session);
        await _stateGate.WaitAsync(token);
        try
        {
            EnsureCurrent(generation);
            if (remember) await _store.WriteAsync(session, token); else await _store.ClearAsync(token);
            token.ThrowIfCancellationRequested(); EnsureCurrent(generation); _remember = remember; _session = session;
        }
        finally { _stateGate.Release(); }
    }
    private async Task ClearStoreAsync(int generation)
    {
        await _stateGate.WaitAsync();
        try { if (generation == _generation) await _store.ClearAsync(CancellationToken.None); }
        finally { _stateGate.Release(); }
    }
    private async Task InvalidateAsync(int generation)
    {
        await _stateGate.WaitAsync();
        try
        {
            if (generation != _generation) return;
            _generation++; _session = null; _remember = false; await _store.ClearAsync(CancellationToken.None);
        }
        finally { _stateGate.Release(); }
    }
    private async Task UpdateMeAsync(FriendProfile profile, int generation, CancellationToken token)
    {
        var session = Session;
        if (session is null || session.Me.UserId != profile.UserId) throw InvalidResponse();
        await SaveSessionAsync(session with { Me = profile }, _remember, generation, token);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Interlocked.Increment(ref _generation); _session = null; _lifetime.Cancel();
        // HttpClient is owned by the composition root. Durable remembered credentials survive normal app close.
    }
}
