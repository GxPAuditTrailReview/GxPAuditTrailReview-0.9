using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

namespace AuditTrailReview.WebApp.Security
{
    /// <summary>
    /// Builds the <see cref="ForwardedHeadersOptions"/> for <c>UseForwardedHeaders</c>.
    /// <para>
    /// X-Forwarded-For / X-Forwarded-Proto are honoured ONLY when the immediate sender is a
    /// configured trusted proxy. The old setup cleared KnownProxies/KnownNetworks, which made the
    /// app believe those headers from anyone: a client could forge the IP recorded in the user
    /// login log (GxP evidence) and fake "X-Forwarded-Proto: https" to pass the Secure-cookie check.
    /// </para>
    /// <para>
    /// Configuration (arrays; environment variables use <c>ForwardedHeaders__KnownProxies__0</c>):
    /// <c>ForwardedHeaders:KnownProxies</c> = single IPs, <c>ForwardedHeaders:KnownNetworks</c> = CIDR
    /// ranges (e.g. <c>172.16.0.0/12</c>). Nothing configured keeps the framework default
    /// (loopback only), which is correct for a proxy on the same host.
    /// </para>
    /// </summary>
    public static class ForwardedHeadersSetup
    {
        public const string SectionName = "ForwardedHeaders";

        public static ForwardedHeadersOptions Build(IConfiguration configuration)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };

            var section = configuration.GetSection(SectionName);

            foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
            {
                if (string.IsNullOrWhiteSpace(proxy)) continue; // empty env var from a compose default
                if (!IPAddress.TryParse(proxy?.Trim(), out var address))
                    throw new InvalidOperationException(
                        $"{SectionName}:KnownProxies contains an invalid IP address: '{proxy}'.");
                options.KnownProxies.Add(address);
            }

            foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
            {
                if (string.IsNullOrWhiteSpace(network)) continue;
                var parts = (network ?? string.Empty).Trim().Split('/');
                if (parts.Length != 2
                    || !IPAddress.TryParse(parts[0], out var prefix)
                    || !int.TryParse(parts[1], out var prefixLength)
                    || prefixLength < 0
                    || prefixLength > (prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))
                    throw new InvalidOperationException(
                        $"{SectionName}:KnownNetworks contains an invalid CIDR range: '{network}'.");
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
            }

            return options;
        }
    }
}
