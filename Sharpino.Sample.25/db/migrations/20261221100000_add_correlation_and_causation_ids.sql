-- migrate:up

-- Add correlation_id and causation_id to events_01_todo
ALTER TABLE public.events_01_todo
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_todo_correlation_id 
    ON public.events_01_todo(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_todo_causation_id 
    ON public.events_01_todo(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Drop obsolete functions to avoid ambiguity
DROP FUNCTION IF EXISTS insert_md_01_todo_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer);
DROP FUNCTION IF EXISTS insert_md_01_todo_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]);

-- Todo opt_lock (7 params)
CREATE OR REPLACE FUNCTION insert_md_01_todo_aggregate_event_and_return_id_opt_lock(
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
    inserted_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_todo', aggregate_id, last_event_id);
    INSERT INTO events_01_todo(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Todo opt_lock2 (10 params)
CREATE OR REPLACE FUNCTION insert_md_01_todo_aggregate_event_and_return_id_opt_lock2(
    IN event_in text,
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
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_todo', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;
    INSERT INTO events_01_todo(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Atomic patch for todo
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_todo(
    IN in_aggregate_id uuid,
    IN in_event_id integer,
    IN in_noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_todo
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_todo
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;

-- migrate:down

