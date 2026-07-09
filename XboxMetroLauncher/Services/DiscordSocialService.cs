using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Discord.Sdk;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class DiscordSocialService : ISocialIntegrationService, IDisposable
{
    private static readonly NativeMethods.Discord_FreeFn? RootedFreeFn;
    private static readonly bool DelegateLifetimePatchApplied;

    static DiscordSocialService()
    {
        try
        {
            var userDataType = typeof(NativeMethods).GetNestedType("ManagedUserData", BindingFlags.NonPublic | BindingFlags.Public);
            var freeField = userDataType?.GetField("Free", BindingFlags.Public | BindingFlags.Static);
            var unmanagedFree = userDataType?.GetMethod("UnmanagedFree", BindingFlags.Public | BindingFlags.Static);
            if (freeField is null || unmanagedFree is null)
            {
                return;
            }

            RootedFreeFn = (NativeMethods.Discord_FreeFn)Delegate.CreateDelegate(typeof(NativeMethods.Discord_FreeFn), unmanagedFree);
            var pointer = Marshal.GetFunctionPointerForDelegate(RootedFreeFn);
            unsafe
            {
                freeField.SetValue(null, Pointer.Box(pointer.ToPointer(), typeof(void*)));
            }

            DelegateLifetimePatchApplied = true;
        }
        catch
        {
            // Connect/Restore refuse to run without the patch instead of
            // letting the SDK crash the whole launcher later.
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly TimeSpan AuthorizeTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CallbackPumpInterval = TimeSpan.FromMilliseconds(50);

    private readonly string _configPath;
    private readonly object _clientLock = new();
    private Client? _client;
    private Client.OnStatusChanged? _statusChangedHandler;
    private Client.UserUpdatedCallback? _userUpdatedHandler;
    private Client.RelationshipCreatedCallback? _relationshipCreatedHandler;
    private Client.RelationshipDeletedCallback? _relationshipDeletedHandler;
    private Client.MessageCreatedCallback? _messageCreatedHandler;
    private System.Threading.Timer? _callbackPump;
    private readonly object _messagesLock = new();
    private readonly Dictionary<ulong, List<DiscordDmMessage>> _directMessagesByUser = new();
    private ulong _currentUserId;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    private static readonly TimeSpan BadgeCacheAge = TimeSpan.FromHours(6);
    private readonly object _badgeCacheLock = new();
    private readonly Dictionary<ulong, (DateTimeOffset FetchedAt, IReadOnlyList<DiscordProfileBadge> Badges)> _badgeCache = new();
    private int _pumpEntered;
    private Client.Status _status = Client.Status.Disconnected;
    private TaskCompletionSource<bool>? _readyCompletion;

    public DiscordSocialService()
    {
        _configPath = Path.Combine(AppPaths.UserDataFolder, "discord-config.json");
        EnsureConfigExample();
    }

    public SocialFriendSource Source => SocialFriendSource.Discord;

    /// <summary>Raised (from the SDK callback pump thread) whenever relationships or presence change.</summary>
    public event Action? FriendsUpdated;

    /// <summary>Raised (from the SDK callback pump thread) when a DM is sent or received; the argument is the other user's id.</summary>
    public event Action<ulong>? DirectMessageReceived;

    public string LastStatusMessage { get; private set; } = string.Empty;

    public bool IsConfigured => TryGetApplicationId(out _);

    public bool IsSessionActive
    {
        get
        {
            lock (_clientLock)
            {
                return _client is not null && _status == Client.Status.Ready;
            }
        }
    }

    public async Task<SocialConnectionResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetApplicationId(out var applicationId))
        {
            return NotConfiguredResult();
        }

        if (!DelegateLifetimePatchApplied)
        {
            return FailedResult("The Discord SDK wrapper could not be initialized safely on this build.");
        }

        try
        {
            var client = EnsureClient();
            using var verifier = client.CreateAuthorizationCodeVerifier();
            var codeVerifier = verifier.Verifier();

            var authorization = NewCompletion<(bool Success, string Message, string Code, string RedirectUri)>();
            using (var args = new AuthorizationArgs())
            {
                args.SetClientId(applicationId);
                // Communication scopes (sdk.social_layer) are a superset of the presence
                // scopes and are required for the in-dash DM chat to send messages.
                args.SetScopes(Client.GetDefaultCommunicationScopes());
                args.SetCodeChallenge(verifier.Challenge());
                client.Authorize(args, new Client.AuthorizationCallback((result, code, redirectUri) =>
                    authorization.TrySetResult((result.Successful(), result.Error(), code, redirectUri))));
            }

            var (authTimedOut, auth) = await WaitWithTimeoutAsync(authorization.Task, AuthorizeTimeout, cancellationToken).ConfigureAwait(false);
            if (authTimedOut)
            {
                client.AbortAuthorize();
                return FailedResult("Discord sign-in timed out. Try again from the friends list.");
            }

            if (!auth.Success)
            {
                if (auth.Message.Contains("redirect_uri", StringComparison.OrdinalIgnoreCase))
                {
                    return FailedResult("Your Discord app has no redirect URL. In the Developer Portal, open your app's OAuth2 tab, add http://127.0.0.1/callback as a redirect, then try Connect again.");
                }

                if (auth.Message.Contains("invalid_scope", StringComparison.OrdinalIgnoreCase))
                {
                    return FailedResult("Your Discord app doesn't have the Social SDK enabled yet. In the Developer Portal, open Discord Social SDK > Getting Started in your app's sidebar, submit the form, then try Connect again.");
                }

                return FailedResult($"Discord sign-in was not completed. {auth.Message}".Trim());
            }

            var tokenExchange = NewCompletion<(bool Success, string Message, string AccessToken, string RefreshToken, string Scopes)>();
            client.GetToken(applicationId, auth.Code, codeVerifier, auth.RedirectUri,
                new Client.TokenExchangeCallback((result, accessToken, refreshToken, _, _, scopes) =>
                    tokenExchange.TrySetResult((result.Successful(), result.Error(), accessToken, refreshToken, scopes))));

            var (tokenTimedOut, token) = await WaitWithTimeoutAsync(tokenExchange.Task, RequestTimeout, cancellationToken).ConfigureAwait(false);
            if (tokenTimedOut || !token.Success)
            {
                return FailedResult($"Discord token exchange failed. {(tokenTimedOut ? "Timed out." : token.Message)}".Trim());
            }

            var (established, establishMessage) = await EstablishSessionAsync(client, token.AccessToken, cancellationToken).ConfigureAwait(false);
            if (!established)
            {
                return FailedResult($"Discord connection failed. {establishMessage}".Trim());
            }

            return BuildConnectedResult(client, token.AccessToken, token.RefreshToken, token.Scopes);
        }
        catch (OperationCanceledException)
        {
            return FailedResult("Discord sign-in was canceled.");
        }
        catch (Exception exception)
        {
            return FailedResult($"Discord SDK error: {exception.Message}");
        }
    }

    public async Task<SocialConnectionResult> RestoreSessionAsync(
        string accessToken,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetApplicationId(out var applicationId))
        {
            return NotConfiguredResult();
        }

        if (!DelegateLifetimePatchApplied)
        {
            return FailedResult("The Discord SDK wrapper could not be initialized safely on this build.");
        }

        try
        {
            var client = EnsureClient();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                var (established, _) = await EstablishSessionAsync(client, accessToken, cancellationToken).ConfigureAwait(false);
                if (established)
                {
                    return BuildConnectedResult(client, accessToken: string.Empty, refreshToken: string.Empty, grantedScopes: string.Empty);
                }
            }

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var tokenExchange = NewCompletion<(bool Success, string Message, string AccessToken, string RefreshToken, string Scopes)>();
                client.RefreshToken(applicationId, refreshToken,
                    new Client.TokenExchangeCallback((result, newAccessToken, newRefreshToken, _, _, scopes) =>
                        tokenExchange.TrySetResult((result.Successful(), result.Error(), newAccessToken, newRefreshToken, scopes))));

                var (refreshTimedOut, refreshed) = await WaitWithTimeoutAsync(tokenExchange.Task, RequestTimeout, cancellationToken).ConfigureAwait(false);
                if (refreshTimedOut)
                {
                    return FailedResult("Discord could not be reached.");
                }

                if (refreshed.Success)
                {
                    var (established, establishMessage) = await EstablishSessionAsync(client, refreshed.AccessToken, cancellationToken).ConfigureAwait(false);
                    if (established)
                    {
                        return BuildConnectedResult(client, refreshed.AccessToken, refreshed.RefreshToken, refreshed.Scopes);
                    }

                    return FailedResult($"Discord connection failed. {establishMessage}".Trim());
                }
            }

            return new SocialConnectionResult
            {
                State = DiscordConnectionState.SessionExpired,
                PopupMessage = "Discord session expired. Select Connect Discord to sign in again."
            };
        }
        catch (OperationCanceledException)
        {
            return FailedResult("Discord reconnect was canceled.");
        }
        catch (Exception exception)
        {
            return FailedResult($"Discord SDK error: {exception.Message}");
        }
    }

    public Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(CancellationToken cancellationToken = default)
    {
        LastStatusMessage = string.Empty;
        Client? client;
        lock (_clientLock)
        {
            client = _status == Client.Status.Ready ? _client : null;
        }

        if (client is null)
        {
            return Task.FromResult<IReadOnlyList<SocialFriend>>([]);
        }

        try
        {
            var friends = new List<SocialFriend>();
            foreach (var relationship in client.GetRelationships())
            {
                using (relationship)
                {
                    if (relationship.DiscordRelationshipType() is not RelationshipType.Friend
                        && relationship.GameRelationshipType() is not RelationshipType.Friend)
                    {
                        continue;
                    }

                    using var user = relationship.User();
                    if (user is null)
                    {
                        continue;
                    }

                    friends.Add(MapFriend(user));
                }
            }

            return Task.FromResult<IReadOnlyList<SocialFriend>>(friends
                .OrderByDescending(friend => friend.IsOnline)
                .ThenBy(friend => friend.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
        }
        catch (Exception exception)
        {
            LastStatusMessage = $"Discord friends failed to load: {exception.Message}";
            return Task.FromResult<IReadOnlyList<SocialFriend>>([]);
        }
    }

    public Task<SocialPartyInviteResult> InviteToPartyAsync(SocialFriend friend, CancellationToken cancellationToken = default)
        => Task.FromResult(new SocialPartyInviteResult
        {
            AddToPartyList = false,
            PopupMessage = "Discord party invites are not supported yet."
        });

    public string ConfiguredApplicationId
    {
        get
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    return string.Empty;
                }

                var config = JsonSerializer.Deserialize<DiscordSocialConfig>(File.ReadAllText(_configPath), JsonOptions);
                var applicationId = config?.ApplicationId?.Trim() ?? string.Empty;
                return applicationId.All(char.IsDigit) ? applicationId : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public string ConfiguredBotToken => ReadConfig()?.BotToken?.Trim() ?? string.Empty;

    public void SaveConfig(string applicationId, string botToken)
    {
        var config = ReadConfig() ?? new DiscordSocialConfig();
        config.ApplicationId = applicationId.Trim();
        config.BotToken = botToken.Trim();
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    private DiscordSocialConfig? ReadConfig()
    {
        try
        {
            return File.Exists(_configPath)
                ? JsonSerializer.Deserialize<DiscordSocialConfig>(File.ReadAllText(_configPath), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DiscordProfileBadge>> GetUserBadgesAsync(ulong userId, CancellationToken cancellationToken = default)
    {
        var botToken = ConfiguredBotToken;
        if (botToken.Length == 0)
        {
            return [];
        }

        lock (_badgeCacheLock)
        {
            if (_badgeCache.TryGetValue(userId, out var cached)
                && DateTimeOffset.UtcNow - cached.FetchedAt < BadgeCacheAge)
            {
                return cached.Badges;
            }
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://discord.com/api/v10/users/{userId}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bot {botToken}");
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogDebug($"Badge lookup for {userId} failed: HTTP {(int)response.StatusCode}");
                return [];
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(payload);
            long flags = 0;
            if (document.RootElement.TryGetProperty("public_flags", out var flagsElement))
            {
                flagsElement.TryGetInt64(out flags);
            }

            var badges = MapPublicFlags(flags);
            lock (_badgeCacheLock)
            {
                _badgeCache[userId] = (DateTimeOffset.UtcNow, badges);
            }

            return badges;
        }
        catch (Exception exception)
        {
            LogDebug($"Badge lookup for {userId} failed: {exception.Message}");
            return [];
        }
    }

    private static IReadOnlyList<DiscordProfileBadge> MapPublicFlags(long flags)
    {
        var badges = new List<DiscordProfileBadge>();
        void Add(int bit, string name, string colorHex, string iconHash)
        {
            if ((flags & (1L << bit)) != 0)
            {
                badges.Add(new DiscordProfileBadge
                {
                    Name = name,
                    ColorHex = colorHex,
                    IconUrl = $"https://cdn.discordapp.com/badge-icons/{iconHash}.png"
                });
            }
        }

        Add(0, "Discord Staff", "#5865F2", "5e74e9b61934fc1f67c65515d1f7e60d");
        Add(1, "Partner", "#4087ED", "3f9748e53446a137a052f3454e2de41e");
        Add(2, "HypeSquad Events", "#F8A532", "bf01d1073931f921909045f3a39fd264");
        Add(3, "Bug Hunter", "#2FA65C", "2717692c7dca7289b35297368a940dd0");
        Add(6, "HypeSquad Bravery", "#9C84EF", "8a88d63823d8a71cd5e390baa45efa02");
        Add(7, "HypeSquad Brilliance", "#F47B67", "011940fd013da3f7fb926e4a1cd2e618");
        Add(8, "HypeSquad Balance", "#45DDC0", "3aa41de486fa12454c3761e8e223442e");
        Add(9, "Early Supporter", "#4E8EF0", "7060786766c9c840eb3019e725d2b358");
        Add(14, "Bug Hunter Gold", "#E7B446", "848f79194d4be5ff5f81505cbd0ce1e6");
        Add(17, "Early Verified Bot Dev", "#5865F2", "6df5892e0f35b051f8b61eace34f4967");
        Add(18, "Moderator Alumni", "#5865F2", "fee1624003e2fee35cb398e125dc479b");
        Add(22, "Active Developer", "#23A55A", "6bdc42827a38498929a4920da12695d9");
        return badges;
    }

    public void DisconnectSession() => Dispose();

    public void Dispose()
    {
        lock (_clientLock)
        {
            _callbackPump?.Dispose();
            _callbackPump = null;

            try
            {
                _client?.Disconnect();
            }
            catch
            {
                // The native client may already be torn down during shutdown.
            }

            try
            {
                _client?.Dispose();
            }
            catch
            {
                // Ignore double-dispose races at app exit.
            }

            _client = null;
            _status = Client.Status.Disconnected;
        }
    }

    private Client EnsureClient()
    {
        lock (_clientLock)
        {
            if (_client is not null)
            {
                return _client;
            }

            var client = new Client();
            _statusChangedHandler = new Client.OnStatusChanged(HandleStatusChanged);
            client.SetStatusChangedCallback(_statusChangedHandler);
            _userUpdatedHandler = new Client.UserUpdatedCallback(_ => FriendsUpdated?.Invoke());
            client.SetUserUpdatedCallback(_userUpdatedHandler);
            _relationshipCreatedHandler = new Client.RelationshipCreatedCallback((_, _) => FriendsUpdated?.Invoke());
            client.SetRelationshipCreatedCallback(_relationshipCreatedHandler);
            _relationshipDeletedHandler = new Client.RelationshipDeletedCallback((_, _) => FriendsUpdated?.Invoke());
            client.SetRelationshipDeletedCallback(_relationshipDeletedHandler);
            _messageCreatedHandler = new Client.MessageCreatedCallback(HandleMessageCreated);
            client.SetMessageCreatedCallback(_messageCreatedHandler);
            _client = client;
            _callbackPump = new System.Threading.Timer(PumpCallbacks, null, CallbackPumpInterval, CallbackPumpInterval);
            return client;
        }
    }

    private void PumpCallbacks(object? state)
    {
        if (Interlocked.Exchange(ref _pumpEntered, 1) == 1)
        {
            return;
        }

        try
        {
            NativeMethods.Discord_RunCallbacks();
        }
        catch
        {
            // Never let a native pump failure take down the timer thread.
        }
        finally
        {
            Interlocked.Exchange(ref _pumpEntered, 0);
        }
    }

    private void HandleStatusChanged(Client.Status status, Client.Error error, int errorDetail)
    {
        TaskCompletionSource<bool>? readyCompletion = null;
        var outcome = false;
        lock (_clientLock)
        {
            _status = status;
            if (status is Client.Status.Ready or Client.Status.Disconnected)
            {
                readyCompletion = _readyCompletion;
                _readyCompletion = null;
                outcome = status == Client.Status.Ready;
            }
        }

        readyCompletion?.TrySetResult(outcome);
        if (status == Client.Status.Ready)
        {
            CaptureCurrentUserId();
            // Presence and avatars can keep syncing after Ready; let listeners refresh.
            FriendsUpdated?.Invoke();
        }
    }

    private void CaptureCurrentUserId()
    {
        Client? client;
        lock (_clientLock)
        {
            client = _client;
        }

        if (client is null)
        {
            return;
        }

        try
        {
            using var user = client.GetCurrentUser();
            if (user is not null)
            {
                _currentUserId = user.Id();
            }
        }
        catch
        {
            // DM authorship falls back to treating everything as incoming.
        }
    }

    private void HandleMessageCreated(ulong messageId)
    {
        Client? client;
        lock (_clientLock)
        {
            client = _client;
        }

        if (client is null)
        {
            return;
        }

        try
        {
            using var message = client.GetMessageHandle(messageId);
            if (message is null)
            {
                LogDebug($"MessageCreated {messageId}: no handle");
                return;
            }

            // A user DM always carries a user recipient; lobby/channel messages do not.
            // Channel() can still be null for a freshly received DM, so don't rely on it.
            var recipientId = message.RecipientId();
            if (recipientId == 0)
            {
                LogDebug($"MessageCreated {messageId}: no recipient (not a DM), skipped");
                return;
            }

            var authorId = message.AuthorId();
            var currentUserId = _currentUserId;
            var otherUserId = authorId == currentUserId ? recipientId : authorId;
            if (otherUserId == 0)
            {
                return;
            }

            var authorName = string.Empty;
            try
            {
                using var author = message.Author();
                authorName = author?.DisplayName() ?? string.Empty;
            }
            catch
            {
                // Author details are cosmetic.
            }

            AppendDirectMessage(otherUserId, new DiscordDmMessage
            {
                MessageId = messageId,
                AuthorId = authorId,
                AuthorName = authorName,
                Content = message.Content(),
                SentAt = SafeTimestamp(message),
                IsFromCurrentUser = authorId == currentUserId
            });
        }
        catch
        {
            // Never let a message callback take down the pump thread.
        }
    }

    private static DateTimeOffset SafeTimestamp(MessageHandle message)
    {
        try
        {
            var unixMilliseconds = message.SentTimestamp();
            return unixMilliseconds > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)unixMilliseconds)
                : DateTimeOffset.Now;
        }
        catch
        {
            return DateTimeOffset.Now;
        }
    }

    private void AppendDirectMessage(ulong otherUserId, DiscordDmMessage entry)
    {
        lock (_messagesLock)
        {
            if (!_directMessagesByUser.TryGetValue(otherUserId, out var messages))
            {
                messages = [];
                _directMessagesByUser[otherUserId] = messages;
            }

            if (messages.Any(existing => existing.MessageId == entry.MessageId))
            {
                return;
            }

            messages.Add(entry);
            if (messages.Count > 200)
            {
                messages.RemoveAt(0);
            }
        }

        DirectMessageReceived?.Invoke(otherUserId);
    }

    public IReadOnlyList<DiscordDmMessage> GetDirectMessages(ulong userId)
    {
        lock (_messagesLock)
        {
            return _directMessagesByUser.TryGetValue(userId, out var messages)
                ? messages.ToList()
                : [];
        }
    }

    public async Task<(bool Success, string ErrorMessage)> SendDirectMessageAsync(
        ulong userId,
        string content,
        CancellationToken cancellationToken = default)
    {
        Client? client;
        lock (_clientLock)
        {
            client = _status == Client.Status.Ready ? _client : null;
        }

        if (client is null)
        {
            return (false, "Discord is not connected.");
        }

        try
        {
            var completion = NewCompletion<(bool Success, string Message, string ResponseBody, ulong MessageId)>();
            client.SendUserMessage(userId, content, new Client.SendUserMessageCallback((result, messageId) =>
                completion.TrySetResult((result.Successful(), result.Error(), SafeResponseBody(result), messageId))));

            var (timedOut, sent) = await WaitWithTimeoutAsync(completion.Task, RequestTimeout, cancellationToken).ConfigureAwait(false);
            if (timedOut)
            {
                return (false, "Discord did not respond.");
            }

            if (!sent.Success)
            {
                LogDebug($"SendUserMessage failed: user={userId} contentLength={content.Length} error='{sent.Message}' body='{sent.ResponseBody}'");
                var details = ExtractApiErrorDetail(sent.ResponseBody);
                var message = string.IsNullOrWhiteSpace(sent.Message) ? "The message could not be sent." : sent.Message;
                return (false, string.IsNullOrWhiteSpace(details) ? message : $"{message} ({details})");
            }

            // The MessageCreated callback usually echoes sent messages, but append
            // immediately (dedup by id) so the chat feels instant.
            AppendDirectMessage(userId, new DiscordDmMessage
            {
                MessageId = sent.MessageId,
                AuthorId = _currentUserId,
                AuthorName = string.Empty,
                Content = content,
                SentAt = DateTimeOffset.Now,
                IsFromCurrentUser = true
            });
            return (true, string.Empty);
        }
        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    private async Task<(bool Established, string Message)> EstablishSessionAsync(
        Client client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var tokenUpdate = NewCompletion<(bool Success, string Message)>();
        client.UpdateToken(AuthorizationTokenType.Bearer, accessToken,
            new Client.UpdateTokenCallback(result => tokenUpdate.TrySetResult((result.Successful(), result.Error()))));

        var (updateTimedOut, update) = await WaitWithTimeoutAsync(tokenUpdate.Task, RequestTimeout, cancellationToken).ConfigureAwait(false);
        if (updateTimedOut || !update.Success)
        {
            return (false, updateTimedOut ? "Timed out." : update.Message);
        }

        TaskCompletionSource<bool> readyCompletion;
        lock (_clientLock)
        {
            if (_status == Client.Status.Ready)
            {
                return (true, string.Empty);
            }

            readyCompletion = _readyCompletion ??= NewCompletion<bool>();
        }

        client.Connect();
        var (connectTimedOut, ready) = await WaitWithTimeoutAsync(readyCompletion.Task, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        if (connectTimedOut || !ready)
        {
            return (false, connectTimedOut ? "Timed out." : "Discord closed the connection.");
        }

        PublishRichPresence(client);
        return (true, string.Empty);
    }

    private SocialConnectionResult BuildConnectedResult(Client client, string accessToken, string refreshToken, string grantedScopes)
    {
        var displayName = string.Empty;
        var userId = string.Empty;
        var avatarUrl = string.Empty;
        try
        {
            using var user = client.GetCurrentUser();
            if (user is not null)
            {
                displayName = FirstNonEmpty(user.DisplayName(), user.GlobalName(), user.Username());
                userId = user.Id().ToString(CultureInfo.InvariantCulture);
                avatarUrl = SafeAvatarUrl(user);
            }
        }
        catch
        {
            // Profile details are cosmetic; the connection itself already succeeded.
        }

        return new SocialConnectionResult
        {
            State = DiscordConnectionState.Connected,
            PopupMessage = string.IsNullOrWhiteSpace(displayName)
                ? "Connected to Discord."
                : $"Connected to Discord as {displayName}.",
            DisplayName = displayName,
            UserId = userId,
            AvatarPathOrUrl = avatarUrl,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            GrantedScopes = grantedScopes,
            TokenTypeName = "Bearer"
        };
    }

    private static SocialFriend MapFriend(UserHandle user)
    {
        var id = user.Id();
        var status = SafeStatus(user);
        var (isOnline, statusText) = status switch
        {
            StatusType.Online => (true, "Online"),
            StatusType.Idle => (true, "Away"),
            StatusType.Dnd => (true, "Busy"),
            StatusType.Streaming => (true, "Streaming"),
            _ => (false, "Offline")
        };

        var activityText = string.Empty;
        try
        {
            using var activity = user.GameActivity();
            var activityName = activity?.Name();
            if (!string.IsNullOrWhiteSpace(activityName))
            {
                activityText = $"Playing {activityName}";
            }
        }
        catch
        {
            // Presence details are optional.
        }

        var username = string.Empty;
        try
        {
            username = user.Username();
        }
        catch
        {
            // Username lookup can fail for provisional accounts.
        }

        var (gamerscoreText, reputationText, zoneText) = BuildSyntheticStats(id);
        return new SocialFriend
        {
            Id = $"discord:{id}",
            DisplayName = FirstNonEmpty(user.DisplayName(), user.GlobalName(), username, id.ToString(CultureInfo.InvariantCulture)),
            Source = SocialFriendSource.Discord,
            AvatarPathOrUrl = SafeAvatarUrl(user),
            IsOnline = isOnline,
            StatusText = statusText,
            ActivityText = activityText,
            GamerscoreText = gamerscoreText,
            ReputationText = reputationText,
            ZoneText = zoneText,
            IdentityDetailText = string.IsNullOrWhiteSpace(username) ? "Discord" : $"@{username}"
        };
    }

    private static (string Gamerscore, string Reputation, string Zone) BuildSyntheticStats(ulong userId)
    {
        var random = new Random(unchecked((int)(userId ^ (userId >> 32))));
        var zones = new[] { "Recreation", "Family", "Pro", "Underground" };
        var filledStars = random.Next(3, 6);
        var reputation = new string('★', filledStars) + new string('☆', 5 - filledStars);
        return ($"{random.Next(2500, 95000):N0} G", reputation, zones[random.Next(zones.Length)]);
    }

    private static StatusType SafeStatus(UserHandle user)
    {
        try
        {
            return user.Status();
        }
        catch
        {
            return StatusType.Unknown;
        }
    }

    private static string SafeAvatarUrl(UserHandle user)
    {
        try
        {
            return user.AvatarUrl(UserHandle.AvatarType.Png, UserHandle.AvatarType.Png) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static void PublishRichPresence(Client client)
    {
        try
        {
            using var activity = new Activity();
            activity.SetType(ActivityTypes.Playing);
            activity.SetName("DashX360");
            activity.SetDetails("Xbox 360 Dashboard");
            client.UpdateRichPresence(activity, new Client.UpdateRichPresenceCallback(_ => { }));
        }
        catch
        {
            // Rich presence is best-effort.
        }
    }

    private static string SafeResponseBody(ClientResult result)
    {
        try
        {
            return result.ResponseBody() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractApiErrorDetail(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ToString();
            }

            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Fall back to the raw body, trimmed.
        }

        return responseBody.Length > 200 ? responseBody[..200] : responseBody;
    }

    private static void LogDebug(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppPaths.LogsFolder, "discord-debug.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break the feature.
        }
    }

    private static TaskCompletionSource<T> NewCompletion<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<(bool TimedOut, T Result)> WaitWithTimeoutAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (completed != task)
        {
            return (true, default!);
        }

        return (false, await task.ConfigureAwait(false));
    }

    private static SocialConnectionResult NotConfiguredResult()
        => new()
        {
            State = DiscordConnectionState.NotImplemented,
            PopupMessage = "Discord needs UserData\\discord-config.json with your application id."
        };

    private static SocialConnectionResult FailedResult(string message)
        => new()
        {
            State = DiscordConnectionState.Failed,
            PopupMessage = message
        };

    private bool TryGetApplicationId(out ulong applicationId)
    {
        applicationId = 0;
        try
        {
            if (!File.Exists(_configPath))
            {
                return false;
            }

            var config = JsonSerializer.Deserialize<DiscordSocialConfig>(File.ReadAllText(_configPath), JsonOptions);
            return config is not null
                   && ulong.TryParse(config.ApplicationId, NumberStyles.None, CultureInfo.InvariantCulture, out applicationId)
                   && applicationId != 0;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureConfigExample()
    {
        var examplePath = Path.Combine(AppPaths.UserDataFolder, "discord-config.example.json");
        if (File.Exists(examplePath))
        {
            return;
        }

        var example = new DiscordSocialConfig
        {
            ApplicationId = "paste-your-discord-application-id-here",
            BotToken = "optional-bot-token-enables-profile-badges"
        };
        File.WriteAllText(examplePath, JsonSerializer.Serialize(example, JsonOptions));
    }
}
