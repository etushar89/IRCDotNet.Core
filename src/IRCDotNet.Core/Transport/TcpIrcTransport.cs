using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using IRCDotNet.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace IRCDotNet.Core.Transport;

/// <summary>
/// IRC transport over a raw TCP socket with optional SSL/TLS encryption.
/// This is the standard IRC connection method (typically port 6667 for plain, 6697 for SSL).
/// Text encoding is configurable via <see cref="IrcClientOptions.Encoding"/> (default UTF-8 without BOM).
/// </summary>
public class TcpIrcTransport : IIrcTransport
{
    private readonly IrcClientOptions _options;
    private readonly ILogger? _logger;
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private volatile bool _connected;

    /// <summary>
    /// Initializes a new TCP transport. Does not connect — call <see cref="ConnectAsync"/> to establish the connection.
    /// </summary>
    /// <param name="options">Connection configuration including server hostname, port, SSL mode, encoding, and timeouts.</param>
    /// <param name="logger">Optional logger for connection diagnostics and error reporting.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public TcpIrcTransport(IrcClientOptions options, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("TCP connecting to {Server}:{Port} (SSL: {UseSsl})",
            _options.Server, _options.Port, _options.UseSsl);

        _tcpClient = new TcpClient();
        using var timeoutCts = new CancellationTokenSource(_options.ConnectionTimeoutMs);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        await _tcpClient.ConnectAsync(_options.Server, _options.Port, combinedCts.Token).ConfigureAwait(false);

        // TCP KeepAlive: probe every 15s after 30s idle, fail after ~75s
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 15);
        _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);

        _stream = _tcpClient.GetStream();

        if (_options.UseSsl)
        {
            RemoteCertificateValidationCallback? certCallback = null;
            if (_options.AcceptInvalidSslCertificates)
            {
                certCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }

            var sslStream = new SslStream(_stream, false, certCallback, null);

            using var sslTimeoutCts = new CancellationTokenSource(_options.ConnectionTimeoutMs);
            using var sslCombinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sslTimeoutCts.Token);

            var sslTask = sslStream.AuthenticateAsClientAsync(_options.Server, null,
                System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13, false);
            await sslTask.WaitAsync(sslCombinedCts.Token).ConfigureAwait(false);
            _stream = sslStream;
        }

        var encoding = _options.Encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
            ? new UTF8Encoding(false)
            : Encoding.GetEncoding(_options.Encoding);

        _reader = new StreamReader(_stream, encoding);
        _writer = new StreamWriter(_stream, encoding) { NewLine = "\r\n" };
        _connected = true;

        _logger?.LogInformation("TCP connected to {Server}:{Port}", _options.Server, _options.Port);
    }

    /// <inheritdoc />
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (_reader == null) return null;
        try
        {
            return await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Stream was disposed during disconnect — treat as end of stream
            _connected = false;
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (_writer == null)
            throw new InvalidOperationException("Transport not connected");

        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        _connected = false;

        if (_writer != null)
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            _writer = null;
        }

        _reader?.Dispose();
        _reader = null;

        if (_stream != null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _tcpClient?.Dispose();
        _tcpClient = null;
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
}
