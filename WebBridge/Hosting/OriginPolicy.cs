namespace IOToolkit.WebBridge.Hosting;

/// <summary>
/// Decides which browser origins may call the bridge.
/// </summary>
/// <remarks>
/// The bridge listens on loopback only, but "loopback" does not mean "safe": any web page the
/// operator opens on the exhibit machine can POST to 127.0.0.1 and drive actuators. Echoing
/// <c>Access-Control-Allow-Origin: *</c> made that possible, because the browser then let any origin
/// read the response and send credentialed-free writes.
/// <para>
/// Requests without an <c>Origin</c> header come from non-browser clients (the MyriadBox shell's
/// content server, curl, native hosts) and are allowed. Browser requests are allowed when the origin
/// is loopback (the page is served by something on this machine, which is the exhibit shell's normal
/// case regardless of the port it picked) or explicitly whitelisted.
/// </para>
/// </remarks>
public sealed class OriginPolicy
{
    private readonly HashSet<string> _allowed;

    public OriginPolicy(IEnumerable<string>? allowedOrigins)
    {
        _allowed = new HashSet<string>(
            (allowedOrigins ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Normalize),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool AllowsAnyLoopback => true;

    public IReadOnlyCollection<string> ExplicitOrigins => _allowed;

    /// <summary>Returns the origin to echo back, or null when the caller must be rejected.</summary>
    public string? Resolve(string? originHeader)
    {
        if (string.IsNullOrWhiteSpace(originHeader))
            return null; // Not a browser request: no CORS response headers needed.

        if (string.Equals(originHeader.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            return null; // Sandboxed iframe / opaque origin: nothing legitimate to match against.

        var origin = Normalize(originHeader);
        if (_allowed.Contains(origin))
            return origin;
        if (IsLoopbackOrigin(origin))
            return origin;
        return null;
    }

    /// <summary>Whether a browser request may proceed. Non-browser requests always may.</summary>
    public bool IsAllowed(string? originHeader) =>
        string.IsNullOrWhiteSpace(originHeader) || Resolve(originHeader) is not null;

    private static bool IsLoopbackOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;
        var host = uri.Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.Ordinal)
            || host.Equals("[::1]", StringComparison.Ordinal)
            || host.Equals("::1", StringComparison.Ordinal);
    }

    private static string Normalize(string origin) => origin.Trim().TrimEnd('/');
}
