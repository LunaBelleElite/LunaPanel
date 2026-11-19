using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Http;

/// <summary>
/// Refuses any request whose <c>Host</c> header is not one of this
/// computer's own names - the guard against DNS rebinding
/// (<c>ref/docs/hosting.md</c>, "DNS rebinding and the Host header").
///
/// <b>The attack.</b> A web page re-points its own domain at 127.0.0.1, so
/// the commander's browser sends that page's requests to LunaPanel's
/// loopback listener - where a request is the host with no cookie at all -
/// and, because the browser thinks it is still talking to the page's own
/// origin, lets the page read every response. What gives it away is the
/// one thing the page cannot choose: the browser still sends the page's
/// domain as <c>Host</c>.
///
/// <b>Allowed:</b> an IPv4 literal in canonical four-octet dotted-decimal
/// form, a bracketed IPv6 literal with no zone id, <c>localhost</c>, or a
/// name in the trusted list (this machine's name and its <c>.local</c>
/// form), names compared case-insensitively - and in every case the port
/// must be the one the request actually arrived on, an absent port meaning
/// 80. Everything else is refused, including names that wildcard DNS
/// resolves to loopback (<c>127.0.0.1.nip.io</c>, <c>localtest.me</c>):
/// they are names, not IP literals, and a rebinding page can own one.
///
/// <b>This only ever refuses.</b> It says nothing about who the host is;
/// that decision stays with the connection alone, made later by the device
/// authentication middleware, and no header can reach it.
/// </summary>
public static class HostHeaderPolicy
{
    /// <summary>The whole 400 body. Never echoes the offending header.</summary>
    public const string RefusalMessage = "LunaPanel only answers to its IP address or this computer's name.";

    /// <summary>The longest Host value considered at all (a DNS name's own limit is 253).</summary>
    public const int MaxHostLength = 255;

    /// <summary>How many distinct refused values are logged before the log goes quiet.</summary>
    public const int MaxDistinctLoggedHosts = 20;

    /// <summary>How much of a refused value reaches the log.</summary>
    public const int MaxLoggedHostLength = 64;

    /// <summary>
    /// The machine names this server answers to besides IP literals and
    /// <c>localhost</c>: the name itself and its mDNS form. A blank name
    /// trusts no name at all, rather than trusting <c>.local</c>.
    /// </summary>
    public static IReadOnlyCollection<string> TrustedNamesFor(string? machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName))
        {
            return Array.Empty<string>();
        }

        var name = machineName.Trim();
        return new[] { name, name + ".local" };
    }

    /// <summary>
    /// Whether <paramref name="hostHeader"/> names this server on
    /// <paramref name="localPort"/>, the port the request arrived on. Pure:
    /// no context, no I/O, so every shape can be pinned directly.
    /// </summary>
    public static bool IsAllowed(string? hostHeader, int localPort, IReadOnlyCollection<string> trustedNames)
    {
        if (string.IsNullOrEmpty(hostHeader) || hostHeader.Length > MaxHostLength)
        {
            return false;
        }

        // Character allowlist first: letters, digits and the punctuation a
        // host, a bracketed IPv6 literal and a port can contain. This is what
        // refuses whitespace, control characters, '@', '/', '\' and '%'
        // (a zone id) before any parsing happens.
        foreach (var c in hostHeader)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':' or '[' or ']'))
            {
                return false;
            }
        }

        string host;
        string? port;
        bool bracketed;

        if (hostHeader[0] == '[')
        {
            var close = hostHeader.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            host = hostHeader[1..close];
            var rest = hostHeader[(close + 1)..];
            if (rest.Length == 0)
            {
                port = null;
            }
            else if (rest[0] == ':')
            {
                port = rest[1..];
            }
            else
            {
                return false;
            }

            bracketed = true;
        }
        else
        {
            var colon = hostHeader.IndexOf(':');
            if (colon != hostHeader.LastIndexOf(':'))
            {
                // More than one colon outside brackets: bare IPv6, or junk.
                return false;
            }

            host = colon < 0 ? hostHeader : hostHeader[..colon];
            port = colon < 0 ? null : hostHeader[(colon + 1)..];
            bracketed = false;
        }

        if (!PortMatches(port, localPort))
        {
            return false;
        }

        if (bracketed)
        {
            return IsIPv6Literal(host);
        }

        return IsCanonicalIPv4(host)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || trustedNames.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A plain 1-5 digit number, no sign, no leading zero, 1-65535, equal to
    /// the arrival port. Absent means 80.
    /// </summary>
    private static bool PortMatches(string? port, int localPort)
    {
        if (port is null)
        {
            return localPort == 80;
        }

        if (port.Length is < 1 or > 5 || port[0] == '0')
        {
            return false;
        }

        var value = 0;
        foreach (var c in port)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return value is >= 1 and <= 65535 && value == localPort;
    }

    /// <summary>
    /// Exactly four dot-separated decimal octets, each 0-255 with no leading
    /// zero. Refuses every other spelling some parser would also read as an
    /// address (<c>2130706433</c>, <c>0x7f000001</c>, <c>127.1</c>,
    /// <c>0177.0.0.1</c>) - one spelling per address is the rule a reader
    /// can check.
    /// </summary>
    private static bool IsCanonicalIPv4(string host)
    {
        var parts = host.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 3 || (part.Length > 1 && part[0] == '0'))
            {
                return false;
            }

            var value = 0;
            foreach (var c in part)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }

                value = (value * 10) + (c - '0');
            }

            if (value > 255)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The inside of a bracketed literal: hex digits, colons and (for an
    /// IPv4-mapped tail) dots only, and parsing as an IPv6 address. The
    /// character allowlist has already refused '%', so no zone id gets here.
    /// </summary>
    private static bool IsIPv6Literal(string host)
    {
        if (host.Length == 0 || !host.Contains(':'))
        {
            return false;
        }

        foreach (var c in host)
        {
            if (!(char.IsAsciiHexDigit(c) || c is ':' or '.'))
            {
                return false;
            }
        }

        return IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6;
    }

    /// <summary>
    /// Adds the policy to the pipeline. Must sit before device
    /// authentication so it covers every route, the auth-exempt ones
    /// included (pinned by <c>HostAccessSourceGuardTests</c>).
    /// </summary>
    public static IApplicationBuilder UseHostHeaderPolicy(
        this IApplicationBuilder app,
        IDiagnosticLog log,
        IReadOnlyCollection<string> trustedNames)
    {
        // Per pipeline, not static: one server's refusals never silence
        // another's. Bounded, so a hostile page cycling through names cannot
        // grow it or flood the log.
        var logged = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var capReported = 0;

        return app.Use(async (context, next) =>
        {
            var hostHeader = context.Request.Headers.Host.ToString();
            if (IsAllowed(hostHeader, context.Connection.LocalPort, trustedNames))
            {
                await next(context);
                return;
            }

            var shown = ForLog(hostHeader);
            if (logged.Count < MaxDistinctLoggedHosts)
            {
                if (logged.TryAdd(shown, 0))
                {
                    log.Warn("Auth", "Request refused: the Host header is not this computer's IP address or name", shown);
                }
            }
            else if (!logged.ContainsKey(shown) && Interlocked.Exchange(ref capReported, 1) == 0)
            {
                log.Warn("Auth", $"Request refused: the Host header is not this computer's IP address or name - {MaxDistinctLoggedHosts} different values already logged, further ones will not be");
            }

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(RefusalMessage);
        });
    }

    /// <summary>
    /// What of a refused value reaches the log: control characters replaced,
    /// cut to <see cref="MaxLoggedHostLength"/> with a marker. Never the whole
    /// value.
    /// </summary>
    private static string ForLog(string hostHeader)
    {
        if (hostHeader.Length == 0)
        {
            return "(empty)";
        }

        var cut = hostHeader.Length > MaxLoggedHostLength ? hostHeader[..MaxLoggedHostLength] : hostHeader;
        var cleaned = new string(cut.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return hostHeader.Length > MaxLoggedHostLength ? cleaned + "...(cut)" : cleaned;
    }
}
