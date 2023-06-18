-- migrate:up

CREATE TABLE public.events_01_tags (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE public.events_01_tags ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots_01_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots_01_tags (
    id integer DEFAULT nextval('public.snapshots_01_tags_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE ONLY public.events_01_tags
    ADD CONSTRAINT events_tags_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_tags
    ADD CONSTRAINT snapshots_tags_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_tags
    ADD CONSTRAINT event_01_tags_fk FOREIGN KEY (event_id) REFERENCES public.events_01_tags(id) MATCH FULL ON DELETE CASCADE;

-- for developing and testing is ok to grant ALL on user 'events'
-- however, in production we may prefer to grant only the necessary permissions (write)

GRANT ALL ON TABLE public.events_01_tags TO safe;
GRANT ALL ON TABLE public.snapshots_01_tags TO safe;
GRANT ALL ON SEQUENCE public.snapshots_01_tags_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_01_tags_id_seq TO safe;


-- migrate:down

