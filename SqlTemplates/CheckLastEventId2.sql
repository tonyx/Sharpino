-- migrate:up

-- Replace check_last_event_id_opt_lock with an advisory-lock-aware version.
--
-- RATIONALE:
--   The previous version did a plain SELECT to find the max event_id for an aggregate,
--   then the caller inserted a new event.  Under PostgreSQL's default READ COMMITTED
--   isolation a second concurrent transaction could slip an INSERT in between the
--   SELECT and the INSERT of the first transaction, causing two writes with the same
--   "last_event_id" claim to both succeed.
--
--   SELECT ... FOR UPDATE does NOT prevent phantom INSERTs (new rows), so it would
--   not close this gap.
--
--   Instead, we acquire a session-scoped transaction advisory lock keyed on:
--     hashtext(full_stream_name || '|' || target_aggregate_id::text)
--   This lock is held until the calling transaction commits or rolls back, so any
--   second concurrent transaction trying to write to the same aggregate will block
--   until the first one is done.  The result is per-aggregate serialization of writes
--   with zero schema changes and no impact on writes to different aggregates.

CREATE OR REPLACE FUNCTION check_last_event_id_opt_lock(
    IN stream_name text,
    IN target_aggregate_id uuid,
    IN expected_last_event_id int
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    found_last_event_id integer;
    query text;
    full_stream_name text;
    lock_key bigint;
BEGIN
    full_stream_name := stream_name;
    IF NOT full_stream_name LIKE 'events_%' THEN
        IF full_stream_name LIKE '_%' THEN
            full_stream_name := 'events' || full_stream_name;
        ELSE
            full_stream_name := 'events_' || full_stream_name;
        END IF;
    END IF;

    -- If target_aggregate_id is null, try to resolve it from the expected_last_event_id
    IF target_aggregate_id IS NULL THEN
        query := format('SELECT aggregate_id FROM %I WHERE id = $1', full_stream_name);
        EXECUTE query INTO target_aggregate_id USING expected_last_event_id;
    END IF;

    IF target_aggregate_id IS NULL THEN
        IF expected_last_event_id > 0 THEN
            RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected event % not found to resolve aggregate', full_stream_name, expected_last_event_id;
        END IF;
    ELSE
        -- Acquire a per-aggregate advisory lock for the duration of this transaction.
        -- This prevents concurrent writes to the same aggregate from interleaving
        -- between our SELECT (below) and the INSERT performed by the caller.
        -- pg_advisory_xact_lock is released automatically on COMMIT / ROLLBACK.
        lock_key := hashtext(full_stream_name || '|' || target_aggregate_id::text);
        PERFORM pg_advisory_xact_lock(lock_key);

        query := format('SELECT id FROM %I WHERE aggregate_id = $1 ORDER BY id DESC LIMIT 1', full_stream_name);
        EXECUTE query INTO found_last_event_id USING target_aggregate_id;

        IF expected_last_event_id = 0 THEN
            IF found_last_event_id IS NOT NULL THEN
                RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected no previous events, but found event %', full_stream_name, found_last_event_id;
            END IF;
        ELSIF expected_last_event_id > 0 THEN
            IF found_last_event_id IS NULL OR found_last_event_id <> expected_last_event_id THEN
                RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected last event id %, but found %', full_stream_name, expected_last_event_id, found_last_event_id;
            END IF;
        END IF;
    END IF;
END;
$$;

-- migrate:down

CREATE OR REPLACE FUNCTION check_last_event_id_opt_lock(
    IN stream_name text,
    IN target_aggregate_id uuid,
    IN expected_last_event_id int
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    found_last_event_id integer;
    query text;
    full_stream_name text;
BEGIN
    full_stream_name := stream_name;
    IF NOT full_stream_name LIKE 'events_%' THEN
        IF full_stream_name LIKE '_%' THEN
            full_stream_name := 'events' || full_stream_name;
        ELSE
            full_stream_name := 'events_' || full_stream_name;
        END IF;
    END IF;

    IF target_aggregate_id IS NULL THEN
        query := format('SELECT aggregate_id FROM %I WHERE id = $1', full_stream_name);
        EXECUTE query INTO target_aggregate_id USING expected_last_event_id;
    END IF;

    IF target_aggregate_id IS NULL THEN
        IF expected_last_event_id > 0 THEN
            RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected event % not found to resolve aggregate', full_stream_name, expected_last_event_id;
        END IF;
    ELSE
        query := format('SELECT id FROM %I WHERE aggregate_id = $1 ORDER BY id DESC LIMIT 1', full_stream_name);
        EXECUTE query INTO found_last_event_id USING target_aggregate_id;

        IF expected_last_event_id = 0 THEN
            IF found_last_event_id IS NOT NULL THEN
                RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected no previous events, but found event %', full_stream_name, found_last_event_id;
            END IF;
        ELSIF expected_last_event_id > 0 THEN
            IF found_last_event_id IS NULL OR found_last_event_id <> expected_last_event_id THEN
                RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected last event id %, but found %', full_stream_name, expected_last_event_id, found_last_event_id;
            END IF;
        END IF;
    END IF;
END;
$$;