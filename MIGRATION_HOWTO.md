# Sharpino Migration Guide: Causality Tracking, Unforgiving Evolution & Event Patching

This guide details the architectural enhancements introduced in Sharpino 7.0.1 and provides step-by-step instructions for upgrading existing databases and applications.

---

## 1. Architectural Overview

### 1.1 From "Forgiving" to Strict Evolution
Previous versions included `evolveSkippingErrors`, an error-filtering fold intended to skip corrupt or schema-drifted events during replay. During development you may break existing contract and exising events and so a workaround was to try to skipn them. 
Apart from development and apart from the fact that optimistic lockand MailBoxProcessor on single stream must prevent this possibility, there is still a newer workaround that is somehow preferrable as the previous approach had potential issues.
It leads to silent domain invariant violations in subsequent dependent events.
As formally proven and verified in the test suite:
- Skipping an event creates silent domain invariant violations in subsequent dependent events.
- Replayed states diverge from cached snapshots.
- Concurrency locks give a false sense of safety if replay history differs from original command execution.

In this release some improvement are related to the idea that inconsistency must not be there, but if thet are then the event store can be "patched", the cache can be cleaned and the application can continue working logging the anomaly.
So we have:
- **Strict unforgiving evolution** is the default and only evolution path (`evolveUNforgivingErrors`).
- When an anomaly or deserialization error is encountered, evolution halts immediately.
- Pluggable anomaly reporting is enabled via `IEvolveAnomalyHandler` (e.g. log, quarantine aggregate, or halt host).

### 1.2 The "Third Way": In-Store NoOp Patching & Sanitization
To recover from poison pills, ephemeral events (e.g., expired QR codes), or non-backward-compatible migrations without discarding event history:
1. **Targeted Event Patching**: An erroneous event payload is atomically overwritten with a domain `NoOp` event in the database (`PatchEventWithNoOp`).
2. **Snapshot Invalidation**: Any snapshot taken at or after the patched event ID (`event_id >= target_event_id`) is purged atomically in the same transaction. Genesis snapshots (`event_id IS NULL`) are preserved.
3. **Cache Eviction**: `AggregateCache3` entries are immediately invalidated for the affected aggregate.
4. **Ex-Post Compensation**: If the patched event had a `CorrelationId`, sibling events in other aggregate streams from the same multi-aggregate transaction are identified via `FindEventsByCorrelationId` and compensated.

---

## 2. Database Migration

For each aggregate and context in your database, execute the following SQL migration.

### 2.1 Context Migration (e.g., `events_01_myContext`)

```sql
-- 1. Add Causality Columns & Index
ALTER TABLE events{Version}{ContextStorageName}
ADD COLUMN IF NOT EXISTS correlation_id uuid DEFAULT NULL,
ADD COLUMN IF NOT EXISTS causation_id uuid DEFAULT NULL;

CREATE INDEX IF NOT EXISTS idx_events{Version}{ContextStorageName}_correlation_id 
ON events{Version}{ContextStorageName}(correlation_id) 
WHERE correlation_id IS NOT NULL;

-- 2. Update insert procedure to accept correlation and causation IDs
CREATE OR REPLACE FUNCTION insert_md{Version}{ContextStorageName}_event_and_return_id(
    IN event_in text, -- or bytea if binary store
    IN md_in text,
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events{Version}{ContextStorageName}(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in, now(), md_in, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    RETURN inserted_id;
END;
$$;

-- 3. Add NoOp Patch Procedure
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots_{Version}{ContextStorageName}(
    IN target_event_id int,
    IN noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events{Version}{ContextStorageName}
    SET event = noop_payload
    WHERE id = target_event_id;

    DELETE FROM snapshots{Version}{ContextStorageName}
    WHERE event_id >= target_event_id;
END;
$$;
```

### 2.2 Aggregate Migration (e.g., `events_01_myAggregate`)

```sql
-- 1. Add Causality Columns & Indexes
ALTER TABLE events{Version}{AggregateStorageName}
ADD COLUMN IF NOT EXISTS correlation_id uuid DEFAULT NULL,
ADD COLUMN IF NOT EXISTS causation_id uuid DEFAULT NULL;

CREATE INDEX IF NOT EXISTS idx_events{Version}{AggregateStorageName}_correlation_id 
ON events{Version}{AggregateStorageName}(correlation_id) 
WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_events{Version}{AggregateStorageName}_causation_id 
ON events{Version}{AggregateStorageName}(causation_id) 
WHERE causation_id IS NOT NULL;

-- 2. Update insert function with causality support
CREATE OR REPLACE FUNCTION insert_md{Version}{AggregateStorageName}_event_and_return_id(
    IN event_in text, -- or bytea for binary store
    IN aggregate_id uuid,
    IN distance_from_latest_snapshot int,
    IN md text,
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events{Version}{AggregateStorageName}(event, aggregate_id, timestamp, distance_from_latest_snapshot, md, correlation_id, causation_id)
    VALUES(event_in, aggregate_id, now(), distance_from_latest_snapshot, md, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    RETURN inserted_id;
END;
$$;

-- 3. Update optimistic locking procedure (default NULL maintains 100% backward compatibility)
CREATE OR REPLACE FUNCTION insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id_opt_lock(
    IN event_in text, 
    IN aggregate_id uuid,
    IN distance_from_latest_snapshot int,
    IN md text,
    IN last_event_id int,
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    event_id integer;
    current_last_event_id integer;
BEGIN
    SELECT COALESCE(MAX(id), 0) INTO current_last_event_id 
    FROM events{Version}{AggregateStorageName}
    WHERE events{Version}{AggregateStorageName}.aggregate_id = insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id_opt_lock.aggregate_id;

    IF current_last_event_id <> last_event_id THEN
        RAISE EXCEPTION 'optimistic lock failed: current event id % is not %', current_last_event_id, last_event_id;
    END IF;

    event_id := insert_md{Version}{AggregateStorageName}_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md, correlation_id, causation_id);
    RETURN event_id;
END;
$$;

-- 4. Add Atomic Patch & Snapshot Purge Procedure
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots_{Version}{AggregateStorageName}(
    IN target_aggregate_id uuid,
    IN target_event_id int,
    IN noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events{Version}{AggregateStorageName}
    SET event = noop_payload
    WHERE aggregate_id = target_aggregate_id AND id = target_event_id;

    -- Delete snapshots at or after target_event_id (preserves initial genesis snapshot where event_id IS NULL)
    -- IMPORTANT: Never include `OR event_id IS NULL` here, as that would delete the aggregate's initial state!
    DELETE FROM snapshots{Version}{AggregateStorageName}
    WHERE aggregate_id = target_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= target_event_id;
END;
$$;
```

> [!NOTE]
> **PostgreSQL 63-Character Identifier Limit (`NAMEDATALEN`)**: PostgreSQL truncates function names to 63 bytes. For aggregates with a `StorageName` longer than ~10 characters (e.g. `_Extracatalogue`), `insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id_opt_lock` and `..._opt_lock2` truncate to the same 63-byte identifier. They coexist safely in PostgreSQL because they have distinct parameter signatures (7 parameters vs. 10 parameters), so ensure you use `CREATE OR REPLACE FUNCTION` without dropping the 7-parameter overload when adding `opt_lock2`.

---

## 3. Application Configuration & Code Updates

### 3.1 Defining Domain NoOp Events
Add a `NoOp` case to your aggregate and context event types:

```fsharp
type OrderEvents =
    | OrderCreated of OrderDetails
    | OrderShipped of TrackingNumber
    | OrderNoOp // Handled as Ok state
    interface Event<Order> with
        member this.Process(state: Order) =
            match this with
            | OrderCreated details -> state.Create details
            | OrderShipped tracking -> state.Ship tracking
            | OrderNoOp -> Ok state
```

### 3.2 Configuring Evolve Anomaly Handlers
Configure a custom anomaly handler via `Sharpino.Core.currentAnomalyHandler` (prefer implementing `OnEvolveAnomalyAsync` so async read paths in `StateView` stay 100% non-blocking):

```fsharp
open System.Threading.Tasks
open Sharpino.Core
open Sharpino.Sanitizer

type AutoRecoveryAnomalyHandler(eventStore: IEventStore<string>, logger: ILogger) =
    interface IEvolveAnomalyHandler with
        member this.OnEvolveAnomaly(ctx: AnomalyContext) =
            logger.LogCritical("Poison pill detected in {Stream} event {Id}: {Error}", 
                ctx.StreamName, ctx.EventId, ctx.ErrorMessage)

        member this.OnEvolveAnomalyAsync(ctx: AnomalyContext) =
            task {
                logger.LogCritical("Poison pill detected in {Stream} event {Id}: {Error}", 
                    ctx.StreamName, ctx.EventId, ctx.ErrorMessage)
                match ctx.AggregateId with
                | Some id when ctx.AggregateType = typeof<Order> ->
                    let! _ = sanitizeAggregateStreamAsync<Order, OrderEvents, string> eventStore id OrderNoOp None
                    ()
                | _ -> ()
            }
            
currentAnomalyHandler <- AutoRecoveryAnomalyHandler(eventStore, myLogger)
```

### 3.3 Running the Sanitizer
To asynchronously repair an aggregate stream containing an inconsistent event (includes built-in per-aggregate `SemaphoreSlim` single-flight locking and a configurable cooldown window via `"SanitizerCooldownMs"` or `Sanitizer.setCooldownMs`):

```fsharp
open FsToolkit.ErrorHandling
open Sharpino.Sanitizer

// Define optional async cross-aggregate compensator
let compensator (correlationId: Guid) (poisonEventId: EventId) : TaskResult<string, string> =
    taskResult {
        // Query sibling events on correlated streams
        let! siblingEvents = eventStore.FindEventsByCorrelationIdAsync("01", "_booking", correlationId)
        // Issue compensating action against related booking
        return "Compensated related booking"
    }

// Execute async sanitization
let! result = 
    Sanitizer.sanitizeAggregateStreamAsync<Order, OrderEvents, string> 
        eventStore 
        orderId 
        OrderNoOp 
        (Some compensator)

match result with
| Ok report when report.IsClean ->
    printfn "Stream was already clean."
| Ok report ->
    printfn "Stream healed with actions: %A" report.Actions
| Error err ->
    printfn "Sanitization failed: %s" err
```

