-- migrate:up

-- account: causality columns + partial indexes
ALTER TABLE public.events_01_account
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_account_correlation_id
    ON public.events_01_account(correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_account_causation_id
    ON public.events_01_account(causation_id)
    WHERE causation_id IS NOT NULL;

-- counter: causality columns + partial indexes
ALTER TABLE public.events_01_counter
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_counter_correlation_id
    ON public.events_01_counter(correlation_id)
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_counter_causation_id
    ON public.events_01_counter(causation_id)
    WHERE causation_id IS NOT NULL;

-- Drop obsolete/ambiguous overloads (including legacy text-typed opt_lock2 in a binary store)
DROP FUNCTION IF EXISTS insert_md_01_account_aggregate_event_and_return_id_opt_lock(bytea, uuid, integer, text, integer);
DROP FUNCTION IF EXISTS insert_md_01_account_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[]);
DROP FUNCTION IF EXISTS insert_md_01_account_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_account_aggregate_event_and_return_id_opt_lock2(bytea, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_account_event_and_return_id(bytea, uuid, integer, text);
DROP FUNCTION IF EXISTS insert_md_01_counter_aggregate_event_and_return_id_opt_lock(bytea, uuid, integer, text, integer);
DROP FUNCTION IF EXISTS insert_md_01_counter_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[]);
DROP FUNCTION IF EXISTS insert_md_01_counter_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_counter_aggregate_event_and_return_id_opt_lock2(bytea, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_counter_event_and_return_id(bytea, uuid, integer, text);

--------------------------------------------------------------------------------
-- account functions
--------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION insert_md_01_account_event_and_return_id(
    IN event_in bytea,
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
    INSERT INTO events_01_account(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md_01_account_aggregate_event_and_return_id_opt_lock(
    IN event_in bytea,
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
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_account', aggregate_id, last_event_id);
    INSERT INTO events_01_account(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_account(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md_01_account_aggregate_event_and_return_id_opt_lock2(
    IN event_in bytea,
    IN aggregate_id uuid,
    IN distance_from_latest_snapshot int,
    IN md text,
    IN last_event_id int,
    IN extra_stream_names text[],
    IN extra_event_ids int[],
    IN extra_aggregate_ids uuid[],
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_account', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_account(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_account(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Atomic NoOp patch + snapshot purge (root genesis snapshots with event_id IS NULL are kept)
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_account(
    IN in_aggregate_id uuid,
    IN in_event_id integer,
    IN in_noop_payload bytea
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_account
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_account
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;

--------------------------------------------------------------------------------
-- counter functions
--------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION insert_md_01_counter_event_and_return_id(
    IN event_in bytea,
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
    INSERT INTO events_01_counter(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md_01_counter_aggregate_event_and_return_id_opt_lock(
    IN event_in bytea,
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
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_counter', aggregate_id, last_event_id);
    INSERT INTO events_01_counter(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_counter(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md_01_counter_aggregate_event_and_return_id_opt_lock2(
    IN event_in bytea,
    IN aggregate_id uuid,
    IN distance_from_latest_snapshot int,
    IN md text,
    IN last_event_id int,
    IN extra_stream_names text[],
    IN extra_event_ids int[],
    IN extra_aggregate_ids uuid[],
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_counter', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_counter(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_counter(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Atomic NoOp patch + snapshot purge (root genesis snapshots with event_id IS NULL are kept)
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_counter(
    IN in_aggregate_id uuid,
    IN in_event_id integer,
    IN in_noop_payload bytea
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_counter
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_counter
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;

-- migrate:down
