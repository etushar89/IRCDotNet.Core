namespace IRCDotNet.Core.Transport;

/// <summary>
/// Abstraction over the network transport used to communicate with an IRC server.
/// Implementations handle connection lifecycle, line-oriented reading/writing, and resource disposal.
/// Two built-in implementations: <see cref="TcpIrcTransport"/> (raw TCP/SSL) and <see cref="WebSocketIrcTransport"/> (WebSocket).
/// </summary>
public interface IIrcTransport : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Whether the transport is currently connected and able to send/receive data.
    /// Returns <c>false</c> before <see cref="ConnectAsync"/> completes and after <see cref="DisconnectAsync"/> is called.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Opens the connection to the IRC server. On failure, the transport remains in an unconnected state
    /// and the caller must create a new instance to retry.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the connection attempt.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled or the connection timed out.</exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the next complete IRC protocol line from the server.
    /// Blocks until a full line is available, the connection closes, or cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the read operation.</param>
    /// <returns>The next IRC line without trailing CRLF, or <c>null</c> when the connection is closed or the stream ends.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single IRC protocol line to the server. CRLF is appended automatically by the transport.
    /// </summary>
    /// <param name="line">The IRC line to send, without trailing CRLF (e.g. <c>"PRIVMSG #channel :hello"</c>).</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <exception cref="InvalidOperationException">The transport is not connected.</exception>
    Task WriteLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the connection gracefully and releases all underlying network resources (sockets, streams, buffers).
    /// Safe to call multiple times. After this call, <see cref="IsConnected"/> returns <c>false</c>.
    /// </summary>
    Task DisconnectAsync();
}
