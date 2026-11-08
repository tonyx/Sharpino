-- migrate:up

-- Add correlation_id and causation_id to events_01_site
ALTER TABLE public.events_01_site
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_site_correlation_id 
    ON public.events_01_site(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_site_causation_id 
    ON public.events_01_site(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Add correlation_id and causation_id to events_01_truck
ALTER TABLE public.events_01_truck
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_truck_correlation_id 
    ON public.events_01_truck(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_truck_causation_id 
    ON public.events_01_truck(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Drop obsolete functions to avoid ambiguity
DROP FUNCTION IF EXISTS insert_md_01_site_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer);
DROP FUNCTION IF EXISTS insert_md_01_truck_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer);
DROP FUNCTION IF EXISTS insert_md_01_site_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_truck_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_site_event_and_return_id(text, uuid, integer, text);
DROP FUNCTION IF EXISTS insert_md_01_truck_event_and_return_id(text, uuid, integer, text);

-- Site insert_md event
CREATE OR REPLACE FUNCTION insert_md_01_site_event_and_return_id(
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
    INSERT INTO events_01_site(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Site opt_lock (7 params)
CREATE OR REPLACE FUNCTION insert_md_01_site_aggregate_event_and_return_id_opt_lock(
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
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_site', aggregate_id, last_event_id);
    INSERT INTO events_01_site(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_site(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Site opt_lock2 (10 params)
CREATE OR REPLACE FUNCTION insert_md_01_site_aggregate_event_and_return_id_opt_lock2(
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
    PERFORM check_last_event_id_opt_lock('events_01_site', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_site(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_site(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Truck insert_md event
CREATE OR REPLACE FUNCTION insert_md_01_truck_event_and_return_id(
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
    INSERT INTO events_01_truck(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Truck opt_lock (7 params)
CREATE OR REPLACE FUNCTION insert_md_01_truck_aggregate_event_and_return_id_opt_lock(
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
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_truck', aggregate_id, last_event_id);
    INSERT INTO events_01_truck(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_truck(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Truck opt_lock2 (10 params)
CREATE OR REPLACE FUNCTION insert_md_01_truck_aggregate_event_and_return_id_opt_lock2(
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
    PERFORM check_last_event_id_opt_lock('events_01_truck', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_truck(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_truck(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Atomic patch for site
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_site(
    IN in_aggregate_id uuid,
    IN in_event_id integer,
    IN in_noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_site
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_site
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;

-- Atomic patch for truck
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_truck(
    IN in_aggregate_id uuid,
    IN in_event_id integer,
    IN in_noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_truck
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_truck
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;

-- migrate:down

