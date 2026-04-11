using System.Net.WebSockets;
using System.Text;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Transport;

/// <summary>
/// IRC transport over a WebSocket connection (RFC 6455).
/// Used to connect to IRC servers that expose a WebSocket endpoint (e.g. UnrealIRCd, InspIRCd, KiwiIRC gateways).
/// IRC protocol lines are sent as WebSocket text frames with CRLF terminators.
/// On receive, both text and binary frames are accepted and decoded as UTF-8,
/// and both newline-delimited and frame-as-line formats are supported.
/// </summary>
public class WebSocketIrcTransport : IIrcTransport
{
    private readonly IrcClientOptions _options;
    private readonly ILogger? _logger;
    private ClientWebSocket? _webSocket;
    private readonly StringBuilder _lineBuffer = new();
    private volatile bool _connected;

    /// <summary>
    /// Initializes a new WebSocket transport. Does not connect — call <see cref="ConnectAsync"/> to establish the connection.
    /// </summary>
    /// <param name="options">Connection configuration. <see cref="IrcClientOptions.WebSocketUri"/> must be a valid <c>ws://</c> or <c>wss://</c> URI.</param>
    /// <param name="logger">Optional logger for connection diagnostics and error reporting.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><see cref="IrcClientOptions.WebSocketUri"/> is null or whitespace.</exception>
    public WebSocketIrcTransport(IrcClientOptions options, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        if (string.IsNullOrWhiteSpace(options.WebSocketUri))
            throw new ArgumentException("WebSocketUri must be set for WebSocket transport.", nameof(options));
    }

    /// <inheritdoc />
    public bool IsConnected => _connected && _webSocket?.State == WebSocketState.Open;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("WebSocket connecting to {Uri}", _options.WebSocketUri);

        _webSocket = new ClientWebSocket();
        _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        // Many IRC WebSocket servers require the "irc" subprotocol (e.g. UnrealIRCd, InspIRCd)
        _webSocket.Options.AddSubProtocol("irc");

        // Allow invalid SSL certificates if configured (self-signed, expired, wrong hostname)
        if (_options.AcceptInvalidSslCertificates)
        {
            _webSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
        }

        using var timeoutCts = new CancellationTokenSource(_options.ConnectionTimeoutMs);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        await _webSocket.ConnectAsync(new Uri(_options.WebSocketUri!), combinedCts.Token).ConfigureAwait(false);

        _connected = true;
        _logger?.LogInformation("WebSocket connected to {Uri}", _options.WebSocketUri);
    }

    /// <inheritdoc />
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return null;

        // Check if we already have a complete line buffered from a previous read
        var newlineIndex = IndexOfNewline();
        if (newlineIndex >= 0)
        {
            return ExtractLine(newlineIndex);
        }

        // Read from WebSocket until we get a complete line
        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Let cancellation propagate — callers (ReadLoopAsync) expect this
                throw;
            }
            catch (WebSocketException)
            {
                _connected = false;
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _connected = false;
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _lineBuffer.Append(text);

                // Check for complete line(s) delimited by \n
                newlineIndex = IndexOfNewline();
                if (newlineIndex >= 0)
                {
                    return ExtractLine(newlineIndex);
                }

                // Some IRC WebSocket servers (e.g. UnrealIRCd) send each IRC line
                // as a complete WebSocket frame without \r\n terminators.
                // If end-of-message and no newline found, treat the buffered content as a complete line.
                if (result.EndOfMessage && _lineBuffer.Length > 0)
                {
                    var completeLine = _lineBuffer.ToString().TrimEnd('\r', '\n');
                    _lineBuffer.Clear();
                    if (completeLine.Length > 0)
                        return completeLine;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket not connected");

        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        _connected = false;

        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error during WebSocket close handshake");
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _lineBuffer.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Scans the internal line buffer for a <c>\n</c> character without allocating a string.
    /// </summary>
    /// <returns>Zero-based index of the first <c>\n</c>, or <c>-1</c> if not found.</returns>
    private int IndexOfNewline()
    {
        for (int i = 0; i < _lineBuffer.Length; i++)
        {
            if (_lineBuffer[i] == '\n')
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Extracts one complete IRC line from the internal buffer up to the newline at <paramref name="newlineIndex"/>.
    /// Any remaining data after the newline is preserved in the buffer for subsequent reads.
    /// </summary>
    /// <param name="newlineIndex">Zero-based index of the <c>\n</c> character in the buffer.</param>
    /// <returns>The extracted line with trailing <c>\r</c> stripped.</returns>
    private string ExtractLine(int newlineIndex)
    {
        var fullBuffer = _lineBuffer.ToString();
        var line = fullBuffer[..newlineIndex].TrimEnd('\r');
        _lineBuffer.Clear();
        if (newlineIndex + 1 < fullBuffer.Length)
        {
            _lineBuffer.Append(fullBuffer[(newlineIndex + 1)..]);
        }
        return line;
    }
}
