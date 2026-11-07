-- migrate:up

CREATE TABLE public.events{Version}{ContextStorageName} (
                                          id integer NOT NULL,
                                          event {Format} NOT NULL,
                                          correlation_id uuid,
                                          causation_id uuid,
                                          published boolean NOT NULL DEFAULT false,
                                          "timestamp" timestamp without time zone NOT NULL,
                                          md text 
);

ALTER TABLE public.events{Version}{ContextStorageName} ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events{Version}{ContextStorageName}_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots{Version}{ContextStorageName}_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots{Version}{ContextStorageName} (
                                             id integer DEFAULT nextval('public.snapshots{Version}{ContextStorageName}_id_seq'::regclass) NOT NULL,
                                             snapshot {Format} NOT NULL,
                                             event_id integer NOT NULL,
                                             "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE ONLY public.events{Version}{ContextStorageName}
    ADD CONSTRAINT events{Version}{ContextStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots{Version}{ContextStorageName}
    ADD CONSTRAINT snapshots{Version}{ContextStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots{Version}{ContextStorageName}
    ADD CONSTRAINT event{Version}{ContextStorageName}_fk FOREIGN KEY (event_id) REFERENCES public.events{Version}{ContextStorageName}(id) MATCH FULL ON DELETE CASCADE;

create index ix{Version}_events{ContextStorageName}_correlation_id on public.events{Version}{ContextStorageName}(correlation_id) where correlation_id is not null;
create index ix{Version}_events{ContextStorageName}_causation_id on public.events{Version}{ContextStorageName}(causation_id) where causation_id is not null;

CREATE OR REPLACE FUNCTION insert_md{Version}{ContextStorageName}_event_and_return_id(
    IN event_in {Format}, 
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
VALUES(event_in::{Format}, now(), md_in, correlation_id, causation_id) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots_{Version}{ContextStorageName}(
    IN target_event_id int,
    IN noop_payload {Format}
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

-- migrate:down
