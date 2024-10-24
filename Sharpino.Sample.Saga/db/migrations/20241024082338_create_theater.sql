-- migrate:up
CREATE TABLE public.events_01_theater (
                                          id integer NOT NULL,
                                          event text NOT NULL,
                                          published boolean NOT NULL DEFAULT false,
                                          "timestamp" timestamp without time zone NOT NULL,
                                          md text
);

ALTER TABLE public.events_01_theater ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_theater_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots_01_theater_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots_01_theater (
                                             id integer DEFAULT nextval('public.snapshots_01_theater_id_seq'::regclass) NOT NULL,
                                             snapshot text NOT NULL,
                                             event_id integer NOT NULL,
                                             "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE ONLY public.events_01_theater
    ADD CONSTRAINT events_theater_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_theater
    ADD CONSTRAINT snapshots_theater_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_theater
    ADD CONSTRAINT event_01_theater_fk FOREIGN KEY (event_id) REFERENCES public.events_01_theater(id) MATCH FULL ON DELETE CASCADE;


CREATE OR REPLACE FUNCTION insert_01_theater_event_and_return_id(
    IN event_in text
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_theater(event, timestamp)
VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;

CREATE OR REPLACE FUNCTION insert_md_01_theater_event_and_return_id(
    IN event_in text, md_in text
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_theater(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;

-- migrate:down

