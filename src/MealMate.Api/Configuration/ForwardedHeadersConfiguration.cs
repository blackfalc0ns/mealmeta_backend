using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MealMate.Api.Configuration;

/// <summary>
/// Binds trusted reverse-proxy settings from configuration. When no proxies or
/// networks are configured, ASP.NET Core defaults (loopback only) are retained.
/// Invalid entries fail fast at startup.
/// </summary>
public static class ForwardedHeadersConfiguration
{
    public static void Configure(IConfiguration configuration, ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        var forwardLimit = configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
        if (forwardLimit < 1)
        {
            throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be greater than or equal to 1.");
        }

        options.ForwardLimit = forwardLimit;

        var knownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
        if (knownProxies is { Length: > 0 })
        {
            foreach (var proxy in knownProxies)
            {
                if (string.IsNullOrWhiteSpace(proxy))
                {
                    continue;
                }

                if (!IPAddress.TryParse(proxy.Trim(), out var address))
                {
                    throw new InvalidOperationException(
                        $"ForwardedHeaders:KnownProxies contains invalid IP address '{proxy}'.");
                }

                options.KnownProxies.Add(address);
            }
        }

        var knownNetworks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
        if (knownNetworks is { Length: > 0 })
        {
            foreach (var network in knownNetworks)
            {
                if (string.IsNullOrWhiteSpace(network))
                {
                    continue;
                }

                if (!TryParseNetwork(network.Trim(), out var ipNetwork))
                {
                    throw new InvalidOperationException(
                        $"ForwardedHeaders:KnownNetworks contains invalid CIDR '{network}'. Expected format: prefix/length.");
                }

                options.KnownNetworks.Add(ipNetwork);
            }
        }
    }

    private static bool TryParseNetwork(string value, out Microsoft.AspNetCore.HttpOverrides.IPNetwork network)
    {
        network = default!;
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash >= value.Length - 1)
        {
            return false;
        }

        if (!IPAddress.TryParse(value[..slash], out var prefix))
        {
            return false;
        }

        if (!int.TryParse(value[(slash + 1)..], out var prefixLength))
        {
            return false;
        }

        try
        {
            network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
