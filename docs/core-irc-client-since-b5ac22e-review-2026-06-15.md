# Review: IRCDotNet.Core changes since b5ac22e through HEAD (focus: IrcClient)

## Verdict
APPROVE WITH CHANGES — the recent fixes (PING/PONG limiter exemption, send-disposal race, reconnect preservation, NAMES convergence, explicit-only monitor rename) are correctly directed at real bugs and pin them with focused tests, but several adjacent races (dispose-during-reconnect, sync `Dispose()` CTS leak, non-atomic `_reconnectAttempts++`, partial `_pendingEventDispatches` drain semantics, and pending-NAMES leak on join-rejected numerics) are not yet covered, and one new fix (`_lastPongReceived` torn write) sits next to long-standing case-sensitive identifier comparisons that the new code inherits. None of the findings are connection-breaking on the happy path; the major ones are reliability and contract-clarity issues to fix before the next NuGet bump.

## Scope Read
- [src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs) (full, 3576 lines)
- [src/IRCDotNet.Core/Events/IrcEvents.cs](../src/IRCDotNet.Core/Events/IrcEvents.cs) (full diff vs `b5ac22e`)
- [src/IRCDotNet.Core/Configuration/IrcClientOptions.cs](../src/IRCDotNet.Core/Configuration/IrcClientOptions.cs) (clone semantics, AlternativeNicks)
- [src/IRCDotNet.Core/Protocol/IrcRateLimiter.cs](../src/IRCDotNet.Core/Protocol/IrcRateLimiter.cs) lines 75-170 (`WaitForAllowedAsync`, `Cleanup`)
- [src/IRCDotNet.Core/Protocol/IsupportParser.cs](../src/IRCDotNet.Core/Protocol/IsupportParser.cs) public surface only
- [tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs) (full, 959 lines)
- [tests/IRCDotNetCore.Tests/Threading/SendDisposalRaceTests.cs](../tests/IRCDotNetCore.Tests/Threading/SendDisposalRaceTests.cs) (full)
- [tests/IRCDotNetCore.Tests/IrcClientMonitorCorrelationTests.cs](../tests/IRCDotNetCore.Tests/IrcClientMonitorCorrelationTests.cs) (full)
- [tests/IRCDotNetCore.Tests/Resilience/ResilienceTests.cs](../tests/IRCDotNetCore.Tests/Resilience/ResilienceTests.cs) lines 85-160
- [tests/IRCDotNetCore.Tests/Events/TypingIndicatorTests.cs](../tests/IRCDotNetCore.Tests/Events/TypingIndicatorTests.cs) full
- [tests/IRCDotNetCore.ConcurrencyTests/PrivateMessageTests.cs](../tests/IRCDotNetCore.ConcurrencyTests/PrivateMessageTests.cs) test list (live integration)
- specs fetched: IRCv3 server-time extension (https://ircv3.net/specs/extensions/server-time)
- repo memory cross-checked against source for staleness; not relied on for any finding.

Commits in range (16):
`1ae937c` exempt PING/PONG from rate limiter; `89908c3` typing prefix `+`/`!`; `1c50128` send/dispose race fix; `b62b370` unused usings; `c3fe41d` 4-digit nick suffix; `78ded36` 433 retry without alternatives; `e848a0b` IRCv3 server-time tag; `1db9daea` CAP DEL re-raise + IsEcho on Notice/Action + ERR_NOMOTD + +/! channel prefixes; `78fd057` typed `IsupportReceived` event; `e7ac141` GetServerCaseMapping + bounded shutdown; `afad2ff` reconnect preservation + NAMES convergence; `879ec30` queued-events drain; `07435ca` per-connection write serialization + queued event dispatch + explicit-only monitor rename; `967afe8`/`b1217af`/`71f57eb` PM monitor correlation docs/tests/handle.

## Blockers
None. The send-disposal race, ping-timeout reconnect preservation, NAMES convergence, and PING/PONG limiter exemption all do what the commit messages claim and have focused tests. No data-leak or transport-mixing problems were found.

## Major

1. **`DisposeAsync` does not actually drain the event queue — it just lets it drain after returning.** [src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2573-L2594)
   - Observation: `DisposeAsync` calls `DisconnectInternalAsync` (which enqueues the final `Disconnected` event into `_pendingEventDispatches`), then in `finally` sets `_eventDispatchClosed = 1` and disposes `_sendLock`/`_connectLock`. There is no `await` for `_pendingEventDispatches` to empty or for the background `ProcessPendingEventDispatchesAsync` task to finish. Compare to the field comment at line 29 ("allowing final queued events to drain") and the commit message of `879ec30` ("Drain queued IRC events during async dispose"); both imply DisposeAsync waits, but the code does not.
   - Why it matters (Priority 2/3 — thread safety / ordering, contract clarity): the test at [tests/IRCDotNetCore.Tests/IrcClientTests.cs#L505](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L505) `await disposeTask.WaitAsync(...)` and *then separately* awaits `secondHandlerCompleted` — i.e. the test passes even when the second handler runs after dispose has returned, so the test does not pin a "drain before return" contract; it only pins eventual consistency. Callers reading the commit message will think the queue is flushed by the time `DisposeAsync` returns; a downstream cleanup path that disposes resources the handler relies on can fault.
   - Suggested change: either (a) await the in-flight `ProcessPendingEventDispatchesAsync` task to completion inside DisposeAsync (track its `Task` and `await` it after the queue drains), or (b) update the field comment and the commit's described contract to "queued handlers continue to drain on a background task after DisposeAsync returns". Pick one and pin a test to it.

2. **Auto-reconnect outlives `DisposeAsync` and can resurrect a "disposed" client.** [src/IRCDotNet.Core/IrcClient.cs#L2410-L2444](../src/IRCDotNet.Core/IrcClient.cs#L2410-L2444) (AttemptReconnectAsync), L264-L283 (DisconnectInternalAsync), L2573-L2594 (DisposeAsync)
   - Observation: `HandleUnexpectedDisconnectAsync` (line 1119) launches `AttemptReconnectAsync` via `SafeFireAndForget` with no tracked task handle. The reconnect's first action is `await Task.Delay(delay)` (line 2422), then `await ConnectAsync()`. If the user calls `DisposeAsync` while AttemptReconnect is in `Task.Delay` or in `ConnectAsync`, DisposeAsync does not (a) acquire `_connectLock`, (b) cancel any reconnect-attempt token, or (c) wait for the fire-and-forget. The reconnect can then re-create `_transport`, set `_isConnected = true`, start a new read loop, and start a new ping timer on a logically-disposed client. `_sendLock` and `_connectLock` are already disposed (line 2589/2590), so the next user send hits the disposal-race translation, but the read loop and ping timer keep running until the new transport faults.
   - Why it matters (Priority 1/2 — data integrity / ordering): the user observes `Disconnected` from DisposeAsync, then later (silently, on the background reconnect path) the client reaches a half-alive state with disposed locks. Errors look like "Not connected" instead of "client was disposed". Worse, if the application captured event subscriptions on the disposed instance, late events from the resurrected read loop will miss them or arrive on a stale instance.
   - Suggested change: gate `AttemptReconnectAsync` on `_disposeRequested == 0` immediately after `Task.Delay` (and again before ConnectAsync) and exit silently if disposal has been requested. Either: (a) track the reconnect task and await it in DisposeAsync, or (b) introduce a dedicated cancellation source for reconnect that DisposeAsync cancels.

3. **Sync `Dispose()` leaks `_cancellationTokenSource` (and the queued event handlers).** [src/IRCDotNet.Core/IrcClient.cs#L2533-L2570](../src/IRCDotNet.Core/IrcClient.cs#L2533-L2570)
   - Observation: sync `Dispose()` calls `_cancellationTokenSource?.Cancel()` (line 2540) but never `Dispose()`s it. It does dispose `_pingTimer`, `_transport`, `_sendLock`, `_connectLock`, but the CTS is leaked until GC. It also does not drain or clear `_pendingEventDispatches` — closures captured in the queue keep the handler delegates (and any state they close over, including event args holding `IrcMessage`) alive until GC.
   - Why it matters (Priority 6 — diagnostics/leaks): not user-visible but a real `IDisposable` contract violation; analyzers like CA2213 should flag the CTS leak. Compare with `DisposeAsync` which routes through `DisconnectInternalAsync` and disposes the CTS at line 281.
   - Suggested change: in sync `Dispose()`, after `Cancel()`, call `_cancellationTokenSource?.Dispose()` inside a try/catch (already disposed if a prior async dispose ran). Optionally clear `_pendingEventDispatches` in sync dispose since handlers cannot run safely once `_sendLock` is disposed.

4. **`_reconnectAttempts++` is not atomic.** [src/IRCDotNet.Core/IrcClient.cs#L52](../src/IRCDotNet.Core/IrcClient.cs#L52), L2418
   - Observation: declared `volatile int _reconnectAttempts` and incremented with `_reconnectAttempts++` (a read-modify-write, NOT atomic; volatile only orders single reads/writes). Reset to `0` in `HandleWelcome` (line 2204) and read as a guard (line 2412). Two reconnect entries can race: the recursive failure-retry path at line 2438 can overlap a fresh `HandleUnexpectedDisconnectAsync`-driven reconnect from an in-flight read loop completion if a new transport was already created. Lost increments could cause the loop to exceed `MaxReconnectAttempts` or to under-count.
   - Why it matters (Priority 2 — concurrency correctness): the tests at lines 590 and 605 of `IrcClientTests.cs` only assert `_reconnectAttempts == 1` for a single attempt; no test exercises the recursive retry path under concurrent disconnect.
   - Suggested change: replace with `Interlocked.Increment(ref _reconnectAttempts)` and `Interlocked.Exchange(ref _reconnectAttempts, 0)`; drop `volatile`.

5. **`_lastPongReceived` is written without `_stateLock` while read under it.** [src/IRCDotNet.Core/IrcClient.cs#L1471-L1474](../src/IRCDotNet.Core/IrcClient.cs#L1471-L1474) (HandlePong), L2362-L2375 (SendPing)
   - Observation: `HandlePong` does `_lastPongReceived = DateTimeOffset.UtcNow;` outside any lock. `SendPing` reads it inside `lock(_stateLock)` to compute the ping-timeout. `DateTimeOffset` is 16 bytes (Int64 ticks + Int16 offset minutes plus padding); writes are not atomic on any platform.
   - Why it matters (Priority 2 — torn read): theoretically a watchdog could see new ticks paired with a stale offset. In practice IRC servers always run UTC=0 and the offset never changes, so the torn-read window cannot mis-fire a timeout. Still it is a pre-condition violation that will become real if anyone changes the field type or adds a non-UTC offset path.
   - Suggested change: store as `long _lastPongReceivedUtcTicks` and use `Interlocked.Exchange` / `Interlocked.Read`. Or take `_stateLock` for the write in HandlePong.

6. **`AlternativeNicks.IndexOf` and the `_currentNick == oldNick`/`nick == _currentNick` comparisons use Ordinal equality, not the server CASEMAPPING.** [src/IRCDotNet.Core/IrcClient.cs#L1736](../src/IRCDotNet.Core/IrcClient.cs#L1736), L1770, L1918, L2917, L2117
   - Observation: HandleJoin / HandlePart / HandleKick gate "is this me?" with `nick == _currentNick` (case-sensitive). HandleNicknameInUse does `_options.AlternativeNicks.IndexOf(_currentNick)` (ordinal). RFC 2812 §1.3 mandates case-insensitive identifier comparison, with the exact mapping coming from ISUPPORT CASEMAPPING (rfc1459, rfc1459-strict, ascii). The new `GetServerCaseMapping()` accessor is in place; `NicknamesEqual(...)` is in place; neither is used here.
   - Why it matters (Priority 1 / 9 — data integrity, protocol conformance): if a server normalizes a nickname's case differently than what the client sent (e.g. capability-aware normalization, or services rewriting case), `_channels[*]` membership and the local "self" detection desync from server truth. Channels containing the user as `Bob` while server thinks they are `bob` quietly never get the local-self path, so `_channels[channel] = new ConcurrentHashSet<string>()` is never created for that channel, and the join is "lost" in local state. NOT introduced by this commit range but the new `ApplyNickChange` (line 2914-2960) is the first place to extract the comparison and would be the natural place to fix.
   - Suggested change: introduce a private helper `IsSelf(string nick) => IrcCaseMapping.Equals(nick, _currentNick, _isupportParser.CaseMapping)` and use it throughout. Same for AlternativeNicks index. RFC reference: https://www.rfc-editor.org/rfc/rfc2812#section-1.3.

7. **ERR_NOCHANMODES (477) / ERR_TOOMANYCHANNELS (405) clean up `_channels` but leak `_pendingNamesUsers`.** [src/IRCDotNet.Core/IrcClient.cs#L3221-L3232](../src/IRCDotNet.Core/IrcClient.cs#L3221-L3232)
   - Observation: when these numerics fire, the handler removes `_channels[errorMessage]` but does not remove `_pendingNamesUsers[errorMessage]`. If an authoritative NAMES burst ever lands before the rejection (e.g. a server that races RPL_NAMREPLY against a delayed services check on +R/+i channels), the pending state stays in the dictionary across reconnects-without-disposal. `DisconnectInternalAsync` does clear it on full disconnect, but a soft state-reset path (e.g. SASL retry without disconnect) would not.
   - Why it matters (Priority 1 — data integrity / leak): bounded by the number of distinct join-rejected channel names per session. Not catastrophic, but the handler is the only place outside disconnect / EndOfNames that removes from `_channels`, and it should match the cleanup that PART/KICK do at lines 1789 and 1928.
   - Suggested change: add `_pendingNamesUsers.TryRemove(errorMessage, out _);` next to the `_channels.TryRemove` call.

8. **`HandleEndOfNames` discards prefix data when no pending state exists.** [src/IRCDotNet.Core/IrcClient.cs#L2243-L2254](../src/IRCDotNet.Core/IrcClient.cs#L2243-L2254)
   - Observation: when `_pendingNamesUsers.TryRemove` returns false (no NAMES burst preceded the EOFNAMES — e.g. a `/NAMES #channel` query for a channel we are already in), the fallback path constructs `ChannelUsersEvent` from `_channels[channel].Select(nick => new ChannelUser { Nick = nick })` — i.e. nicks without prefixes. The previous join's prefix info has already been overwritten by the NAMES replacement set at line 2238 because the replacement set is `ConcurrentHashSet<string>` (nicks only).
   - Why it matters (Priority 5/9): consumers wiring up the `ChannelUsersReceived` event for membership UI lose the @/+ prefix indicator on a `/NAMES` re-query. The pinning test at line 632 (`ProcessMessageAsync_WhenLaterNamesSnapshotOmitsUsers_ShouldReplaceMembershipAndPreservePrefixes`) only covers the case where NAMES *did* arrive before EOFNAMES, so the data path that loses prefixes is not under test.
   - Suggested change: either keep `ChannelUser` (with prefixes) as the canonical in-memory representation alongside the nick set, or document that `ChannelUsersEvent` for a re-queried `/NAMES` does not carry prefixes when there was no NAMES burst.

## Medium

9. **Reconnect rejoin list is `volatile List<string>`, not snapshotted under a lock.** [src/IRCDotNet.Core/IrcClient.cs#L53](../src/IRCDotNet.Core/IrcClient.cs#L53), L1115-L1124, L2426-L2438
   - Observation: `_channelsToRejoin` is reassigned in `HandleUnexpectedDisconnectAsync` (`_channelsToRejoin = _channels.Keys.ToList();`) and read+swapped in `AttemptReconnectAsync` (`var channelsToRejoin = _channelsToRejoin; _channelsToRejoin = new();`). The swap is two writes; a concurrent `HandleUnexpectedDisconnectAsync` from a second disconnect path can stomp the new empty list with a fresh keys snapshot, racing with the foreach iteration in AttemptReconnect. `volatile` only gives reference visibility, not atomic swap.
   - Why it matters (Priority 2 — ordering): bounded by the practical absence of two concurrent disconnects, but the existing reconnect-during-dispose race (Major #2) makes this concrete.
   - Suggested change: use `Interlocked.Exchange(ref _channelsToRejoin, new List<string>())` to take an exclusive reference, then enumerate. Or hold `_stateLock` around the swap.

10. **`time` tag parsing accepts off-spec formats and silently falls back on leap seconds.** [src/IRCDotNet.Core/Events/IrcEvents.cs#L31-L42](../src/IRCDotNet.Core/Events/IrcEvents.cs#L31-L42)
    - Observation: `IrcEvent.GetTimestamp` uses `DateTimeOffset.TryParse(timeValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, ...)`. The IRCv3 server-time spec (https://ircv3.net/specs/extensions/server-time) requires exactly `YYYY-MM-DDThh:mm:ss.sssZ` and explicitly shows `2012-06-30T23:59:60.419Z` (leap second) as a legal value. `DateTimeOffset` cannot represent `:60`; `TryParse` returns false and the code silently falls back to `DateTimeOffset.UtcNow`.
    - Why it matters (Priority 9 — protocol conformance, low impact): leap-second timestamps will be misattributed to local receipt time. Off-spec time formats (e.g. missing milliseconds) are accepted, which is forgiving but hides server bugs.
    - Suggested change: prefer `TryParseExact` with `"yyyy-MM-ddTHH:mm:ss.fffZ"` and a culture-invariant style. For `:60` either clamp to `:59.999` and log, or accept `TryParse` as a fallback after exact-match fails. Add a regression test for both paths.

11. **`PendingMonitoredOffline` correlation key uses `DateTimeOffset` equality.** [src/IRCDotNet.Core/IrcClient.cs#L2967-L2987](../src/IRCDotNet.Core/IrcClient.cs#L2967-L2987)
    - Observation: `FinalizePendingMonitoredOfflineAsync` matches by `current.Timestamp != pending.Timestamp`. `DateTimeOffset.UtcNow` resolution is OS-dependent (≈100ns on Windows, ≈10-15ms on Linux). Two MONITOR-offline notices for the same nick within the same OS clock tick would carry equal timestamps; the second's pending entry would be erroneously confirmed by the first finalize.
    - Why it matters (Priority 1/2 — data integrity, low practical risk): a server is not expected to send back-to-back offlines for the same nick within 10ms.
    - Suggested change: include a monotonic sequence (e.g. `Interlocked.Increment` on a per-client counter) in `PendingMonitoredOffline` and key the correlation off that, not the timestamp.

12. **`AttemptReconnectAsync` recursion does not back off on transient failures distinguishable from permanent ones.** [src/IRCDotNet.Core/IrcClient.cs#L2410-L2444](../src/IRCDotNet.Core/IrcClient.cs#L2410-L2444)
    - Observation: on `ConnectAsync` failure the recursive retry runs after a fixed `Task.Delay(1000)` regardless of the error class. The outer delay-formula `Math.Min(_options.ReconnectDelayMs * _reconnectAttempts, _options.MaxReconnectDelayMs)` IS exponential-ish but only on the outer attempt counter; the inner recursive retry is a flat 1s.
    - Why it matters (Priority 5 — perf/scalability): under a permanently-failing network the client paces between (a) the outer formula and (b) the inner 1s flat. With `MaxReconnectAttempts <= 0` (unlimited) this is bounded by clock time, not stack depth, so it is not a stack-overflow risk; but it does pin a 1Hz reconnect attempt indefinitely, which is hostile to firewalled networks.
    - Suggested change: replace the recursive 1s retry with a single outer-loop continuation that respects `_options.ReconnectDelayMs * _reconnectAttempts` consistently.

13. **`SafeFireAndForget` background tasks are not tracked anywhere.** [src/IRCDotNet.Core/IrcClient.cs#L2674-L2678](../src/IRCDotNet.Core/IrcClient.cs#L2674-L2678) and many call sites
    - Observation: the helper logs unobserved exceptions but does not track task handles. There is no way for `DisposeAsync` to wait for `RefreshMonitoredNickAsync`, `FinalizePendingMonitoredOfflineAsync`, `AutoRejoinOnKick`, `NickServIdentify`, `SaslSuccessCapEnd`/etc., `GenericMessageEvent`, `CtcpVersionReply`/etc., or `AttemptReconnectAsync` to complete.
    - Why it matters (Priority 2/4): Major #2 is the most visible consequence; the rest are mostly benign because they end with a "Not connected" exit, but the contract that DisposeAsync gives back a fully-quiesced client is not actually delivered.
    - Suggested change: extend `SafeFireAndForget` to register tasks in a `ConcurrentDictionary<Guid, Task>` and have DisposeAsync `await Task.WhenAll(...).WaitAsync(timeout)` against the tracked set after `_eventDispatchClosed = 1`. Keep the per-task name for diagnostics.

14. **`SaslMechs` (RPL_SASLMECHS, 908) doesn't reset `_saslInProgress`.** [src/IRCDotNet.Core/IrcClient.cs#L1707-L1713](../src/IRCDotNet.Core/IrcClient.cs#L1707-L1713)
    - Observation: 908 fires before 904 (ERR_SASLFAIL) per Solanum/Atheme/Charybdis, and the comment says "server will also send 904... which handles CAP END". That is true for CAP END but the handler does not clear `_saslInProgress`; only HandleSaslFailure does. If a server sends a degenerate sequence (908 without a follow-up 904), `_saslInProgress` stays `true` indefinitely and CAP END is never sent — registration stalls.
    - Why it matters (Priority 9): the spec (https://ircv3.net/specs/extensions/sasl-3.1) does require 904 to follow 908 in practice, so this is a defensive concern for off-spec servers.
    - Suggested change: keep the comment but also fall-through to schedule a CAP END if no 904 lands within a short timer (or just clear `_saslInProgress` here and let CAP END drive on the next failure path).

## Minor

15. **`HandleNicknameInUseAsync` constructs `Random()` on each call.** [src/IRCDotNet.Core/IrcClient.cs#L2289-L2304](../src/IRCDotNet.Core/IrcClient.cs#L2289-L2304) (and in `HandleNicknameCollisionAsync` at L2316/L2330)
    - Observation: `var random = new Random().Next(10, 99);`. .NET 8's `Random.Shared` is the recommended thread-safe singleton.
    - Why it matters (Priority 5): two back-to-back collisions in the same millisecond produce identical seed values and identical fallback nicks. Compounds the timestamp-collision concern that drove `c3fe41d`.
    - Suggested change: use `Random.Shared.Next(10, 99)`.

16. **`_nickServIdentified` test-then-set is not atomic.** [src/IRCDotNet.Core/IrcClient.cs#L1850-L1862](../src/IRCDotNet.Core/IrcClient.cs#L1850-L1862)
    - Observation: serialized in practice by the read loop, but the field is `volatile` which suggests cross-thread coordination that is not actually present.
    - Suggested change: either drop `volatile` (only the read loop writes) or use `Interlocked.CompareExchange` and update the comment.

17. **`IsupportReceivedEvent` snapshot copies `parser.Features` via `new Dictionary(parser.Features)` without explicit synchronization.** [src/IRCDotNet.Core/Events/IrcEvents.cs#L568-L576](../src/IRCDotNet.Core/Events/IrcEvents.cs#L568-L576)
    - Observation: safe today because the read loop both populates the parser and constructs the event on the same thread; the snapshot is captured before `RaiseEventAsync` enqueues the dispatch. Document the invariant in an XML doc-comment.

18. **`SendQuitForDisconnectAsync` swallows non-timeout faults silently.** [src/IRCDotNet.Core/IrcClient.cs#L1812-L1826](../src/IRCDotNet.Core/IrcClient.cs#L1812-L1826)
    - Observation: only `TimeoutException` is observed; if `SendRawWithCancellationAsync` throws `InvalidOperationException("Not connected")` or `IOException`, those propagate out of the helper and through `DisconnectAsync`'s outer try/catch (which does catch them). Acceptable, but the ObserveFault helper is not used for the IOException path.

19. **`HandleMode` channel detection is hardcoded to `#`/`&`.** [src/IRCDotNet.Core/IrcClient.cs#L3274](../src/IRCDotNet.Core/IrcClient.cs#L3274)
    - Observation: uses `target.StartsWith("#") || target.StartsWith("&")` for channel-vs-user mode dispatch. Inconsistent with the new `+`/`!` recognition added to `PrivateMessageEvent`/`CtcpActionEvent`/`TypingIndicatorEvent`.
    - Suggested change: use `_isupportParser.ChannelTypes.Contains(target[0])` (with bounds check) or extract a private `IsChannelTarget(string)` helper.

20. **`FinalizePendingMonitoredOfflineAsync` does not honor `_cancellationTokenSource.Token`.** [src/IRCDotNet.Core/IrcClient.cs#L2967-L2982](../src/IRCDotNet.Core/IrcClient.cs#L2967-L2982)
    - Observation: `await Task.Delay(MonitorOfflineCorrelationWindow).ConfigureAwait(false)` is uncancellable. After `DisconnectInternalAsync` clears `_pendingMonitoredOfflines`, the delay still completes; the timestamp-mismatch check then bails. So no event is raised, but ~750ms of background work is held alive per outstanding offline.

## Test Gaps

T1. **No test for DisposeAsync during AttemptReconnect** — Major #2 is unpinned. Add: stub a connect that hangs; trigger ping-timeout; call DisposeAsync; assert `_isConnected == false`, no `Connected` event after dispose, no read loop continuation, and the `_disposeRequested` short-circuit is hit.

T2. **No test for sync `Dispose()` with queued events** — Major #1 + #3. Add: enqueue a slow handler via `RaiseEventAsync`; call sync `Dispose()`; assert the handler is *not* invoked (sync Dispose contract) OR is invoked synchronously before Dispose returns. Pick the contract. Today it neither aborts nor drains.

T3. **No test pins the "drain before return" semantics of DisposeAsync** — `DisposeAsync_WhenEventsAreAlreadyQueued_ShouldDrainQueueAndRaiseDisconnected` ([tests/IRCDotNetCore.Tests/IrcClientTests.cs#L505-L552](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L505-L552)) sequentially awaits dispose then handlers; it would also pass if handlers ran *after* dispose. Tighten: assert `secondHandlerCompleted.Task.IsCompleted` is `true` *before* `disposeTask.WaitAsync` returns.

T4. **No test for `_pendingNamesUsers` cleanup on ERR_NOCHANMODES / ERR_TOOMANYCHANNELS** — Major #7.

T5. **No test for case-mismatched self detection in JOIN/PART/KICK** — Major #6. Feed `:Bob!u@h JOIN #room` while `_currentNick == "bob"` and assert the local-self path runs.

T6. **`SendDisposalRaceTests` covers `_sendLock` only**, not `_transport` being nulled or disposed mid-send. Add a counterpart that disposes `_transport` (not the lock) after the in-flight send passes the `_isConnected` check; assert `InvalidOperationException("Not connected")`, no leaked NRE / ObjectDisposedException through `_transport.WriteLineAsync`.

T7. **No test for `_reconnectAttempts` atomicity under concurrent failure paths** — Major #4.

T8. **No test for IRCv3 server-time leap-second `:60` parsing** — Medium #10. Feed `@time=2012-06-30T23:59:60.419Z PRIVMSG ...` and assert the timestamp falls back to `UtcNow` (and document this) or accept `:59.999`.

T9. **No test for monitor-offline dedup under back-to-back duplicate offlines** — Medium #11.

T10. **Documented known-flake `ConcurrentConnectAndDisconnect_ShouldHandleGracefully` is not separated from the suite or marked `[Fact(Skip=...)]`** — it sits in the test suite at [tests/IRCDotNetCore.Tests/Resilience/ResilienceTests.cs#L96](../tests/IRCDotNetCore.Tests/Resilience/ResilienceTests.cs#L96) and per copilot-instructions.md is allowed to fail. That is fine for the build pipeline but it papers over a real concurrent-connect-vs-disconnect race that the new sync-Dispose work touched (Major #3 + Major #2). The test should either be skipped explicitly with a memo link or fixed.

T11. **Pre-send `OnPreSendMessage` interception is tested for IsCancelled effect but not for thread-safety of the handler closure** — `OnPreSendMessage(preSendEvent)` runs on the caller's send-thread inline, before the lock acquisition. If a handler blocks (`Thread.Sleep` / synchronous work), it pins the send path. Add a regression that asserts the handler runs on the caller's thread and is invoked at most once per send.

T12. **`IsupportReceivedEvent` snapshot stability is not pinned** — Minor #17. Process a 005, capture the event, process another 005 that mutates the parser, assert the captured event's snapshot is unchanged.

## Confirmed Good

- **PING/PONG limiter exemption** ([src/IRCDotNet.Core/IrcClient.cs#L487-L532](../src/IRCDotNet.Core/IrcClient.cs#L487-L532), L1466, L2382) is correctly gated on `applyRateLimit && EnableRateLimit && _isRegistered`. Tests `ProcessMessageAsync_WhenServerPingArrivesWhileSendBucketExhausted_ShouldSendPongWithoutRateLimitDelay` (line 386) and `SendPing_WhenSendBucketExhausted_ShouldSendKeepalivePingWithoutRateLimitDelay` (line 423) pin the exact starvation path with a near-zero-refill bucket. Solid.
- **`_sendLock` ObjectDisposedException translation** in both `SendRawCoreAsync` ([line 622-639](../src/IRCDotNet.Core/IrcClient.cs#L622-L639)) and `SendRawWithCancellationAsync` ([line 695-707](../src/IRCDotNet.Core/IrcClient.cs#L695-L707)). The `finally { try { _sendLock.Release(); } catch (ODE) { } }` pattern at lines 660 and 728 prevents the leak both pre- and post-write. Tests in `SendDisposalRaceTests` deterministically simulate the race using reflection. Solid.
- **NAMES convergence with interleaved JOIN/PART/QUIT/NICK** ([line 2207-2271](../src/IRCDotNet.Core/IrcClient.cs#L2207-L2271)): the `PendingNamesState { Snapshot, Mutations }` design is correct — mutations are queued by handlers between RPL_NAMREPLY and RPL_ENDOFNAMES, then replayed on top of the snapshot. The `TryAdd` in `ApplyPendingNamesMutation` correctly preserves prefix info from the authoritative NAMES burst when a JOIN-add for the same nick races in. Pinned by `ProcessMessageAsync_WhenNamesSnapshotInterleavesMembershipChanges_ShouldApplyLiveDeltasAfterEndOfNames` (line 666). Solid.
- **Per-connection write serialization via `_sendLock`** with rate limiting *outside* the lock prevents starvation. `SendMessageAsync_WhenConcurrentCallsTargetSameClient_ShouldSerializeTransportWrites` (line 484) pins `MaxConcurrentWrites == 1`. Solid.
- **Ping-timeout reconnect path preserves `_channelsToRejoin`** by routing through `HandleUnexpectedDisconnectAsync` (line 1115). `SendPing_WhenPongTimesOut_ShouldScheduleReconnectAttemptAndPreserveChannelsToRejoin` (line 597) pins it. Solid (subject to the volatile-list race in Medium #9).
- **433 NICK retry without alternatives** falls through to a 4-digit millisecond suffix (line 2117-2123) instead of stalling. The regex pin at line 326 enforces 4-digit shape, blocking re-emission of the identical NICK within a 1-second server retry window. Solid.
- **Explicit-only monitor rename**: `ApplyNickChange` is the single funnel for both bare `NICK` and any signal that drives a rename, and `MonitorOfflineFollowedByPrivateMessageWithMatchingUserHost_ShouldNotGuessNickChanged` ([tests/IRCDotNetCore.Tests/IrcClientMonitorCorrelationTests.cs#L34](../tests/IRCDotNetCore.Tests/IrcClientMonitorCorrelationTests.cs#L34)) and the multi-candidate test at line 60 pin that PM-only correlation is now best-effort and not a guarantee. Solid.
- **CAP DEL re-raises CapabilitiesNegotiated** with a fresh snapshot ([line 1542-1567](../src/IRCDotNet.Core/IrcClient.cs#L1542-L1567)), pinned by `ProcessMessageAsync_WhenCapabilityDeleted_ShouldRaiseCapabilitySnapshotWithoutDeletedCapability` ([tests/IRCDotNetCore.Tests/IrcClientTests.cs#L370](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L370)). Solid.
- **Bounded shutdown**: `DisconnectAsync` is broken into `SendQuitForDisconnectAsync` / `DisconnectTransportForShutdownAsync` / `WaitForReadLoopShutdownAsync`, each bounded by `_options.SendTimeoutCancelledMs` via `Task.WaitAsync` + `ObserveFault` ([line 1812-1858](../src/IRCDotNet.Core/IrcClient.cs#L1812-L1858)). Pinned by `DisconnectAsync_WhenQuitWriteDoesNotComplete_ShouldStillFinishCleanup` ([tests/IRCDotNetCore.Tests/IrcClientTests.cs#L575](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L575)). Solid.
- **IRCv3 server-time tag** is now applied at `IrcEvent` construction so all events carry the server's timestamp instead of local receipt time, with culture-invariant parsing ([src/IRCDotNet.Core/Events/IrcEvents.cs#L31-L42](../src/IRCDotNet.Core/Events/IrcEvents.cs#L31-L42)). Subject to the leap-second concern in Medium #10. Solid for the common case.

## Unverified / Needs Follow-up
- The `ResilienceTests.ConcurrentConnectAndDisconnect_ShouldHandleGracefully` flakiness (per `.github/copilot-instructions.md`) was not reproduced as part of this review — it is on the documented "known flakes" list. Whether the new sync-Dispose path makes the underlying race better or worse is not separately demonstrated. Recommend re-running it under the changed code at least 100x in CI before the next NuGet bump and either fixing or skipping with a justification.
- The two other documented pre-existing failures (`CacheInvalidationTests`, `ThreadSafetyAdvancedTests.StateTransitions_UnderConcurrentAccess_ShouldRemainConsistent`) were not read during this review; the second one in particular sits next to the concerns in Major #4 and Major #6 and may share a root cause.
- The live-server `PrivateMessageTests` in `IRCDotNetCore.ConcurrencyTests` are integration tests gated on a local IRCd; they were not exercised. The added `ThreeUsers_HumanPacedEdgeCaseChat_ShouldDeliverEveryMessageInOrder` is high-value coverage but its outcome could not be verified without a running server.
