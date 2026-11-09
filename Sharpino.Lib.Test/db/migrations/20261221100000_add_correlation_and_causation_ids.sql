-- migrate:up

-- Add correlation_id and causation_id to events_01_sampleObject
ALTER TABLE public.events_01_sampleObject
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_sampleObject_correlation_id 
    ON public.events_01_sampleObject(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_sampleObject_causation_id 
    ON public.events_01_sampleObject(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Add correlation_id and causation_id to events_01_sampleContext
ALTER TABLE public.events_01_sampleContext
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_sampleContext_correlation_id 
    ON public.events_01_sampleContext(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_sampleContext_causation_id 
    ON public.events_01_sampleContext(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Update helper insert function for sampleObject
CREATE OR REPLACE FUNCTION insert_md_01_sampleObject_event_and_return_id(
    IN event_in text,
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
INSERT INTO events_01_sampleObject(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

-- Update opt_lock function for sampleObject
CREATE OR REPLACE FUNCTION insert_md_01_sampleObject_aggregate_event_and_return_id_opt_lock(
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
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id 
    FROM events_01_sampleObject 
    WHERE events_01_sampleObject.aggregate_id = insert_md_01_sampleObject_aggregate_event_and_return_id_opt_lock.aggregate_id 
    ORDER BY id DESC LIMIT 1;

    IF last_event_id = 0 THEN
        IF found_last_event_id IS NOT NULL THEN
            RAISE EXCEPTION 'Optimistic locking check failed: expected no previous events, but found event %', found_last_event_id;
        END IF;
    ELSIF last_event_id > 0 THEN
        IF found_last_event_id IS NULL OR found_last_event_id <> last_event_id THEN
            RAISE EXCEPTION 'Optimistic locking check failed: expected last event id %, but found %', last_event_id, found_last_event_id;
        END IF;
    END IF;

    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md, correlation_id, causation_id);

    INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Update opt_lock2 function for sampleObject
CREATE OR REPLACE FUNCTION insert_md_01_sampleObject_aggregate_event_and_return_id_opt_lock2(
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
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_sampleObject', aggregate_id, last_event_id);

    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md, correlation_id, causation_id);

    INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Add patch procedure for sampleObject
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots_01_sampleObject(
    IN target_aggregate_id uuid,
    IN target_event_id int,
    IN noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_sampleObject
    SET event = noop_payload
    WHERE aggregate_id = target_aggregate_id AND id = target_event_id;

    DELETE FROM snapshots_01_sampleObject
    WHERE aggregate_id = target_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= target_event_id;
END;
$$;

-- Update helper insert function for sampleContext
CREATE OR REPLACE FUNCTION insert_md_01_sampleContext_event_and_return_id(
    IN event_in text, 
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
INSERT INTO events_01_sampleContext(event, timestamp, md, correlation_id, causation_id)
VALUES(event_in::text, now(), md_in, correlation_id, causation_id) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

-- Add patch procedure for sampleContext
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots_01_sampleContext(
    IN target_event_id int,
    IN noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_sampleContext
    SET event = noop_payload
    WHERE id = target_event_id;

    DELETE FROM snapshots_01_sampleContext
    WHERE event_id >= target_event_id;
END;
$$;

-- migrate:down

