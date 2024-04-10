-- migrate:up

CREATE TABLE public.events{Version}{ContextStorageName} (
                                          id integer NOT NULL,
                                          event json NOT NULL,
                                          published boolean NOT NULL DEFAULT false,
                                          kafkaoffset BIGINT,
                                          kafkapartition INTEGER,
                                          "timestamp" timestamp without time zone NOT NULL
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
                                             snapshot json NOT NULL,
                                             event_id integer NOT NULL,
                                             "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE ONLY public.events{Version}{ContextStorageName}
    ADD CONSTRAINT events{ContextStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots{Version}{ContextStorageName}
    ADD CONSTRAINT snapshots{ContextStorageName}_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots{Version}{ContextStorageName}
    ADD CONSTRAINT event{Version}{ContextStorageName}_fk FOREIGN KEY (event_id) REFERENCES public.events{Version}{ContextStorageName}(id) MATCH FULL ON DELETE CASCADE;


CREATE OR REPLACE FUNCTION insert{Version}{ContextStorageName}_event_and_return_id(
    IN event_in TEXT,
    IN context_state_id uuid
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events{Version}{ContextStorageName}(event, timestamp, context_state_id)
    VALUES(event_in::JSON, now(), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id;

END;
$$;

CREATE OR REPLACE PROCEDURE set_classic_optimistic_lock{Version}{ContextStorageName}() AS $$
BEGIN 
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events{Version}{ContextStorageName}_context_state_id_unique') THEN
ALTER TABLE events{Version}{ContextStorageName}
    ADD CONSTRAINT context_events{Version}{ContextStorageName}_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE PROCEDURE un_set_classic_optimistic_lockcontext_events{Version}{ContextStorageName}() AS $$
BEGIN
    ALTER TABLE eventscontext_events{Version}{ContextStorageName}
    DROP CONSTRAINT IF EXISTS context_eventscontext_events{Version}{ContextStorageName}_context_state_id_unique; 
END;
$$ LANGUAGE plpgsql;

-- migrate:down

