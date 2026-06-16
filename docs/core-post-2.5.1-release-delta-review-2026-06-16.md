# IRCDotNet.Core post-2.5.1 release delta review

Scope: changes from `b5ac22e661888a1e36a8527efed5de335c0ef6a3` (2.5.1) through current `HEAD` (`1ae937c` at review time). Focus areas were IRC protocol compliance, state/data leaks, memory/resource leaks, async lifecycle, monitor and NAMES state, and whether the new tests actually pin the claimed behavior.

Verdict: do not publish the next package until the P1 findings are addressed. The release delta contains several real fixes that are correctly pointed at production issues, especially keepalive rate-limit bypass, send serialization, NICK retry, CAP DEL snapshots, and the removal of heuristic PM nick-change guessing. The remaining risk is concentrated in identity/case mapping, MONITOR lifecycle, and disposal/reconnect quiescence.

Two untracked review drafts already existed under `docs/` before this document was written. This file is the consolidated verified review and intentionally leaves those drafts untouched.

## P1 Findings

### 1. `DisposeAsync` does not drain queued events before returning

Evidence: `DisconnectInternalAsync` enqueues `Disconnected` and `EnhancedDisconnected` via `RaiseEventAsync`, then `DisposeAsync` sets `_eventDispatchClosed` and disposes `_sendLock` / `_connectLock` without awaiting the background event queue processor: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L468-L480), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2573-L2594), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2603-L2664). The queue processor task is fire-and-forget and not stored anywhere.

Impact: callers can observe `DisposeAsync` completion while previously queued user handlers are still running. That is a lifecycle contract bug: downstream cleanup may dispose resources that event handlers still expect, and final disconnect notifications can arrive after the caller has already treated the client as quiesced. The current test name says it drains, but the test awaits `DisposeAsync` and then separately awaits handler completion, so it only proves eventual dispatch: [../tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L505).

Suggested fix: track the event-queue processing task and await it from `DisposeAsync` after `DisconnectInternalAsync` enqueues final events. Add a test that asserts the second queued handler and the disconnect handler have completed before `DisposeAsync` returns.

### 2. Auto-reconnect can outlive disposal and retry against a disposed client

Evidence: unexpected disconnect schedules `AttemptReconnectAsync` with `SafeFireAndForget`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1065). `AttemptReconnectAsync` delays, then calls `ConnectAsync` with no disposal gate or reconnect cancellation token: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2320-L2364). `DisposeAsync` does not track or cancel that task before disposing `_connectLock`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2573-L2594). `ConnectAsync` also has no `_disposeRequested` check before waiting on `_connectLock`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L284-L333).

Impact: if the user disposes while a reconnect is delayed or in progress, the reconnect path can continue after disposal. In one interleaving it repeatedly logs/retries on `ObjectDisposedException` from the disposed connect semaphore; in another it can create a new transport/read loop before the dispose path finishes, leaving a half-alive disposed instance. This is a state leak and a resource leak risk.

Suggested fix: add a reconnect cancellation source that `Dispose` / `DisposeAsync` cancel, check `_disposeRequested` before and after the reconnect delay, and track the reconnect task so async dispose can await or cancel it. Add a test that triggers reconnect, calls `DisposeAsync` during the delay, and asserts no later `ConnectAsync` attempt or `Connected` event occurs.

### 3. MONITOR tracked nicks are case-sensitive, so explicit NICK refresh can be skipped

Evidence: `_monitoredNicks` is a `ConcurrentHashSet<string>` built over a default `ConcurrentDictionary<T, byte>` with no comparer: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L65), [../src/IRCDotNet.Core/Utilities/ConcurrentHashSet.cs](../src/IRCDotNet.Core/Utilities/ConcurrentHashSet.cs#L10-L18). `ApplyNickChange` gates refresh on `_monitoredNicks.Contains(oldNick)`, then removes/adds by exact string: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2903-L2952). The repo already has IRC-aware comparison helpers and comparers, but this path does not use them: [../src/IRCDotNet.Core/Protocol/IrcCaseMapping.cs](../src/IRCDotNet.Core/Protocol/IrcCaseMapping.cs#L139-L235).

Impact: IRC nick equality is case-mapped by the server. If the app monitors `Bob` and the server relays `:bob!u@h NICK :Robert` or any RFC1459-equivalent spelling, `Contains(oldNick)` can fail. The client then leaves stale local monitor state, does not send `MONITOR - old` / `MONITOR + new`, and silently stops tracking the renamed user. This directly weakens the new explicit-only monitor rename behavior.

Suggested fix: make `ConcurrentHashSet<string>` accept an `IEqualityComparer<string>` and use an IRC case-mapping comparer for monitor nicks. If CASEMAPPING changes after 005, rebuild all IRC-identity sets under the new comparer. Add tests using RFC1459 bracket equivalents, not just ASCII case differences.

### 4. `RPL_MONOFFLINE` finalization races with `RPL_MONONLINE`

Evidence: `HandleMonitorOffline` stores a pending offline entry and fire-and-forgets `FinalizePendingMonitoredOfflineAsync`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2888-L2899). `HandleMonitorOnline` cancels only by removing the dictionary entry: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2870-L2886). The finalizer does `TryGetValue`, then later `TryRemove`, removes `_userInfo`, and raises synthetic `UserQuit` without checking whether its own remove won: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2968-L2979).

Impact: an online reply can interleave after the finalizer's successful `TryGetValue` but before its `TryRemove`. The finalizer then still removes the fresh user info and raises `UserQuit` for a user the server just said is online. This is a data-integrity bug in monitor presence.

Suggested fix: cancellation must be coordinated with finalization. Use a per-pending cancellation token or an atomic remove-and-compare operation where finalization only proceeds if it removed the exact pending record. Add a unit test for `MONOFFLINE`, then `MONONLINE` inside the correlation window, including a deliberately blocked finalizer to force the race.

### 5. IRC identity comparison still bypasses server CASEMAPPING in state-critical paths

Evidence: local-self detection uses case-sensitive equality in JOIN, PART, KICK, and nick-change handling: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1736), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1770), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1918), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2917). Echo-message detection uses `StringComparison.OrdinalIgnoreCase`, not the server CASEMAPPING: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1823), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1857). Alternative nick fallback uses `IndexOf(_currentNick)` with the list default comparer: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2140), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2177). Channel user sets are also case-sensitive `ConcurrentHashSet<string>` instances: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1738), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2038).

Impact: on RFC1459 CASEMAPPING networks, `[]\^` and `{}|~` equivalence matters, and ASCII-only ignore-case is still insufficient. These paths can miss self-joins/parts/kicks, duplicate or fail to remove users from local channel sets, misclassify echo-message events, and leave stale `UserInfo` entries. That is both IRC spec non-compliance and a state leak.

Suggested fix: centralize identity comparisons through `IrcCaseMapping.Equals(..., _isupportParser.CaseMapping)` and use comparer-backed collections for all IRC nick/channel member sets. Add tests for ASCII case, RFC1459 bracket equivalents, and CASEMAPPING changes after 005.

## P2 Findings

### 6. MONITOR list numerics 732, 733, and 734 are defined but not handled

Evidence: constants exist for `RPL_MONLIST`, `RPL_ENDOFMONLIST`, and `ERR_MONLISTFULL`: [../src/IRCDotNet.Core/Protocol/IrcNumericReplies.cs](../src/IRCDotNet.Core/Protocol/IrcNumericReplies.cs#L380-L384). Numeric dispatch handles only `RPL_MONONLINE` and `RPL_MONOFFLINE`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1250-L1254). `MonitorNickAsync` adds the nick locally immediately after sending `MONITOR +`, before any server confirmation: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2479-L2493).

Impact: if the server rejects a monitor addition with `ERR_MONLISTFULL`, local `_monitoredNicks` still claims the nick is tracked. Later rename refreshes and synthetic offline handling are based on false state. There is also no typed surface for a server-supplied monitor list snapshot.

Suggested fix: dispatch 732/733/734. At minimum, route 734 through `ErrorReplyReceived` and remove rejected targets from `_monitoredNicks`; preferably expose typed monitor-list events or a request/response API.

### 7. Sync `Dispose()` leaks the cancellation token source and does not quiesce background work

Evidence: sync `Dispose()` cancels `_cancellationTokenSource` but never disposes it: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2527-L2570). It also closes event dispatch for future events but does not clear already queued dispatch closures or wait for any `SafeFireAndForget` work that was already running.

Impact: repeated sync create/dispose cycles can retain CTS resources until GC. Already queued event closures can retain event args, handlers, and captured consumer state longer than the caller expects. Fire-and-forget work such as monitor offline finalizers, CTCP replies, auto-rejoin, NickServ identify, and reconnect retries can continue after `Dispose()` returns.

Suggested fix: dispose the CTS in sync dispose, clear or explicitly drain queued event closures according to the chosen sync-dispose contract, and use the same tracked-background-work mechanism recommended for async dispose.

### 8. Reconnect counters and rejoin list updates are not atomic

Evidence: `_reconnectAttempts` is `volatile int`, incremented with `_reconnectAttempts++`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L52), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2328). `_channelsToRejoin` is a volatile `List<string>` reference, assigned during unexpected disconnect and read/swapped during reconnect without `Interlocked.Exchange` or `_stateLock`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L53), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1052-L1059), [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2339-L2342).

Impact: concurrent disconnect/reconnect paths can lose attempt increments, exceed configured limits, or lose/stomp the rejoin snapshot. This is adjacent to the dispose/reconnect race and should be fixed with it.

Suggested fix: use `Interlocked.Increment` / `Interlocked.Exchange` for attempts and atomically exchange the rejoin list before enumeration.

### 9. IRCv3 server-time parsing accepts off-spec formats and drops leap seconds

Evidence: `IrcEvent.GetTimestamp` uses `DateTimeOffset.TryParse` with invariant culture and UTC adjustment: [../src/IRCDotNet.Core/Events/IrcEvents.cs](../src/IRCDotNet.Core/Events/IrcEvents.cs#L31-L42).

Impact: common server-time values work, and the culture fix is good. But the IRCv3 server-time grammar is intentionally narrow (`YYYY-MM-DDThh:mm:ss.sssZ`), while `TryParse` accepts looser formats. It also cannot represent the leap-second example with `:60`, so those valid IRCv3 timestamps silently fall back to local receipt time. That is low-frequency but still protocol non-compliance.

Suggested fix: use `TryParseExact` for the required grammar, then explicitly handle `:60` by clamping or documenting a fallback behavior. Add tests for non-Gregorian current culture, off-spec accepted strings, and leap seconds.

### 10. `HandleMode` still treats only `#` and `&` as channel prefixes

Evidence: `HandleMode` branches channel-vs-user mode using `target.StartsWith("#") || target.StartsWith("&")`: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L3238-L3263). The release delta fixed `+` and `!` channel detection for message/action/typing events, but not here: [../src/IRCDotNet.Core/Events/IrcEvents.cs](../src/IRCDotNet.Core/Events/IrcEvents.cs#L154-L162), [../src/IRCDotNet.Core/Events/IrcEvents.cs](../src/IRCDotNet.Core/Events/IrcEvents.cs#L1068-L1076), [../src/IRCDotNet.Core/Events/IrcEvents.cs](../src/IRCDotNet.Core/Events/IrcEvents.cs#L1231-L1236).

Impact: MODE changes for `+modeless` or `!safe` channels are logged/handled as user modes. This is the same IRC channel-prefix compliance gap the release just fixed elsewhere.

Suggested fix: extract an `IsChannelTarget` helper based on ISUPPORT `CHANTYPES`, with a fallback to `#&+!`, and use it everywhere channel detection is needed.

## P3 Findings

### 11. Synthetic MONITOR-driven `UserQuit` is not distinguishable from a real QUIT

Evidence: `FinalizePendingMonitoredOfflineAsync` raises `UserQuit` from the 731 numeric message with no marker or reason: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2968-L2979). The tests assert a `UserQuitEvent` for PM-only monitor offline, but do not assert a way to tell it is synthetic: [../tests/IRCDotNetCore.Tests/IrcClientMonitorCorrelationTests.cs](../tests/IRCDotNetCore.Tests/IrcClientMonitorCorrelationTests.cs#L34-L98).

Impact: consumers cannot tell whether a user sent/propagated a real `QUIT` or whether the library synthesized one from MONITOR. That can leak into UI, logs, analytics, and reconnect heuristics that treat quit reasons specially.

Suggested fix: add `IsSynthetic` / `SourceKind` to `UserQuitEvent`, or introduce a dedicated monitor offline event and make the synthetic quit opt-in. At minimum set a stable reason such as `MONITOR offline`.

### 12. NAMES fallback loses prefix metadata when no pending snapshot exists

Evidence: if `RPL_ENDOFNAMES` arrives and no pending NAMES state exists, the fallback creates `ChannelUser` objects from the channel nick set only: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2051-L2055). The canonical `_channels` state stores only strings, so prefix data from prior authoritative NAMES snapshots is not retained: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2035-L2045).

Impact: a consumer that issues or receives a NAMES end without a preceding captured snapshot can get `ChannelUsersReceived` with all users stripped of operator/voice prefixes. The new convergence tests cover the pending-snapshot path, not this fallback: [../tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L633-L690).

Suggested fix: retain `ChannelUser` metadata in the channel model, not just nick strings, or document the fallback as metadata-poor and avoid raising a full `ChannelUsersReceived` snapshot from it.

### 13. Release notes do not cover most post-2.5.1 behavior changes

Evidence: README `Unreleased` currently documents the monitor-correlation changes and tests, but not server-time timestamps, CAP DEL snapshot re-raise, `IsEcho` on notice/action, PING/PONG limiter bypass, send-lock disposal race translation, reconnect/NAMES convergence, or 433 retry fixes: [../README.md](../README.md#L815-L827).

Impact: package consumers will not know which protocol and lifecycle behaviors changed after 2.5.1. This is a release-readiness gap rather than a runtime bug, but it matters because several changes alter event contracts.

Suggested fix: expand `Unreleased` before the next NuGet package and call out event contract changes separately from internal hardening.

## Test Gaps To Close

- Dispose/reconnect: add tests for `DisposeAsync` during reconnect delay and during `ConnectAsync`, and assert no post-dispose `Connected` event or retry loop.
- Event drain: tighten `DisposeAsync_WhenEventsAreAlreadyQueued_ShouldDrainQueueAndRaiseDisconnected` so handler completion is observed before `DisposeAsync` returns.
- Monitor cancellation: add deterministic `MONOFFLINE` then `MONONLINE` correlation-window tests that force the finalizer interleaving.
- Monitor numerics: add 732/733/734 tests, especially list-full rejection removing local pending monitor state.
- CASEMAPPING: add tests for RFC1459 bracket equivalents in self JOIN/PART/KICK, echo-message, `_monitoredNicks`, `_userInfo`, and channel user removal.
- MODE channel prefixes: add tests for `+` and `!` channel targets through `HandleMode`.
- Server-time: add exact-format, loose-format, and leap-second tests.
- Sync dispose: add tests for CTS disposal and already queued event handlers at sync dispose time.

## Confirmed Good In This Delta

- Keepalive PING/PONG bypasses the user-message rate limiter, and tests use a near-empty bucket to pin starvation behavior: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L480-L740), [../tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L385-L469).
- Per-client send serialization is now effectively single-writer via `_sendLock = new(1, 1)`, and a concurrency test pins write ordering: [../tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L478-L503).
- `_sendLock` `ObjectDisposedException` is translated to `InvalidOperationException("Not connected")` on the send path and has targeted tests: [../tests/IRCDotNetCore.Tests/Threading/SendDisposalRaceTests.cs](../tests/IRCDotNetCore.Tests/Threading/SendDisposalRaceTests.cs#L1-L120).
- CAP DEL now raises a fresh capability snapshot and has a focused regression test: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L1454-L1489), [../tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L367-L383).
- Empty `AlternativeNicks` 433 handling now sends a timestamp-suffixed retry instead of stalling registration: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2128-L2157), [../tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L272-L365).
- NAMES convergence correctly handles ordinary interleaved JOIN/PART/QUIT/NICK mutations while a snapshot is pending, subject to the CASEMAPPING and metadata concerns above: [../src/IRCDotNet.Core/IrcClient.cs](../src/IRCDotNet.Core/IrcClient.cs#L2022-L2098), [../tests/IRCDotNetCore.Tests/IrcClientTests.cs](../tests/IRCDotNetCore.Tests/IrcClientTests.cs#L633-L690).
- Typing/channel detection was expanded to `#`, `&`, `+`, and `!` for the event properties that were changed, and theory tests cover those prefixes: [../src/IRCDotNet.Core/Events/IrcEvents.cs](../src/IRCDotNet.Core/Events/IrcEvents.cs#L1231-L1236), [../tests/IRCDotNetCore.Tests/Events/TypingIndicatorTests.cs](../tests/IRCDotNetCore.Tests/Events/TypingIndicatorTests.cs#L72-L94).

## Verification Notes

I reviewed source and tests directly and compared the release delta against the 2.5.1 baseline. I did not run the full test suite in this pass; the repo instructions list three known pre-existing unit failures unrelated to this review. Live concurrency tests were treated as coverage references only, not as fresh verification.