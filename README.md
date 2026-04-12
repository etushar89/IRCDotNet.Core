# IRCDotNet.Core — Modern .NET 8 IRC Client Library

A production-ready, thread-safe [IRC (Internet Relay Chat)](https://en.wikipedia.org/wiki/IRC) client library for .NET 8 with comprehensive IRCv3 support, full IntelliSense documentation, and no runtime dependencies beyond `Microsoft.Extensions.*` 8.0 and `System.Text.Json`.

Build chat applications, notification relays, channel monitors, IRC-to-Discord bridges, or any tool that needs real-time IRC connectivity.

## Installation

```shell
dotnet add package IRCDotNet.Core
```

## Features

- **RFC 1459 + Modern IRC** — Complete protocol implementation with ISUPPORT parsing
- **IRCv3 Capabilities** — SASL, message-tags, server-time, away-notify, account-notify, extended-join, cap-notify, chghost, batch, echo-message, monitor, setname, invite-notify, labeled-response (negotiated)
- **WebSocket Transport** — Connect via `wss://` or `ws://` endpoints (UnrealIRCd, InspIRCd, KiwiIRC gateways) alongside traditional TCP/SSL
- **SASL Authentication** — PLAIN and EXTERNAL mechanisms with automatic CAP negotiation
- **NickServ IDENTIFY** — Reactive identification triggered by NickServ prompts
- **Rate Limiting** — Configurable token-bucket algorithm prevents flood protection kicks
- **Auto-Reconnect** — Exponential backoff with automatic channel rejoin
- **Thread-Safe** — `ConcurrentDictionary`, `ConcurrentHashSet`, semaphore-controlled sends, volatile state
- **Event-Driven** — 39 event types with threaded, sequential, or background dispatch strategies
- **Fluent Builder** — `IrcClientOptionsBuilder` for type-safe configuration
- **Dependency Injection** — `IServiceCollection` integration with `AddIrcClient()` and `AddIrcBotManager()`
- **Multi-Client Management** — `IrcBotManager` as an `IHostedService` for managing multiple connections
- **Protocol Utilities** — IRC case mapping, message validation, encoding helpers, formatting strippers
- **CTCP Support** — ACTION (`/me`), VERSION, PING, TIME, CLIENTINFO, FINGER, SOURCE, USERINFO, ERRMSG with configurable auto-replies
- **Full IntelliSense** — Every public member documented with `<summary>`, `<param>`, and `<returns>` tags

## Quick Start

```csharp
using IRCDotNet.Core;
using IRCDotNet.Core.Configuration;

var options = new IrcClientOptions
{
    Server = "irc.libera.chat",
    Port = 6697,
    UseSsl = true,
    Nick = "MyApp",
    UserName = "myapp",
    RealName = "My IRC Application"
};

await using var client = new IrcClient(options);

client.Connected += (s, e) =>
{
    Console.WriteLine($"Connected to {e.Network} as {e.Nick}");
    _ = client.JoinChannelAsync("#mychannel");
};

client.PrivateMessageReceived += (s, e) =>
    Console.WriteLine($"[{e.Target}] <{e.Nick}> {e.Text}");

client.ChannelJoinFailed += (s, e) =>
    Console.WriteLine($"Failed to join {e.Channel}: {e.Reason}");

await client.ConnectAsync();

// Keep running until Ctrl+C
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
try { await Task.Delay(Timeout.Infinite, cts.Token); } catch (OperationCanceledException) { }
```

## Fluent Builder

```csharp
// TCP/SSL connection
var options = new IrcClientOptionsBuilder()
    .WithNick("MyApp")
    .WithUserName("myapp")
    .WithRealName("My IRC Application")
    .AddServer("irc.libera.chat", 6697, useSsl: true)
    .AddAutoJoinChannels("#channel1", "#channel2")
    .AddAlternativeNick("MyApp_")
    .WithAutoReconnect(maxAttempts: 5)
    .WithSaslAuthentication("myapp", "<your-password>")
    .Build();

// WebSocket connection (no AddServer needed)
var wsOptions = new IrcClientOptionsBuilder()
    .WithNick("MyApp")
    .WithUserName("myapp")
    .WithRealName("My IRC Application")
    .WithWebSocket("wss://irc.unrealircd.org/")
    .WithCtcpAutoReply(true)
    .WithCtcpVersionString("MyApp v1.0")
    .Build();

// One-liner WebSocket build
var quickWs = new IrcClientOptionsBuilder()
    .WithNick("MyApp")
    .WithUserName("myapp")
    .WithRealName("My App")
    .BuildForWebSocket("wss://irc.unrealircd.org/");
```

## Dependency Injection

```csharp
// Single client
services.AddIrcClient(builder =>
{
    builder.WithNick("MyApp")
           .WithUserName("myapp")
           .WithRealName("My IRC Application")
           .AddServer("irc.libera.chat", 6697, useSsl: true);
});

// Multi-client manager (runs as IHostedService)
services.AddIrcBotManager(manager =>
{
    manager.AddBot("server1", b => b
        .WithNick("AppClient")
        .WithUserName("appclient")
        .WithRealName("My App - Server 1")
        .AddServer("irc.libera.chat", 6697, useSsl: true));
});
```

## Events

| Category | Events |
|----------|--------|
| Connection | `Connected`, `Disconnected`, `CapabilitiesNegotiated`, `SaslAuthentication` |
| Messages | `PrivateMessageReceived`, `NoticeReceived`, `MessageTagsReceived` |
| Channels | `UserJoinedChannel`, `ExtendedUserJoinedChannel`, `UserLeftChannel`, `UserKicked`, `TopicChanged`, `ChannelUsersReceived`, `ChannelJoinFailed`, `ChannelModeIsReceived`, `ChannelListReceived`, `ChannelListEndReceived`, `InviteReceived` |
| Users | `NickChanged`, `NicknameCollision`, `UserQuit`, `UserAwayStatusChanged`, `OwnAwayStatusChanged`, `UserAccountChanged`, `UserHostnameChanged` |
| Errors | `ErrorReplyReceived` — general catch-all for any IRC error numeric (482, 442, 461, etc.) |
| Advanced | `RawMessageReceived`, `BatchReceived`, `WhoReceived`, `WhoWasReceived` |
| CTCP | `CtcpRequestReceived`, `CtcpReplyReceived`, `CtcpActionReceived` |
| Enhanced | `OnEnhancedMessage`, `OnEnhancedConnected`, `OnEnhancedDisconnected`, `OnEnhancedUserJoined`, `OnGenericMessage`, `OnPreSendMessage` |

## Usage Examples

### Logging

Pass any `ILogger` or `ILogger<IrcClient>` to get structured diagnostics:

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
var logger = loggerFactory.CreateLogger<IrcClient>();

await using var client = new IrcClient(options, logger);
```

### Send Messages and Respond to Commands

```csharp
client.PrivateMessageReceived += (s, e) =>
{
    if (e.IsChannelMessage && e.Text.StartsWith("!hello"))
    {
        _ = client.SendMessageAsync(e.Target, $"Hello, {e.Nick}!");
    }
};
```

### Cancellation-Safe Messaging

`SendRawAsync` and `SendNoticeAsync` have `*WithCancellationAsync` variants for responsive shutdown:

```csharp
await client.SendRawWithCancellationAsync("PRIVMSG #channel :Hello!", cts.Token);
await client.SendNoticeWithCancellationAsync("someone", "Private notice", cts.Token);
```

### Browse Available Channels

`GetChannelListAsync` collects all LIST entries into a single awaitable result:

```csharp
var channels = await client.GetChannelListAsync(timeout: TimeSpan.FromSeconds(15));

foreach (var ch in channels.OrderByDescending(c => c.UserCount).Take(10))
{
    Console.WriteLine($"{ch.Channel} ({ch.UserCount} users): {ch.Topic}");
}
```

### SASL Authentication with NickServ Fallback

```csharp
var options = new IrcClientOptionsBuilder()
    .WithNick("SecureClient")
    .WithUserName("secureclient")
    .WithRealName("Secure IRC Client")
    .AddServer("irc.libera.chat", 6697, useSsl: true)
    .WithSaslAuthentication("secureclient", "<your-password>", required: false)
    .WithNickServPassword("<your-password>")  // fallback if SASL unavailable
    .Build();

var client = new IrcClient(options);

client.SaslAuthentication += (s, e) =>
{
    if (e.IsSuccessful)
        Console.WriteLine($"SASL {e.Mechanism} succeeded");
    else
        Console.WriteLine($"SASL failed: {e.ErrorMessage}");
};
```

### Enhanced Events with Built-in Response Methods

Enhanced events carry a client reference for fluent replies:

```csharp
client.OnEnhancedMessage += async (s, e) =>
{
    if (e.Text.StartsWith("!ping"))
    {
        await e.RespondAsync("Pong!");               // replies in channel or PM
        await e.RespondToUserAsync("Pong!");          // prefixes nick in channel
        await e.ReplyPrivatelyAsync("Secret pong!");  // always via PM
    }
};
```

### Pre-Send Interception (Filtering / Logging)

Inspect or cancel outgoing messages before they hit the wire:

```csharp
client.OnPreSendMessage += (e) =>
{
    // Log all outgoing messages
    logger.LogDebug("-> {Target}: {Message}", e.Target, e.Message);

    // Block messages containing forbidden words
    if (e.Message.Contains("badword", StringComparison.OrdinalIgnoreCase))
        e.IsCancelled = true;
};
```

### Track User Presence with IRCv3 Capabilities

```csharp
var options = new IrcClientOptionsBuilder()
    .WithNick("PresenceApp")
    .WithUserName("presenceapp")
    .WithRealName("Presence Tracker")
    .AddServer("irc.libera.chat", 6697, useSsl: true)
    .AddCapabilities("away-notify", "account-notify", "extended-join")
    .Build();

var client = new IrcClient(options);

client.UserAwayStatusChanged += (s, e) =>
    Console.WriteLine($"{e.Nick} is {(e.IsAway ? "away" : "back")}: {e.AwayMessage}");

client.UserAccountChanged += (s, e) =>
    Console.WriteLine($"{e.Nick} {(e.Account is null ? "logged out" : $"logged in as {e.Account}")}");

client.ExtendedUserJoinedChannel += (s, e) =>
    Console.WriteLine($"{e.Nick} joined {e.Channel} (account: {e.Account}, realname: {e.RealName})");
```

### Graceful Shutdown with Async Dispose

```csharp
await using var client = new IrcClient(options);

client.Connected += (s, e) => _ = client.JoinChannelAsync("#mychannel");

await client.ConnectAsync();

// ... run until shutdown signal ...

// DisposeAsync sends QUIT and cleans up all resources
```

### Server Feature Detection via ISUPPORT

After connecting, query server-advertised limits:

```csharp
client.Connected += (s, e) =>
{
    var network = client.GetServerNetworkName();       // e.g. "Libera.Chat"
    var maxNick = client.GetServerMaxNicknameLength();  // e.g. 16
    var types = client.GetServerChannelTypes();         // e.g. "#&"

    // Case-insensitive comparison using server rules
    bool same = client.NicknamesEqual("MyApp", "myapp"); // true
};
```

### Validate Input Before Sending

```csharp
if (client.IsValidNickname(userInput))
    await client.ChangeNickAsync(userInput);

if (client.IsValidChannelName(channelInput))
    await client.JoinChannelAsync(channelInput);
```

### Auto-Reconnect with Status Tracking

The library reconnects automatically with exponential backoff. Track connection state in your UI:

```csharp
var options = new IrcClientOptionsBuilder()
    .WithNick("MyApp")
    .WithUserName("myapp")
    .WithRealName("My App")
    .AddServer("irc.libera.chat", 6697, useSsl: true)
    .WithAutoReconnect(enabled: true, maxAttempts: 10,
        initialDelay: TimeSpan.FromSeconds(5),
        maxDelay: TimeSpan.FromMinutes(2))
    .Build();

var client = new IrcClient(options);

client.Connected += (s, e) =>
    UpdateStatusBar($"Connected as {e.Nick}");

client.Disconnected += (s, e) =>
    UpdateStatusBar($"Disconnected: {e.Reason} — reconnecting...");
```

### Nickname Fallback

Configure alternative nicknames in case your preferred nick is taken:

```csharp
var options = new IrcClientOptionsBuilder()
    .WithNick("MyApp")
    .WithUserName("myapp")
    .WithRealName("My App")
    .AddServer("irc.libera.chat", 6697, useSsl: true)
    .AddAlternativeNick("MyApp_")
    .AddAlternativeNick("MyApp__")
    .Build();

var client = new IrcClient(options);

// The client tries alternatives automatically during registration.
// Listen for collisions to track what happened:
client.NicknameCollision += (s, e) =>
    Console.WriteLine($"Nick '{e.CollidingNick}' taken, switched to '{e.FallbackNick}'");
```

### Connect to Private Servers with Self-Signed Certificates

```csharp
// TCP/SSL with self-signed cert
var options = new IrcClientOptions
{
    Server = "my-private-server.local",
    Port = 6697,
    UseSsl = true,
    AcceptInvalidSslCertificates = true,  // accept self-signed certs
    Password = "server-password",          // server PASS command
    Nick = "MyApp",
    UserName = "myapp",
    RealName = "My App"
};

// WebSocket with self-signed cert (also supported)
var wsOptions = new IrcClientOptions
{
    WebSocketUri = "wss://my-private-server.local:8000/",
    AcceptInvalidSslCertificates = true,
    Nick = "MyApp",
    UserName = "myapp",
    RealName = "My App"
};
```

### Connect via WebSocket

Use WebSocket transport to connect through firewalls, proxies, or to servers that only expose a WebSocket endpoint:

```csharp
var options = new IrcClientOptions
{
    WebSocketUri = "wss://irc.unrealircd.org/",
    Nick = "MyApp",
    UserName = "myapp",
    RealName = "My IRC Application",
};

await using var client = new IrcClient(options);

client.Connected += (s, e) =>
{
    Console.WriteLine($"Connected via WebSocket to {e.Network}");
    _ = client.JoinChannelAsync("#mychannel");
};

client.PrivateMessageReceived += (s, e) =>
    Console.WriteLine($"[{e.Target}] <{e.Nick}> {e.Text}");

await client.ConnectAsync();
```

The API is identical for both transports — all events, methods, and properties work the same way. The transport is selected automatically based on whether `WebSocketUri` is set.

### Track Channel Membership

The `Channels` property provides a live snapshot of joined channels and their users:

```csharp
// Check who is in a channel
if (client.Channels.TryGetValue("#mychannel", out var users))
{
    Console.WriteLine($"#mychannel has {users.Count} users:");
    foreach (var nick in users)
        Console.WriteLine($"  {nick}");
}

// Check if a specific user is in a channel
bool isOnline = client.Channels.TryGetValue("#mychannel", out var members)
    && members.Contains("SomeUser");

// List all channels the client is in
foreach (var channel in client.Channels.Keys)
    Console.WriteLine(channel);
```

### Guard Sends with Connection State

Check `IsConnected` and `IsRegistered` before sending in UI applications:

```csharp
async Task SendSafeAsync(IrcClient client, string target, string message)
{
    if (!client.IsConnected)
    {
        ShowError("Not connected to server");
        return;
    }

    if (!client.IsRegistered)
    {
        ShowError("Still registering with server, please wait...");
        return;
    }

    await client.SendMessageAsync(target, message);
}
```

### Handle Protocol Errors

Channel join failures arrive via the `ChannelJoinFailed` event with the IRC error code:

```csharp
client.ChannelJoinFailed += (s, e) =>
{
    Console.WriteLine($"Cannot join {e.Channel}: {e.Reason} (code: {e.ErrorCode})");
    // e.ErrorCode is the IRC numeric: "473" (invite-only), "474" (banned),
    // "475" (bad key), "471" (full), "477" (need registration), "405" (too many channels)
};

// General error handler for any IRC error (fires alongside specific events)
client.ErrorReplyReceived += (s, e) =>
    Console.WriteLine($"IRC error {e.ErrorCode}: {e.Target} — {e.ErrorMessage}");

// Track channel invitations
client.InviteReceived += (s, e) =>
    Console.WriteLine($"{e.Nick} invited you to {e.Channel}");

// Server confirms your away status
client.OwnAwayStatusChanged += (s, e) =>
    Console.WriteLine(e.IsAway ? "You are now away" : "You are no longer away");

// Query channel modes
await client.SendRawAsync("MODE #channel");
client.ChannelModeIsReceived += (s, e) =>
    Console.WriteLine($"{e.Channel} modes: {e.Modes} {e.ModeParams}");
```

For protocol-level error handling, the library provides a typed exception hierarchy via `IrcErrorHandler`:

```csharp
using IRCDotNet.Core.Protocol;

var error = IrcErrorHandler.HandleNumericError(rawMessage);
if (error is IrcChannelBannedException) { /* banned */ }
else if (error is IrcNicknameInUseException) { /* nick taken */ }
else if (error is IrcAuthenticationException) { /* auth failed */ }
```

Exception hierarchy:

- `IrcProtocolException` — base class (carries `NumericCode`)
  - `IrcChannelException` — channel errors (`IrcChannelBannedException`, `IrcChannelInviteOnlyException`, `IrcChannelFullException`, `IrcChannelKeyException`, `IrcChannelPermissionException`, `IrcNotOnChannelException`)
  - `IrcNicknameException` — nickname errors (`IrcNicknameInUseException`, `IrcInvalidNicknameException`, `IrcNicknameCollisionException`)
  - `IrcAuthenticationException` — password/SASL failures
  - `IrcTargetNotFoundException`, `IrcChannelNotFoundException` — target doesn't exist
  - `IrcUnknownCommandException`, `IrcNotRegisteredException`, `IrcAlreadyRegisteredException`
  - `IrcInsufficientParametersException` — not enough parameters for a command
  - `IrcValidationException` — input validation failures

### Configure Rate Limiting

Rate limiting prevents the server from kicking you for flooding. It's enabled by default:

```csharp
using IRCDotNet.Core.Protocol;

// Use a custom rate limit (2 messages/sec, burst of 10)
var options = new IrcClientOptionsBuilder()
    .WithNick("MyApp")
    .WithUserName("myapp")
    .WithRealName("My App")
    .AddServer("irc.libera.chat", 6697, useSsl: true)
    .WithRateLimit(enabled: true, new RateLimitConfig(
        refillRate: 2.0,   // tokens per second
        bucketSize: 10))   // max burst
    .Build();

// Or disable entirely for testing
var testOptions = new IrcClientOptionsBuilder()
    .WithNick("TestApp")
    .WithUserName("testapp")
    .WithRealName("Test")
    .AddServer("localhost", 6667)
    .WithoutRateLimit()
    .Build();
```

### Parse and Inspect Raw Messages

Use `IrcMessage` for low-level protocol work:

```csharp
using IRCDotNet.Core.Protocol;

// Parse a raw IRC line
var msg = IrcMessage.Parse(":nick!user@host PRIVMSG #channel :Hello world");
Console.WriteLine(msg.Command);        // "PRIVMSG"
Console.WriteLine(msg.Source);         // "nick!user@host"
Console.WriteLine(msg.Parameters[0]);  // "#channel"
Console.WriteLine(msg.Parameters[1]);  // "Hello world"

// Build and serialize a message
var outgoing = new IrcMessage
{
    Command = IrcCommands.PRIVMSG,
    Parameters = { "#channel", "Hello!" }
};
Console.WriteLine(outgoing.Serialize()); // "PRIVMSG #channel :Hello!"

// Listen for any raw message
client.RawMessageReceived += (s, e) =>
{
    if (e.Message.Command == IrcNumericReplies.RPL_TOPIC)
        Console.WriteLine($"Topic: {e.Message.Parameters[2]}");
};
```

### Strip IRC Formatting

Remove mIRC color codes, bold, italic, and underline from messages:

```csharp
using IRCDotNet.Core.Protocol;

client.PrivateMessageReceived += (s, e) =>
{
    // e.Text may contain \x02bold\x02 or \x0304,01red text\x03
    var plainText = IrcEncoding.StripIrcFormatting(e.Text);
    Console.WriteLine(plainText); // clean text without formatting
};
```

### CTCP: /me Actions

Send and receive `/me` actions:

```csharp
// Display actions in chat
client.CtcpActionReceived += (s, e) =>
    Console.WriteLine($"* {e.Nick} {e.ActionText}");

// Send a /me action to a channel or user
await client.SendActionAsync("#channel", "waves hello");
```

### CTCP: Query Client Version

Request and receive CTCP VERSION from other users:

```csharp
// Send a VERSION request
await client.SendCtcpRequestAsync("SomeUser", "VERSION");

// Handle the reply
client.CtcpReplyReceived += (s, e) =>
{
    if (e.Command == "VERSION")
        Console.WriteLine($"{e.Nick} is using: {e.ReplyText}");
};
```

The library auto-replies to VERSION, PING, TIME, CLIENTINFO, FINGER, SOURCE, USERINFO, and ERRMSG by default. Disable with:

```csharp
options.EnableCtcpAutoReply = false;
```

Customize the VERSION reply:

```csharp
options.CtcpVersionString = "MyChatApp v1.0";
// Default: "IRCDotNet.Core <assembly-version>"
```

### Protocol Constants

Use typed constants instead of magic strings with `SendRawAsync` and `RawMessageReceived`:

```csharp
using IRCDotNet.Core.Protocol;

// IRC commands
await client.SendRawAsync($"{IrcCommands.PRIVMSG} #channel :Hello");
await client.SendRawAsync($"{IrcCommands.NAMES} #channel");

// Numeric replies in event handlers
client.RawMessageReceived += (s, e) =>
{
    switch (e.Message.Command)
    {
        case IrcNumericReplies.RPL_TOPIC:      // "332"
        case IrcNumericReplies.RPL_TOPICWHOTIME: // "333"
            break;
        case IrcNumericReplies.ERR_BANNEDFROMCHAN: // "474"
            break;
    }
};

// IRCv3 capability names
if (client.EnabledCapabilities.Contains(IrcCapabilities.SERVER_TIME))
    Console.WriteLine("Server-time is available");
```

## API Reference

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsConnected` | `bool` | Whether the client is currently connected |
| `IsRegistered` | `bool` | Whether IRC registration (NICK/USER) is complete |
| `CurrentNick` | `string` | The client's current nickname on the server |
| `Channels` | `IReadOnlyDictionary` | Snapshot of joined channels mapped to user sets |
| `EnabledCapabilities` | `IReadOnlySet<string>` | IRCv3 capabilities enabled for this connection |
| `Configuration` | `IrcClientOptions` | The configuration snapshot used to create this client (cloned at construction time) |

### Methods

> `SendRawAsync` and `SendNoticeAsync` have `*WithCancellationAsync` variants that accept a `CancellationToken`. `ConnectAsync` and `GetChannelListAsync` accept `CancellationToken` directly.

| Category | Methods |
|----------|---------|
| Connection | `ConnectAsync`, `DisconnectAsync`, `SendRawAsync` |
| Channels | `JoinChannelAsync`, `LeaveChannelAsync`, `SetTopicAsync`, `GetTopicAsync`, `GetChannelUsersAsync`, `GetChannelListAsync`, `RequestChannelListAsync` |
| Messaging | `SendMessageAsync`, `SendNoticeAsync`, `SendMessageWithTagsAsync`, `SendTagMessageAsync` |
| CTCP | `SendActionAsync`, `SendCtcpRequestAsync`, `SendCtcpReplyAsync` |
| Queries | `WhoAsync`, `WhoWasAsync`, `GetUserInfoAsync`, `ListChannelsAsync` |
| User | `ChangeNickAsync`, `SetAwayAsync`, `SetRealNameAsync`, `SetUserModeAsync` |
| Admin | `KickUserAsync`, `InviteUserAsync`, `SetChannelModeAsync`, `GetChannelModeAsync` |
| IRCv3 | `MonitorNickAsync`, `UnmonitorNickAsync` |
| Utilities | `NicknamesEqual`, `ChannelNamesEqual`, `IsValidNickname`, `IsValidChannelName`, `EncodeMessage` |
| Server Info | `GetServerNetworkName`, `GetServerMaxNicknameLength`, `GetServerMaxChannelLength`, `GetServerChannelTypes` |
| Lifecycle | `Dispose`, `DisposeAsync` |

## Changelog

### v2.3.1

**Bug Fixes:**
- `IrcEncoding.StripIrcFormatting` now delegates to `IrcFormattingStripper.Strip` — handles full set of formatting codes (hex colors `\x04`, monospace `\x11`, strikethrough `\x1E`) instead of the incomplete manual parser
- Fixed `\x0312` hex escape bug in tests (C# parses as U+0312, not `\x03` + `12` — same class of bug as the CTCP `\x01AC` issue)
- Fixed 4 broken XML doc `cref` links in `IrcClientOptions` after namespace rename
- Added missing XML docs on 5 public members in example files

**Docs:**
- README: corrected dependency claim ("zero external" → accurate list)
- README: added `invite-notify` to IRCv3 capabilities list, clarified `labeled-response` as negotiation-only
- README: completed exception hierarchy (added `IrcInsufficientParametersException`, `IrcValidationException`)
- Zero build warnings (0 CS1574, 0 CS1591)

### v2.3.0

**Breaking — Namespace Rename:**
- All namespaces renamed from `IRCDotNet.*` to `IRCDotNet.Core.*` to match the NuGet package name
- `using IRCDotNet;` → `using IRCDotNet.Core;`
- `using IRCDotNet.Configuration;` → `using IRCDotNet.Core.Configuration;`
- `using IRCDotNet.Events;` → `using IRCDotNet.Core.Events;`
- `using IRCDotNet.Protocol;` → `using IRCDotNet.Core.Protocol;`
- `using IRCDotNet.Transport;` → `using IRCDotNet.Core.Transport;`
- `using IRCDotNet.Utilities;` → `using IRCDotNet.Core.Utilities;`

**Other Changes:**
- Repository moved to https://github.com/etushar89/IRCDotNet.Core
- CTCP SOURCE auto-reply URL updated to new repo
- Removed `Roslynator.Analyzers` dev dependency

### v2.2.0

**New Events:**
- `InviteReceived` — fired when the client is invited to a channel
- `OwnAwayStatusChanged` — server confirms away status (RPL_UNAWAY 305 / RPL_NOWAWAY 306)
- `ChannelModeIsReceived` — response to `MODE #channel` query (RPL_CHANNELMODEIS 324)
- `ErrorReplyReceived` — general catch-all for any IRC error numeric (482, 442, 461, etc.)

**Bug Fixes:**
- `ERR_NOCHANMODES` (477) now routed to `ChannelJoinFailed` — fixes silent failure on channels requiring NickServ identification (e.g., Libera.Chat `#networking`)
- `ERR_TOOMANYCHANNELS` (405) now routed to `ChannelJoinFailed`
- `ERR_CHANOPRIVSNEEDED` (482), `ERR_NOTONCHANNEL` (442), `ERR_NEEDMOREPARAMS` (461) now routed to `ErrorReplyReceived`
- `RPL_NOTOPIC` (331) now fires `TopicChanged` with empty topic (previously dropped)
- Case-insensitive `_channels` dictionary per RFC 2812 Section 1.3

**Improvements:**
- INVITE command handler added (previously could send invites but not receive them)
- `RPL_TOPICWHOTIME` (333) now logged
- 754 unit tests (42 new for this release)

### v2.1.1

**Bug Fixes:**
- Echo-message CTCP fix: skip echoed CTCP requests/replies when `echo-message` capability is enabled (prevented auto-replies to self)

### v2.1.0

**New Features:**
- CTCP (Client-To-Client Protocol) support — ACTION (`/me`), VERSION, PING, TIME, CLIENTINFO, FINGER, SOURCE, USERINFO, ERRMSG with configurable auto-replies
- WebSocket transport — connect via `wss://` or `ws://` endpoints (UnrealIRCd, InspIRCd, KiwiIRC gateways)
- `IIrcTransport` interface for transport abstraction (`TcpIrcTransport`, `WebSocketIrcTransport`)
- Fluent builder API: `WithWebSocket()`, `BuildForWebSocket()`, `WithCtcpAutoReply()`, `WithCtcpVersionString()`
- Events: `CtcpRequestReceived`, `CtcpReplyReceived`, `CtcpActionReceived`
- `SendActionAsync`, `SendCtcpRequestAsync`, `SendCtcpReplyAsync` methods

**Bug Fixes:**
- CTCP `\x01` delimiter: use `\u0001` everywhere to avoid C# hex escape parsing issue (`\x01AC` → U+01AC)

### v2.0.2

- Initial NuGet release
- Full RFC 1459 + IRCv3 protocol implementation
- TCP/SSL transport with SASL authentication
- 30+ typed events, auto-reconnect, rate limiting, DI integration

## Requirements

- .NET 8.0 or later (targets `net8.0`; compatible with .NET 9 and .NET 10 but not explicitly tested)
- No platform-specific dependencies — works on Windows, macOS, and Linux

## Links

- [GitHub Repository](https://github.com/etushar89/IRCDotNet.Core)
- [Report Issues](https://github.com/etushar89/IRCDotNet.Core/issues)

## License

MIT — see [LICENSE](https://github.com/etushar89/IRCDotNet.Core/blob/main/LICENSE) for details.
