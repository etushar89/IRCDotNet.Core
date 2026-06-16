# Review: IRCDotNet.Core post-2.5.1 (b5ac22e..HEAD)

## Verdict
APPROVE WITH CHANGES — server-time, +/! channel routing, IsupportReceived, keepalive starvation,
and SemaphoreSlim disposal contract are landed correctly, but two protocol-level event-contract
regressions and four medium-severity case-mapping/MONITOR-numeric gaps need to be addressed
before publishing as a follow-up release.

## Scope Read
- src/IRCDotNet.Core/IrcClient.cs (full, 3576 lines)
- src/IRCDotNet.Core/Events/IrcEvents.cs (full, 1276 lines)
- src/IRCDotNet.Core/Protocol/IrcCapabilities.cs (diff)
- src/IRCDotNet.Core/Protocol/IrcMessage.cs (lines 1-300)
- src/IRCDotNet.Core/Protocol/IsupportParser.cs (lines 1-300)
- src/IRCDotNet.Core/Protocol/IrcNumericReplies.cs (lines 370-395)
- src/IRCDotNet.Core/Protocol/IrcRateLimiter.cs (header + DefaultConfig)
- src/IRCDotNet.Core/Protocol/IrcCaseMapping.cs (lines 1-220)
- src/IRCDotNet.Core/Utilities/ConcurrentHashSet.cs (lines 1-60)
- src/IRCDotNet.Core/Configuration/IrcClientOptions.cs (diff)
- src/IRCDotNet.Core/IIrcClient.cs (diff)
- tests/IRCDotNetCore.Tests/IrcClientTests.cs (684-line additions)
- tests/IRCDotNetCore.Tests/IrcClientMonitorCorrelationTests.cs (full)
- tests/IRCDotNetCore.Tests/Threading/SendDisposalRaceTests.cs (full)
- tests/IRCDotNetCore.Tests/Events/RecentEnhancedEventsTests.cs (diff)
- tests/IRCDotNetCore.Tests/Events/TypingIndicatorTests.cs (diff)
- tests/IRCDotNetCore.Tests/Protocol/IsupportParserTests.cs (diff)
- specs fetched this turn:
  - IRCv3 MONITOR (`https://ircv3.net/specs/extensions/monitor.html`) — numerics 730-734, command modifiers, "MONITOR ... allowing users to avoid nick-change stalking"
  - IRCv3 server-time (`https://ircv3.net/specs/extensions/server-time`) — ISO 8601 `YYYY-MM-DDThh:mm:ss.sssZ`, "client SHOULD treat the message as having occurred at the given time"
- specs cited from prior context (NOT re-fetched this turn — see Unverified): RFC 2812 §1.3 case mapping; RFC 2811 §2 channel types; RFC 2812 §3.2.5 NAMES.

## Blockers
None.

## Major

### M1. MONITOR-tracked nick is silently dropped when the server NICKs with a different case
- Observation: [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L65) declares
  `private readonly ConcurrentHashSet<string> _monitoredNicks = new();`. `ConcurrentHashSet<T>`
  ([src/IRCDotNet.Core/Utilities/ConcurrentHashSet.cs](src/IRCDotNet.Core/Utilities/ConcurrentHashSet.cs#L12))
  wraps `new ConcurrentDictionary<T, byte>()` with no comparer — so for `T = string` membership is
  case-sensitive Ordinal. `ApplyNickChange`
  ([src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L2913))
  branches on `_monitoredNicks.Contains(oldNick)`; the same case-sensitive path is used for the
  subsequent `Remove(oldNick) / Add(newNick)` ([lines 2947-2948](src/IRCDotNet.Core/IrcClient.cs#L2947-L2948)).
- Why it's wrong: nick comparison is required to use the server's CASEMAPPING (RFC 2812 §1.3 — "the characters {}|^ are considered to be the lower case equivalents of the characters []\\~ ... critical issue when determining the equivalence of two nicknames or channel names"). A server that locally lower-cases nicks before relaying NICK commands (common on `rfc1459` casemapping ircds) will deliver `:Bob!u@h NICK :bob`, after which `_monitoredNicks.Contains("Bob")` returns `false`. The client then leaves the stale `"Bob"` in its monitor list, never sends `MONITOR + bob` via `RefreshMonitoredNickAsync`, and the user effectively disappears from monitor coverage with no diagnostic.
- Suggested change: construct `_monitoredNicks` with an IRC-aware comparer that tracks the
  current `_isupportParser.CaseMapping` (the same way `_channels` is keyed via
  `StringComparer.OrdinalIgnoreCase` today, ideally via the existing
  `IrcCaseMapping.CreateComparer(...)` helper so RFC 1459 `[]\^` ↔ `{}|~` equivalence works).
  When CASEMAPPING is renegotiated mid-session, rebuild the set under the new comparer.

### M2. Synthetic UserQuit on RPL_MONOFFLINE has a TOCTOU race with concurrent RPL_MONONLINE
- Observation: `HandleMonitorOffline`
  ([src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L2888))
  enqueues `_pendingMonitoredOfflines[target.Nick] = pending` and fires
  `FinalizePendingMonitoredOfflineAsync(pending)` after a 750 ms delay. The finalizer at
  [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L2900) (verified region around
  the `_userInfo.TryRemove(pending.Nick, out _)` call) uses an unsynchronized
  TryGetValue → TryRemove → TryRemove → RaiseEventAsync sequence. `HandleMonitorOnline`
  ([src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L2884))
  does `_pendingMonitoredOfflines.TryRemove(target.Nick, out _);`, but only the dictionary entry —
  not a coordinated cancellation of the in-flight finalizer.
- Why it's wrong: a 731 followed by a 730 within 750 ms (well within bouncer-replay or netsplit-recovery
  timing) can interleave as: finalize.TryGetValue(success) → online.TryRemove(pending entry) →
  online.UpdateUserInfo(...) → finalize.TryRemove(_userInfo) → finalize.RaiseEventAsync(UserQuit).
  The user is online on the server, the local `_userInfo` cache for them is now empty, and
  consumers receive a UserQuit for a still-online user — a data-integrity violation (Priority 1).
  The IRCv3 MONITOR spec ("a target has just become online, or that a target they have added to
  their monitor list is online") makes 730 the authoritative cancel signal.
- Suggested change: gate finalization on a per-pending CancellationTokenSource that
  `HandleMonitorOnline` cancels; or atomically swap the pending entry with a sentinel and treat
  finalization as no-op if the sentinel is observed. Either way, the eviction of `_userInfo` and
  the UserQuit emission must observe the cancel signal under the same memory-ordering as the
  pending-entry mutation — a plain dictionary `TryRemove` is not enough.

## Medium

### Med1. RPL_MONLIST (732), RPL_ENDOFMONLIST (733), and ERR_MONLISTFULL (734) are unhandled
- Observation: constants exist in
  [src/IRCDotNet.Core/Protocol/IrcNumericReplies.cs](src/IRCDotNet.Core/Protocol/IrcNumericReplies.cs#L380-L384):
  ```
  public const string RPL_MONLIST       = "732";
  public const string RPL_ENDOFMONLIST  = "733";
  public const string ERR_MONLISTFULL   = "734";
  ```
  but a global file search for `RPL_MONLIST | RPL_ENDOFMONLIST | ERR_MONLISTFULL` outside that
  file returns zero matches in `src/IRCDotNet.Core` — they are never dispatched in
  `ProcessMessageAsync` / `HandleNumericReply`.
- Why it's wrong: `MonitorNickAsync` populates `_monitoredNicks` BEFORE the server has accepted
  the addition. Per the IRCv3 MONITOR spec, "this numeric is used to indicate to a client that
  their monitor list is full, so the command failed. The `<limit>` parameter is the maximum
  number of targets a client may have in their list, the `<targets>` parameter is the list of
  targets ... that cannot be added." The current code never removes the rejected targets from
  `_monitoredNicks`, so subsequent `ApplyNickChange` will spuriously fire `RefreshMonitoredNickAsync`
  for nicks the server is not actually tracking, and `MonitorListAsync()` (if exposed) would not
  reconcile against `RPL_MONLIST`/`RPL_ENDOFMONLIST`. This is observability + data-integrity.
- Suggested change: route 732/733/734 to dedicated handlers. 734 must remove the listed targets
  from `_monitoredNicks` and surface a typed `MonitorListFullEvent` (or at minimum an
  `ErrorReplyEvent`). 732/733 should populate a per-list-snapshot result that the public
  `IrcClient` API can return (or raise a `MonitorListReceivedEvent`).

### Med2. `_userInfo` is a case-sensitive dictionary used in case-insensitive contexts
- Observation: [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L64)
  `private readonly ConcurrentDictionary<string, UserInfo> _userInfo = new();`. The default
  constructor uses `EqualityComparer<string>.Default`, which is `StringComparer.Ordinal`
  (case-sensitive). The same field is queried in
  [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L2929)
  (`_userInfo.TryRemove(oldNick, out var userInfo)`) and in `CreatePendingMonitoredOffline`.
- Why it's wrong: same RFC 2812 §1.3 violation as M1, but on the user-presence cache. A server
  that varies casing across PRIVMSG / NICK / MONITOR will end up with two distinct `_userInfo`
  entries for the same logical user, and `ApplyNickChange` will fail to migrate the old entry
  when the case of `oldNick` does not match the cached key.
- Suggested change: construct `_userInfo` with the same IRC-aware comparer recommended for M1,
  and audit all lookups (`UpdateUserInfo`, `_userInfo.TryGetValue`, `_userInfo.TryRemove`) to
  ensure they use the parser's CASEMAPPING after 005 has been received.

### Med3. echo-message detection uses Ordinal case mapping instead of server CASEMAPPING
- Observation: [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L1844-L1845)
  and [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L1854-L1855):
  ```csharp
  var isEcho = _enabledCapabilities.Contains("echo-message") &&
               string.Equals(nick, _currentNick, StringComparison.OrdinalIgnoreCase);
  ```
- Why it's wrong: The library already exposes `GetServerCaseMapping()` and an
  `IrcCaseMapping.Equals(a, b, mappingType)` helper specifically for this purpose
  ([src/IRCDotNet.Core/Protocol/IrcCaseMapping.cs](src/IRCDotNet.Core/Protocol/IrcCaseMapping.cs#L163-L177)).
  When CASEMAPPING is `rfc1459` (the IRCv3-default) the server may relay our self-nick with
  different case folding for the bracket characters (`[]\^` ↔ `{}|~`). With OrdinalIgnoreCase,
  the echo gate misfires, the CTCP auto-reply branch can fire on our own ACTION/CTCP, and the
  `IsEcho` field on `NoticeEvent` / `CtcpActionEvent` is computed against the wrong identity.
- Suggested change: replace both `OrdinalIgnoreCase` calls with
  `IrcCaseMapping.Equals(nick, _currentNick, _isupportParser.CaseMapping)`. (The recently-merged
  `+`/`!` channel-prefix tests already cover this kind of conformance gap; the same mindset
  applies to nick equivalence.)

### Med4. Mid-NAMES self-PART/QUIT can resurrect a freshly-left channel
- Observation: `HandleEndOfNames` at
  [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L2024-L2050) does
  `_channels[channel] = replacementSet;` unconditionally after applying any queued
  ADD/REMOVE/RENAME mutations from `_pendingNamesUsers`. The `HandlePart` /
  `HandleQuit` / `HandleKick` paths remove the channel from `_channels` if we are the
  departing user, and reset our pending NAMES state — but the convergence still emits a
  `ChannelUsersReceived` event AND re-creates `_channels[channel]` if the server sends the final
  RPL_ENDOFNAMES after we already left.
- Why it's wrong: the lifecycle invariant "we are not in the channel set ⇒ no `_channels[channel]`
  entry" is violated. Downstream consumers binding to `Channels` see a phantom membership and
  may try to send to a channel we are not in, producing ERR_NOTONCHANNEL (442) on next operation.
- Suggested change: in `HandleEndOfNames`, if `pendingNamesState` is no longer associated with
  an active membership (e.g. `_channels.ContainsKey(channel)` is false at the moment of
  convergence), drop the snapshot and do not emit `ChannelUsersReceived`. Equivalently,
  `HandlePart`/`HandleQuit`/`HandleKick` should remove the matching key from
  `_pendingNamesUsers` when the departing user is self.

## Minor

### Min1. `HandleNickChange` strips a leading `:` that the parser already removed
- Observation: [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L1893)
  `var newNick = message.Parameters[0].TrimStart(':');`. `IrcMessage.Parse()` strips the
  trailing-parameter `:` prefix at
  [src/IRCDotNet.Core/Protocol/IrcMessage.cs](src/IRCDotNet.Core/Protocol/IrcMessage.cs#L178)
  before adding to `Parameters`. RFC 2812 §2.3.1 nickname grammar disallows `:` as a nickname
  character.
- Why it's wrong: dead defensive code — but TrimStart removes ALL leading `:` characters,
  which on malformed parser output (`::foo`) would silently swallow the corruption rather than
  surface it. Reads as if the nickname could legitimately have a leading `:`.
- Suggested change: drop the `TrimStart(':')` and rely on `Parameters[0]` directly. If
  defensive validation is desired, validate against RFC 2812 nickname grammar and surface a
  `ProtocolError` when violated.

### Min2. `HandleMode` recognizes only `#` and `&` channel prefixes
- Observation: [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L3263-L3273)
  branches on `target.StartsWith("#") || target.StartsWith("&")` to distinguish channel-mode
  from user-mode. The newly-merged work in `Events/IrcEvents.cs` (typing, action, message)
  consistently recognizes `+` and `!` as channel prefixes per the ISUPPORT default
  `CHANTYPES=#&+!`.
- Why it's wrong: a MODE command on a `+modeless` or `!safechan` is now treated as a user-mode
  set, missing the `ChannelModeChangedEvent` path. Consistency with the rest of the channelness
  detection just merged.
- Suggested change: switch the conditional to the same `target.Length > 0 && target[0] is '#'
  or '&' or '+' or '!'` shape used in `IsChannelTyping` (or, better, route through
  `_isupportParser.ChannelTypes` so it adapts to per-server CHANTYPES advertisements).

### Min3. Synthetic `UserQuit` from MONITOR offline lacks a quit reason and conflates lifecycle semantics
- Observation: `FinalizePendingMonitoredOfflineAsync` raises
  `RaiseEventAsync(UserQuit, new UserQuitEvent(pending.Message, pending.Nick, pending.User, pending.Host));`.
  `UserQuitEvent.Reason` will be unset/empty — `pending.Message` is the 731 RPL_MONOFFLINE, not a
  real `QUIT`. Per the IRCv3 MONITOR spec the numeric only signals "target has just left the irc
  network, or ... is offline."
- Why it's wrong: consumers that branch on `UserQuit.Reason` (e.g. UI that distinguishes
  "Excess Flood" vs "Ping timeout") cannot tell a real QUIT from a MONITOR-driven synthesized
  one. There is also no way for the consumer to opt out of the synthesis.
- Suggested change: emit a dedicated `MonitorOfflineFinalizedEvent` (or annotate the event with
  an `IsSynthetic` flag), and downgrade the auto-`UserQuit` to opt-in via `IrcClientOptions`. At
  minimum, populate `Reason` with a constant marker (`"MONITOR offline"`).

## Nits

### Nit1. `_supportedCapabilities` / `_enabledCapabilities` use case-sensitive Ordinal
- IRCv3 capability identifiers are documented as case-sensitive in the IRCv3 capability-negotiation
  spec ("Capability identifiers consist of one or more characters from the set [a-zA-Z0-9-]. They
  are case-sensitive."), so the current Ordinal comparer is technically conformant. Flag for
  awareness only — no change needed.

## Unverified / Needs Follow-up
- RFC 2812 §1.3 (case mapping), RFC 2811 §2 (channel types), and RFC 2812 §3.2.5 (NAMES) were
  cited from prior context, not re-fetched this turn. The IRCv3 MONITOR and server-time specs
  WERE re-fetched and quoted directly. Findings M1, Med2, Med3 hinge on RFC 2812 §1.3 — if a
  re-fetch produces different wording, downgrade those findings to "verified by IRCv3
  modern-irc convention" rather than RFC.
- The `IRCDotNetCore.ConcurrencyTests/PrivateMessageTests.cs` 449-line addition was not read in
  this review — it's a live-server suite gated behind external IRCd availability. Any
  regression there is not covered by these findings.
- The `_pendingNamesUsers` per-channel state was inspected for the convergence path but not for
  cross-channel rejoin scenarios (channel left + immediately rejoined while a stale 366 is still
  in flight). Worth a targeted test before claiming the convergence is fully sound.

## Confirmed Good
- `IrcEvent.Timestamp`
  ([src/IRCDotNet.Core/Events/IrcEvents.cs](src/IRCDotNet.Core/Events/IrcEvents.cs#L17-L41))
  parses the server-time tag using `CultureInfo.InvariantCulture` with
  `DateTimeStyles.AssumeUniversal | AdjustToUniversal`. Matches the IRCv3 server-time grammar
  `YYYY-MM-DDThh:mm:ss.sssZ` exactly, and the new
  `NEW001_IrcEventTimestamp_WhenCurrentCultureIsNonGregorian_ShouldUseServerTimestamp` test
  pins this against `ar-SA` (Hijri calendar) — a real regression if anyone ever introduces
  `CurrentCulture` parsing.
- `IsupportReceivedEvent`
  ([src/IRCDotNet.Core/Events/IrcEvents.cs](src/IRCDotNet.Core/Events/IrcEvents.cs#L540-L602))
  copies all parser state into immutable fields at construction time, isolating handlers from
  later `IsupportParser` mutations on subsequent 005 lines. `Features` is wrapped in
  `ReadOnlyDictionary` over a fresh `Dictionary<string, string?>(parser.Features,
  StringComparer.OrdinalIgnoreCase)`, and `SupportedCapabilities` snapshots via `.ToArray()`.
- `IsChannelTyping` / `IsChannelMessage` / `IsChannelAction` now correctly recognize the full
  default `CHANTYPES=#&+!` per ISUPPORT
  ([src/IRCDotNet.Core/Events/IrcEvents.cs](src/IRCDotNet.Core/Events/IrcEvents.cs#L1232-L1234)
  and the parallel changes in PRIVMSG / CTCP). Test coverage in
  `Events/TypingIndicatorTests.cs` is theory-driven over `#`/`&`/`+`/`!`.
- `EXTENDED-MONITOR` capability constant added to
  `src/IRCDotNet.Core/Protocol/IrcCapabilities.cs` and included in the default-requested set in
  `src/IRCDotNet.Core/Configuration/IrcClientOptions.cs`. `IIrcClient.MonitorNickAsync` XML
  documentation correctly states "MONITOR does not infer nickname changes" — matches the IRCv3
  MONITOR spec rationale ("allowing users to avoid nick-change stalking").
- Keepalive starvation fix:
  [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L491-L548) introduces a
  private `SendRawCoreAsync(message, applyRateLimit)` overload, with `HandlePingAsync`
  ([src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L1281)) calling it with
  `applyRateLimit:false`. Pinned by
  `ProcessMessageAsync_WhenServerPingArrivesWhileSendBucketExhausted_ShouldSendPongWithoutRateLimitDelay`
  and `SendPing_WhenSendBucketExhausted_ShouldSendKeepalivePingWithoutRateLimitDelay`. Both tests
  use a near-empty bucket (`refillRate: 0.001, bucketSize: 1, initialTokens: 0`) — a saturated
  user-message bucket no longer starves liveness traffic.
- `_sendLock = new(1, 1)` reduced from the prior `(5, 5)` value
  ([src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L26)) — IRC requires
  in-order writes per connection; the prior multi-permit semaphore could interleave bytes.
  `SendMessageAsync_WhenConcurrentCallsTargetSameClient_ShouldSerializeTransportWrites` pins
  the new contract.
- ERR_NICKNAMEINUSE empty-AlternativeNicks fallback uses millisecond-precision suffix
  (4 zero-padded digits, `% 10000`) — pinned by
  `ProcessMessageAsync_WhenNicknameInUseReceivedDuringRegistrationWithNoAlternativeNicks_ShouldSendTimestampNickRetry`,
  fixing the prior unix-second collision when a 433 retry arrived within the same second.
- SemaphoreSlim disposal contract: the
  [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L599-L609)
  / [lines 698-705](src/IRCDotNet.Core/IrcClient.cs#L698-L705) `try { _sendLock.WaitAsync... }
  catch (ObjectDisposedException)` blocks plus the matching `try { _sendLock.Release(); }
  catch (ObjectDisposedException) { }` envelope translate the VPN-mid-flight teardown ODE into a
  clean `InvalidOperationException("Not connected")`. Pinned by `SendDisposalRaceTests`.
- NAMES convergence (`PendingNamesState`) is mutation-aware: the test
  `ProcessMessageAsync_WhenNamesSnapshotInterleavesMembershipChanges_ShouldApplyLiveDeltasAfterEndOfNames`
  asserts the right end-state after JOIN/QUIT/NICK arrive between 353 and 366. Prefix
  preservation verified by
  `ProcessMessageAsync_WhenLaterNamesSnapshotOmitsUsers_ShouldReplaceMembershipAndPreservePrefixes`.
- Event-ordering FIFO contract:
  `RaiseEventAsync_WhenEarlierEventHandlerIsBlocked_ShouldNotLetLaterEventsOvertake`
  pins the `_pendingEventDispatches` ConcurrentQueue + `_isProcessingEventQueue` Interlocked-flag
  worker pattern at [src/IRCDotNet.Core/IrcClient.cs](src/IRCDotNet.Core/IrcClient.cs#L2602-L2645).
  Combined with `DisposeAsync_WhenEventsAreAlreadyQueued_ShouldDrainQueueAndRaiseDisconnected`,
  the queue drain on dispose is verified.
- CAP DEL now re-raises a `CapabilitiesNegotiatedEvent` snapshot that excludes the deleted
  capability — pinned by
  `ProcessMessageAsync_WhenCapabilityDeleted_ShouldRaiseCapabilitySnapshotWithoutDeletedCapability`.
- `IsupportParser.GetServerCaseMapping` defaults to `Rfc1459` before any 005 arrives, and
  unknown CASEMAPPING values fall back to `Rfc1459` rather than `Ascii` — pinned by
  `CaseMapping_UnknownType_ReturnsRfc1459Fallback` and
  `GetServerCaseMapping_BeforeIsupport_ReturnsDefaultRfc1459`.

## Test Gaps

1. No test for `MonitorNickAsync("Bob")` followed by `:Bob!u@h NICK :bob` — `_monitoredNicks`
   case-flip would expose M1.
2. No test for `:server 734 me <limit> Bob :Monitor list is full.` — Med1 ERR_MONLISTFULL not
   reconciling local state.
3. No test for `:server 732 me :Bob,Carol` + `:server 733 me :End of MONITOR list` — Med1 list
   round-trip.
4. No test for self-PART or self-QUIT received between RPL_NAMREPLY (353) and
   RPL_ENDOFNAMES (366) for the same channel — Med4 phantom-channel resurrection.
5. No test for echo-message with a self-nick that contains RFC 1459 case-equivalence
   characters (`Foo[bar]` ↔ `foo{bar}`) under server CASEMAPPING=rfc1459 — Med3.
6. No test for two PRIVMSG / NICK pairs that vary case for the same logical user — Med2
   `_userInfo` double-tracking.
7. No test for RPL_MONOFFLINE → RPL_MONONLINE within 750 ms for the same nick — M2 race.
8. No test for `MOTD` interrupted by `ERR_NOMOTD` (422) without a preceding `RPL_MOTDSTART` —
   the new ERR_NOMOTD → ErrorReplyEvent emission is only verified at the unit level, not in a
   "client transitions out of MOTD-receive state" sense.
9. No test for MODE on a `+modeless` or `!safechan` target — Min2 prefix coverage.
