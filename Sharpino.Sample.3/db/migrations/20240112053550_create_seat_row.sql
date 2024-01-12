-- migrate:up

CREATE TABLE public.events_01_seatrow (
                                          id integer NOT NULL,
                                          event json NOT NULL,
                                          published boolean NOT NULL DEFAULT false,
                                          "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE public.events_01_seatrow ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_seatrow_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots_01_seatrow_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots_01_seatrow (
                                             id integer DEFAULT nextval('public.snapshots_01_seatrow_id_seq'::regclass) NOT NULL,
                                             snapshot json NOT NULL,
                                             event_id integer NOT NULL,
                                             kafkaoffset BIGINT,
                                             kafkapartition INTEGER,
                                             "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE ONLY public.events_01_seatrow
    ADD CONSTRAINT events_seat_row_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_seatrow
    ADD CONSTRAINT snapshots_tags_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_seatrow
    ADD CONSTRAINT event_01_seatrow_fk FOREIGN KEY (event_id) REFERENCES public.events_01_seatrow(id) MATCH FULL ON DELETE CASCADE;

                                                                                                                           
CREATE OR REPLACE FUNCTION insert_01_seatrow_event_and_return_id(
    IN event_in TEXT
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_seatrow(event, timestamp)
VALUES(event_in::JSON, now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$
                                                                                                                           
-- migrate:down
