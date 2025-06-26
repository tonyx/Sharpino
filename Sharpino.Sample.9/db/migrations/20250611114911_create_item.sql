-- migrate:up

CREATE TABLE public.events_01_item (
                                       id integer NOT NULL,
                                       aggregate_id uuid NOT NULL,
                                       event text NOT NULL,
                                       published boolean NOT NULL DEFAULT false,
                                       "timestamp" timestamp without time zone NOT NULL,
                                       md text
);

ALTER TABLE public.events_01_item ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots_01_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots_01_item (
                                          id integer DEFAULT nextval('public.snapshots_01_item_id_seq'::regclass) NOT NULL,
                                          snapshot text NOT NULL,
                                          event_id integer, -- the initial snapshot has no event_id associated so it can be null
                                          aggregate_id uuid NOT NULL,
                                          "timestamp" timestamp without time zone NOT NULL,
                                          is_deleted boolean NOT NULL DEFAULT false
);

ALTER TABLE ONLY public.events_01_item
    ADD CONSTRAINT events_item_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_item
    ADD CONSTRAINT snapshots_item_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_item
    ADD CONSTRAINT event_01_item_fk FOREIGN KEY (event_id) REFERENCES public.events_01_item (id) MATCH FULL ON DELETE CASCADE;

CREATE SEQUENCE public.aggregate_events_01_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.aggregate_events_01_item (
                                                 id integer DEFAULT nextval('public.aggregate_events_01_item_id_seq') NOT NULL,
                                                 aggregate_id uuid NOT NULL,
                                                 event_id integer
);

ALTER TABLE ONLY public.aggregate_events_01_item
    ADD CONSTRAINT aggregate_events_01_item_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.aggregate_events_01_item
    ADD CONSTRAINT aggregate_events_01_fk  FOREIGN KEY (event_id) REFERENCES public.events_01_item (id) MATCH FULL ON DELETE CASCADE;

CREATE OR REPLACE FUNCTION insert_01_item_event_and_return_id(
    IN event_in text,
    IN aggregate_id uuid
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_item(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_md_01_item_event_and_return_id(
    IN event_in text,
    IN aggregate_id uuid,
    IN md text
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_item(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_01_item_aggregate_event_and_return_id(
    IN event_in text,
    IN aggregate_id uuid 
)
RETURNS int
    
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_item_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_item(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


CREATE OR REPLACE FUNCTION insert_md_01_item_aggregate_event_and_return_id(
    IN event_in text,
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
    event_id := insert_md_01_item_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_item(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;

CREATE OR REPLACE FUNCTION insert_enhanced_01_item_aggregate_event_and_return_id(
       IN event_in text,
       IN last_event_id integer,
       IN p_aggregate_id uuid,
       IN md text
   )
RETURNS int
LANGUAGE plpgsql      
AS $$       

DECLARE
    inserted_id integer;
    event_id integer;
    max_id integer := (SELECT MAX(id) FROM events_01_item WHERE aggregate_id = p_aggregate_id);
           
BEGIN 
IF (max_id = last_event_id or (last_event_id = 0 and max_id is null)) THEN
 event_id := insert_md_01_item_event_and_return_id(event_in, p_aggregate_id, md);
 INSERT INTO aggregate_events_01_item(aggregate_id, event_id)       
 VALUES(p_aggregate_id, event_id);
END IF;        

return event_id;

COMMIT;
END;

$$;

-- migrate:down

