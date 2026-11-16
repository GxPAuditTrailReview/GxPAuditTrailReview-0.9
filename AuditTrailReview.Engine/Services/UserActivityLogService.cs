using AuditTrailReview.Database;
using AuditTrailReview.Model;
using Microsoft.AspNetCore.Http;
using System.Net;

namespace AuditTrailReview.Engine.Services
{
    public interface IUserActivityLogService
    {
        Task LogAsync(string action, string? userId, string? userName,
                      HttpContext httpContext, string? details = null, bool success = true);
    }

    public class UserActivityLogService : IUserActivityLogService
    {
        private readonly ApplicationDbContext _context;

        public UserActivityLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(string action, string? userId, string? userName,
                                   HttpContext httpContext, string? details = null, bool success = true)
        {
            var entry = new UserActivityLog
            {
                Action    = action,
                UserId    = userId,
                UserName  = userName,
                IpAddress = ResolveIpAddress(httpContext),
                UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
                Details   = details,
                Timestamp = DateTime.UtcNow,
                Success   = success
            };

            _context.UserActivityLog.Add(entry);
            await _context.SaveChangesAsync();
        }

        private static string ResolveIpAddress(HttpContext httpContext)
        {
            // Only the connection's address is recorded. X-Forwarded-For is never read here:
            // any client can send it. When the app runs behind a trusted reverse proxy, the
            // UseForwardedHeaders middleware (restricted to ForwardedHeaders:KnownProxies/
            // KnownNetworks) has already rewritten RemoteIpAddress to the real client.
            var remoteIp = httpContext.Connection.RemoteIpAddress;

            if (remoteIp == null)
                return "Unknown";

            // Map IPv4-in-IPv6 addresses (e.g. ::ffff:192.168.1.1 → 192.168.1.1)
            if (remoteIp.IsIPv4MappedToIPv6)
                remoteIp = remoteIp.MapToIPv4();

            // Resolve loopback to a readable label
            if (IPAddress.IsLoopback(remoteIp))
                return "Localhost (127.0.0.1)";

            return remoteIp.ToString();
        }
    }
}