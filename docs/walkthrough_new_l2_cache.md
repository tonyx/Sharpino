# Walkthrough: L2 Cache & Multi-Tier Caching Improvements in Sharpino

We have completed the implementation of the L2 Cache architectural improvements in `Sharpino.Lib`, resolving the FusionCache multi-tier caching challenges and accelerating aggregate snapshot retrieval.

---

## Key Achievements

### 1. Serializable DTO: `CachedAggregateEntry`
In [`Cache.fs`](file:///Users/antoniolucca/github/realsharpino/Sharpino/Sharpino.Lib/Cache.fs):
- Previously, `AggregateCache3` stored an un-serializable `Task<Result<EventId * obj, string>>` inside FusionCache. When `SetupDistributedCache` was called, FusionCache was unable to serialize `Task` instances to L2 (PostgreSQL / Redis), causing silent fallbacks and disabled L2 caching.
- Introduced `CachedAggregateEntry`:
  ```fsharp
  [<CLIMutable>]
  type CachedAggregateEntry = {
      EventId: EventId
      TypeName: string
      StateJson: string
      [<System.Text.Json.Serialization.JsonIgnore>]
      mutable BoxedState: obj option
  }
  ```
- **L1 Performance Guarantee**: For in-memory (L1) hits, `BoxedState` holds the direct in-memory reference to the aggregate object. Reading from L1 avoids any JSON deserialization overhead, preserving sub-microsecond retrieval speed.
- **L2 Serialization**: When writing or spilling to L2 (PostgreSQL / Redis), `EventId`, `TypeName`, and `StateJson` serialize cleanly via standard JSON serializers. Upon rehydration from L2, `BoxedState` is lazily deserialized and populated.
- **Backwards-Compatible API**: Preserved `Memoize`, `MemoizeAsync`, `Memoize2 (eventId, newState |> box) aggregateId`, `LastEventId`, `GetState`, and `GetStateAsync`. All existing call sites across `CommandHandler.fs` and application code compile without changes.

### 2. Enabled Distributed Cache Setup
In `AggregateCache3.SetupL2AndBackplane`:
- Re-enabled `(statePerAggregate :> IFusionCache).SetupDistributedCache(dc.Value, ser.Value)`.
- Configured backplane event receiver options with `SkipDistributedCacheRead = true` and `SkipDistributedCacheWrite = true`, ensuring that distributed backplane eviction signals (such as PG `LISTEN/NOTIFY` or Redis Pub/Sub) evict stale L1 memory entries while allowing L2 to retain the latest serialized aggregate snapshot.
- Implemented `ClearL1()` via `MemoryCacheAccessor.TryClear()` to invalidate L1 while keeping L2 intact for testing and rolling node deployments.

### 3. Read-Through Aggregate Snapshot Acceleration in `StateView.fs`
In [`StateView.fs`](file:///Users/antoniolucca/github/realsharpino/Sharpino/Sharpino.Lib/StateView.fs):
- Both `getLastAggregateSnapshot` and `getLastAggregateSnapshotAsync` now consult `AggregateCache3.Instance.GetEntry / GetEntryAsync(aggregateId)` before hitting the database aggregate snapshot table.
- Added `convertStateJsonToFormat<'F>` to convert serialized state representations to either string or binary (`byte[]`) depending on the configured event store.
- Rebuilding aggregate state on fresh nodes or after cold starts is now accelerated by distributed L2 cache read-through, eliminating heavy snapshot table queries when the cached snapshot is already current.

---

## Verification & Test Results

### 1. Unit Tests in `Sharpino.Lib.Test`
Added [`AggregateCache3Tests.fs`](file:///Users/antoniolucca/github/realsharpino/Sharpino/Sharpino.Lib.Test/AggregateCache3Tests.fs) using `MemoryDistributedCache` to exercise multi-tier caching:
1. `Memoize2 populates L1 and writes serializable entry to L2 distributed cache` — **PASSED**
2. `ClearL1 removes in-memory state while L2 distributed cache allows rehydration` — **PASSED**
3. `StateView.getLastAggregateSnapshotAsync uses L1/L2 cache before querying event store` — **PASSED**
4. `StateView.getLastAggregateSnapshot synchronous also utilizes L1/L2 cache` — **PASSED**
5. `Clean removes entry from both L1 and L2` — **PASSED**

```
[15:06:05 INF] EXPECTO! 5 tests run in 00:00:00.3802640 for AggregateCache3 L2 and Multi-Tier Cache Tests – 5 passed, 0 ignored, 0 failed, 0 errored. Success!
```

### 2. Multi-Target Framework Compilation
Verified clean compilation across all target frameworks:
- `net8.0` — **0 Errors**
- `net9.0` — **0 Errors**
- `net10.0` — **0 Errors**

---

## Files Modified / Created

| File | Status | Description |
|------|--------|-------------|
| [`Sharpino.Lib/Cache.fs`](file:///Users/antoniolucca/github/realsharpino/Sharpino/Sharpino.Lib/Cache.fs) | Modified | Added `CachedAggregateEntry`, re-enabled L2 distributed cache setup, refactored `AggregateCache3` methods and `ClearL1`. |
| [`Sharpino.Lib/StateView.fs`](file:///Users/antoniolucca/github/realsharpino/Sharpino/Sharpino.Lib/StateView.fs) | Modified | Integrated `AggregateCache3` read-through in `getLastAggregateSnapshot` and `getLastAggregateSnapshotAsync`. |
| [`Sharpino.Lib.Test/Sharpino.Lib.Test.fsproj`](file:///Users/antoniolucca/github/realsharpino/Sharpino/Sharpino.Lib.Test/Sharpino.Lib.Test.fsproj) | Modified | Registered `AggregateCache3Tests.fs`. |
| [`Sharpino.Lib.Test/AggregateCache3Tests.fs`](file:///Users/antoniolucca/github/realsharpino/Sharpino/Sharpino.Lib.Test/AggregateCache3Tests.fs) | Added | 5 comprehensive unit tests for L1/L2 cache behavior, rehydration, and StateView acceleration. |
