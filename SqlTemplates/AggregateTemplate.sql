-- migrate:up

CREATE TABLE public.events{Version}{AggregateStorageName} (
                                          id integer NOT NULL,
                                          aggregate_id uuid NOT NULL,
                                          event {Format} NOT NULL,
                                          published boolean NOT NULL DEFAULT false,
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
                                                    event_id integer
);

ALTER TABLE ONLY public.aggregate_events{Version}{AggregateStorageName}
    ADD CONSTRAINT aggregate_events{Version}{AggregateStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.aggregate_events{Version}{AggregateStorageName}
    ADD CONSTRAINT aggregate_events{Version}_fk  FOREIGN KEY (event_id) REFERENCES public.events{Version}{AggregateStorageName} (id) MATCH FULL ON DELETE CASCADE;

CREATE OR REPLACE FUNCTION insert{Version}{AggregateStorageName}_event_and_return_id(
    IN event_in {Format},
    IN aggregate_id uuid
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events{Version}{AggregateStorageName}(event, aggregate_id, timestamp)
VALUES(event_in::{Format}, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md{Version}{AggregateStorageName}_event_and_return_id(
    IN event_in {Format},
    IN aggregate_id uuid,
    IN md text
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events{Version}{AggregateStorageName}(event, aggregate_id, timestamp, md)
VALUES(event_in::{Format}, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert{Version}{AggregateStorageName}_aggregate_event_and_return_id(
    IN event_in {Format},
    IN aggregate_id uuid 
)
RETURNS int
    
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert{Version}{AggregateStorageName}_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events{Version}{AggregateStorageName}(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


CREATE OR REPLACE FUNCTION insert_md{Version}{AggregateStorageName}_aggregate_event_and_return_id(
    IN event_in {Format},
    IN aggregate_id uuid,
    IN md text   
)
RETURNS int
    
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md{Version}{AggregateStorageName}_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events{Version}{AggregateStorageName}(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;

-- migrate:down
