-- migrate:up

CREATE TABLE public.events{Version}{AggregateStorageName} (
                                          id integer NOT NULL,
                                          aggregate_id uuid NOT NULL,
                                          event {Format} NOT NULL,
                                          correlation_id uuid,
                                          causation_id uuid,
                                          published boolean NOT NULL DEFAULT false,
                                          distance_from_latest_snapshot int,
                                          "timestamp" timestamp without time zone NOT NULL,
                                          md text 
);

ALTER TABLE public.events{Version}{AggregateStorageName} ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events{Version}{AggregateStorageName}_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots{Version}{AggregateStorageName}_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots{Version}{AggregateStorageName} (
                                             id integer DEFAULT nextval('public.snapshots{Version}{AggregateStorageName}_id_seq'::regclass) NOT NULL,
                                             snapshot {Format} NOT NULL,
                                             event_id integer, -- the initial snapshot has no event_id associated so it can be null
                                             aggregate_id uuid NOT NULL,
                                             "timestamp" timestamp without time zone NOT NULL,
                                             is_deleted boolean NOT NULL DEFAULT false
);

ALTER TABLE ONLY public.events{Version}{AggregateStorageName}
    ADD CONSTRAINT events{AggregateStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots{Version}{AggregateStorageName}
    ADD CONSTRAINT snapshots{AggregateStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots{Version}{AggregateStorageName}
    ADD CONSTRAINT event{Version}{AggregateStorageName}_fk FOREIGN KEY (event_id) REFERENCES public.events{Version}{AggregateStorageName} (id) MATCH FULL ON DELETE CASCADE;

CREATE SEQUENCE public.aggregate_events{Version}{AggregateStorageName}_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.aggregate_events{Version}{AggregateStorageName} (
                                                    id integer DEFAULT nextval('public.aggregate_events{Version}{AggregateStorageName}_id_seq') NOT NULL,
                                                    aggregate_id uuid NOT NULL,
                                                    event_id integer UNIQUE
);

ALTER TABLE ONLY public.aggregate_events{Version}{AggregateStorageName}
    ADD CONSTRAINT aggregate_events{Version}{AggregateStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.aggregate_events{Version}{AggregateStorageName}
    ADD CONSTRAINT aggregate_events{Version}_fk  FOREIGN KEY (event_id) REFERENCES public.events{Version}{AggregateStorageName} (id) MATCH FULL ON DELETE CASCADE;

create index ix{Version}_events{AggregateStorageName}_id on public.events{Version}{AggregateStorageName}(aggregate_id);
create index ix{Version}_events{AggregateStorageName}_correlation_id on public.events{Version}{AggregateStorageName}(correlation_id) where correlation_id is not null;
create index ix{Version}_events{AggregateStorageName}_causation_id on public.events{Version}{AggregateStorageName}(causation_id) where causation_id is not null;
create index ix{Version}_aggregate_events{AggregateStorageName}_id on public.aggregate_events{Version}{AggregateStorageName}(aggregate_id);
create index ix{Version}_snapshot{AggregateStorageName}_id on public.snapshots{Version}{AggregateStorageName}(aggregate_id);
create index ix{Version}_snapshot{AggregateStorageName}_aggregate_id_and_id on public.snapshots{Version}{AggregateStorageName}(aggregate_id, id DESC);
create index ix{Version}_snapshot{AggregateStorageName}_event_id on public.snapshots{Version}{AggregateStorageName}(event_id);
create index ix{Version}_events{AggregateStorageName}_timestamp on public.events{Version}{AggregateStorageName}("timestamp");
create index ix{Version}_snapshots{AggregateStorageName}_timestamp on public.snapshots{Version}{AggregateStorageName}("timestamp");
                                                                                                                                                          
CREATE OR REPLACE FUNCTION insert_md{Version}{AggregateStorageName}_event_and_return_id(
    IN event_in {Format},
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
INSERT INTO events{Version}{AggregateStorageName}(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
VALUES(event_in::{Format}, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id_opt_lock(
    IN event_in {Format},
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
    FROM events{Version}{AggregateStorageName} 
    WHERE events{Version}{AggregateStorageName}.aggregate_id = insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id_opt_lock.aggregate_id 
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

    event_id := insert_md{Version}{AggregateStorageName}_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md, correlation_id, causation_id);

    INSERT INTO aggregate_events{Version}{AggregateStorageName}(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id_opt_lock2(
    IN event_in {Format},
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
    -- Perform the main optimistic locking check for the aggregate itself
    PERFORM check_last_event_id_opt_lock('events{Version}{AggregateStorageName}', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md{Version}{AggregateStorageName}_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md, correlation_id, causation_id);

    INSERT INTO aggregate_events{Version}{AggregateStorageName}(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;

CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots_{Version}{AggregateStorageName}(
    IN target_aggregate_id uuid,
    IN target_event_id int,
    IN noop_payload {Format}
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    -- 1. Replace the corrupted event payload
    UPDATE events{Version}{AggregateStorageName}
    SET event = noop_payload
    WHERE aggregate_id = target_aggregate_id AND id = target_event_id;

    -- 2. Drop all subsequent snapshots for this aggregate
    DELETE FROM snapshots{Version}{AggregateStorageName}
    WHERE aggregate_id = target_aggregate_id AND (event_id >= target_event_id OR event_id IS NULL);
END;
$$;

-- migrate:down
