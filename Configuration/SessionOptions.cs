namespace Carwash.API.Configuration;

public sealed class SessionOptions
{
    public const string SectionName = "Session";
    public int IdleTimeoutMinutes { get; set; } = 30;
    public int AbsoluteLifetimeHours { get; set; } = 8;
    public int MaxSessions { get; set; } = 10000;
}