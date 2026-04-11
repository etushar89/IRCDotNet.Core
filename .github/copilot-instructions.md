# Copilot Instructions for IRCDotNet.Core

**Do not commit or push without explicit human review and testing.**

## Build Commands
- **Library**: `dotnet build src/IRCDotNet.Core -c Release`
- **Unit tests**: `dotnet test tests/IRCDotNetCore.Tests -v quiet`
- **Live server tests**: `dotnet test tests/IRCDotNetCore.ConcurrencyTests --filter ExtendedEvent`
- **Pack NuGet**: `dotnet pack src/IRCDotNet.Core -c Release`

## Project Structure
- **src/IRCDotNet.Core/** — Core IRC client library (.NET 8), published as `IRCDotNet.Core` on NuGet
- **tests/IRCDotNetCore.Tests/** — xUnit unit tests (754 tests, 751 pass, 3 known pre-existing failures)
- **tests/IRCDotNetCore.ConcurrencyTests/** — Live server integration tests (HybridIRC, UnrealIRCd, Libera.Chat)

## Namespaces
- Root: `IRCDotNet.Core`
- Sub-namespaces: `IRCDotNet.Core.Configuration`, `IRCDotNet.Core.Events`, `IRCDotNet.Core.Protocol`, `IRCDotNet.Core.Transport`, `IRCDotNet.Core.Utilities`, `IRCDotNet.Core.Extensions`
- Test namespaces: `IRCDotNet.Tests.*`, `IRCDotNet.ConcurrencyTests`

## Tech Stack
- .NET 8 LTS (do not upgrade beyond LTS)
- Zero external runtime dependencies beyond `Microsoft.Extensions.*` 8.0
- NuGet package ID: `IRCDotNet.Core`

## Code Conventions
- Line endings: LF enforced via `dotnet format --verify-no-changes` in Directory.Build.props pre-build target
- New files must use LF line endings
- XML documentation required on all public members (`<summary>`, `<param>`, `<returns>`)
- Use `ConfigureAwait(false)` on all async calls in the library (non-UI code)
- Use `StringComparison.OrdinalIgnoreCase` for IRC channel/nick comparisons (RFC 2812)

## Known Test Failures (3 pre-existing, unrelated to current work)
- `CacheInvalidationTests.InvalidateAllCaches_ShouldClearAllCaches`
- `ResilienceTests.ConcurrentConnectAndDisconnect_ShouldHandleGracefully`
- `ThreadSafetyAdvancedTests.StateTransitions_UnderConcurrentAccess_ShouldRemainConsistent`

## Key Architecture Decisions
- **Transport abstraction**: `IIrcTransport` interface with `TcpIrcTransport` and `WebSocketIrcTransport` implementations
- **Case-insensitive channels**: `_channels` dictionary uses `StringComparer.OrdinalIgnoreCase` per RFC 2812
- **CTCP delimiter**: Always use `\u0001` (not `\x01` — C# parses `\x01AC` as U+01AC)
- **Echo-message**: isEcho check runs before CTCP detection to prevent auto-replies to self
- **Event dispatch**: `RaiseEventAsync` with configurable dispatchers (threaded, sequential, background)
- **Rate limiting**: Token-bucket algorithm via `IrcRateLimiter`, configurable per-client
- **Auto-reconnect**: Exponential backoff with channel rejoin list preserved across reconnects

## IRC Protocol Notes
- Channel names are case-insensitive (RFC 2812 Section 1.3)
- IRC messages are limited to 512 bytes including CRLF
- NAMES reply (353) arrives after JOIN confirmation — not before
- ERR_NOCHANMODES (477) is used by Libera/Solanum for "need to identify with services"
- Some servers (UnrealIRCd) send WebSocket frames without \r\n — use EndOfMessage fallback

## IRC Specifications
- **RFC 1459** — Internet Relay Chat Protocol (original, 1993): https://www.rfc-editor.org/rfc/rfc1459
- **RFC 2812** — Internet Relay Chat: Client Protocol (updated, 2000): https://www.rfc-editor.org/rfc/rfc2812
- **RFC 2813** — Internet Relay Chat: Server Protocol: https://www.rfc-editor.org/rfc/rfc2813
- **IRCv3 Specifications** — Modern extensions (capabilities, message tags, SASL, etc.): https://ircv3.net/irc/
- **Modern IRC Client Protocol** — Living document for current best practices: https://modern.ircdocs.horse/
- **CTCP Specification** — Client-To-Client Protocol: https://modern.ircdocs.horse/ctcp

## NuGet Publish Checklist
1. Bump `Version` in `src/IRCDotNet.Core/IRCDotNet.Core.csproj`
2. Update `PackageReleaseNotes` in csproj with the new version's changes
3. Update `Description` in csproj if feature count or summary changed (e.g., event count)
4. Update `PackageTags` in csproj if new feature categories were added
5. Update relevant sections in `README.md` (features, events table, usage examples, API reference)
6. Add new version entry to the Changelog section in `README.md`
7. Run all tests: `dotnet test tests/IRCDotNetCore.Tests -v quiet`
8. Commit all changes with clean working tree
9. Pack: `dotnet pack src/IRCDotNet.Core -c Release`
10. Publish: `dotnet nuget push src/IRCDotNet.Core/bin/Release/IRCDotNet.Core.X.Y.Z.nupkg --source https://api.nuget.org/v3/index.json --api-key <KEY>`
11. Git tag: `git tag vX.Y.Z && git push origin vX.Y.Z`

## Common Pitfalls
- PowerShell string replacement introduces CRLF — always convert back to LF after bulk edits
- `IrcMessage.Parse()` handles trailing parameters with `:` prefix correctly
- `HandleJoin` overwrites `_channels[channel]` — NAMES data arriving before JOIN would be lost (server sends NAMES after JOIN per RFC)
- Test projects need explicit `using IRCDotNet.Core;` — the test namespace `IRCDotNet.Tests` doesn't implicitly resolve types from `IRCDotNet.Core`
