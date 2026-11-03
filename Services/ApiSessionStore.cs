using System.Security.Cryptography;
using SessionOptions = Carwash.API.Configuration.SessionOptions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Carwash.API.Services;

public sealed class ApiSessionStore : IDisposable
{
    private readonly Dictionary<string, SessionEntry> sessions = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly SessionOptions options;
    private readonly ITimer cleanup;

    public ApiSessionStore(TimeProvider clock, IOptions<SessionOptions> options)
    {
        this.clock = clock;
        this.options = options.Value;
        if (this.options.IdleTimeoutMinutes <= 0 || this.options.AbsoluteLifetimeHours <= 0
            || this.options.MaxSessions is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(options));
        cleanup = clock.CreateTimer(_ => RemoveExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public IssuedSession? Issue(int userId)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            RemoveExpiredCore(now);
            if (sessions.Count >= options.MaxSessions) return null;
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var expires = now.AddHours(options.AbsoluteLifetimeHours);
            sessions.Add(Hash(token), new SessionEntry(userId, expires));
            return new IssuedSession(token, expires, checked(options.AbsoluteLifetimeHours * 3600));
        }
    }

    public int? Validate(string token)
    {
        if (!IsValidToken(token)) return null;
        lock (gate)
        {
            var hash = Hash(token);
            if (!sessions.TryGetValue(hash, out var session)) return null;
            var now = clock.GetUtcNow();
            if (Expired(session, now))
            {
                sessions.Remove(hash);
                return null;
            }
            return session.UserId;
        }
    }

    public void Revoke(string token)
    {
        if (!IsValidToken(token)) return;
        lock (gate) sessions.Remove(Hash(token));
    }

    public static bool IsValidToken(string? token)
    {
        if (token?.Length != 43 || token.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
            return false;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            return bytes.Length == 32 && WebEncoders.Base64UrlEncode(bytes) == token;
        }
        catch (FormatException) { return false; }
    }

    private bool Expired(SessionEntry session, DateTimeOffset now) =>
        now >= session.AbsoluteExpires;

    private void RemoveExpired()
    {
        lock (gate) RemoveExpiredCore(clock.GetUtcNow());
    }

    private void RemoveExpiredCore(DateTimeOffset now)
    {
        foreach (var hash in sessions.Where(pair => Expired(pair.Value, now)).Select(pair => pair.Key).ToArray())
            sessions.Remove(hash);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));
    public void Dispose() => cleanup.Dispose();

    private sealed record SessionEntry(int UserId, DateTimeOffset AbsoluteExpires);
}

public sealed record IssuedSession(string AccessToken, DateTimeOffset ExpiresAt, int IdleTimeoutSeconds)
{
    public override string ToString() => $"IssuedSession {{ ExpiresAt = {ExpiresAt:O} }}";
}