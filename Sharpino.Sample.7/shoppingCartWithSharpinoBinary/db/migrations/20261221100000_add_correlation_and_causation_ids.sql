-- migrate:up

-- Add correlation_id and causation_id to events_01_goodscontainer
ALTER TABLE public.events_01_goodscontainer
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_goodscontainer_correlation_id 
    ON public.events_01_goodscontainer(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_goodscontainer_causation_id 
    ON public.events_01_goodscontainer(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Add correlation_id and causation_id to events_01_good
ALTER TABLE public.events_01_good
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_good_correlation_id 
    ON public.events_01_good(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_good_causation_id 
    ON public.events_01_good(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Add correlation_id and causation_id to events_01_cart
ALTER TABLE public.events_01_cart
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_cart_correlation_id 
    ON public.events_01_cart(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_cart_causation_id 
    ON public.events_01_cart(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Drop obsolete functions to avoid ambiguity
DROP FUNCTION IF EXISTS insert_md_01_good_aggregate_event_and_return_id_opt_lock(bytea, uuid, integer, text, integer);
DROP FUNCTION IF EXISTS insert_md_01_cart_aggregate_event_and_return_id_opt_lock(bytea, uuid, integer, text, integer);
DROP FUNCTION IF EXISTS insert_md_01_good_aggregate_event_and_return_id_opt_lock2(bytea, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_cart_aggregate_event_and_return_id_opt_lock2(bytea, uuid, integer, text, integer, text[], integer[], uuid[]);
DROP FUNCTION IF EXISTS insert_md_01_goodscontainer_event_and_return_id(bytea, text);
DROP FUNCTION IF EXISTS insert_md_01_goodsContainer_event_and_return_id(bytea, text);
DROP FUNCTION IF EXISTS insert_md_01_good_event_and_return_id(bytea, uuid, integer, text);
DROP FUNCTION IF EXISTS insert_md_01_cart_event_and_return_id(bytea, uuid, integer, text);

-- GoodsContainer context insert_md (4 params)
CREATE OR REPLACE FUNCTION insert_md_01_goodsContainer_event_and_return_id(
    IN event_in bytea, 
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
    INSERT INTO events_01_goodscontainer(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, now(), md_in, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Good insert_md event
CREATE OR REPLACE FUNCTION insert_md_01_good_event_and_return_id(
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
    INSERT INTO events_01_good(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Good opt_lock (7 params)
CREATE OR REPLACE FUNCTION insert_md_01_good_aggregate_event_and_return_id_opt_lock(
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
    PERFORM check_last_event_id_opt_lock('events_01_good', aggregate_id, last_event_id);
    INSERT INTO events_01_good(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_good(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Good opt_lock2 (10 params)
CREATE OR REPLACE FUNCTION insert_md_01_good_aggregate_event_and_return_id_opt_lock2(
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
    PERFORM check_last_event_id_opt_lock('events_01_good', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_good(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_good(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Cart insert_md event
CREATE OR REPLACE FUNCTION insert_md_01_cart_event_and_return_id(
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
    INSERT INTO events_01_cart(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Cart opt_lock (7 params)
CREATE OR REPLACE FUNCTION insert_md_01_cart_aggregate_event_and_return_id_opt_lock(
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
    PERFORM check_last_event_id_opt_lock('events_01_cart', aggregate_id, last_event_id);
    INSERT INTO events_01_cart(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_cart(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Cart opt_lock2 (10 params)
CREATE OR REPLACE FUNCTION insert_md_01_cart_aggregate_event_and_return_id_opt_lock2(
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
    PERFORM check_last_event_id_opt_lock('events_01_cart', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_cart(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_cart(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

-- Atomic patch for good
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_good(
    IN in_aggregate_id uuid,
    IN in_event_id integer,
    IN in_noop_payload bytea
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_good
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_good
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;

-- Atomic patch for cart
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_cart(
    IN in_aggregate_id uuid,
    IN in_event_id integer,
    IN in_noop_payload bytea
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_cart
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_cart
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;

-- Atomic patch for goodscontainer (context)
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_goodsContainer(
    IN in_event_id integer,
    IN in_noop_payload bytea
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_goodscontainer
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_01_goodscontainer
    WHERE event_id IS NOT NULL AND event_id >= in_event_id;
END;
$$;

-- migrate:down

