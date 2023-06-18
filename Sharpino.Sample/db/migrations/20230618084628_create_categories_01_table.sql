-- migrate:up

CREATE TABLE public.events_02_categories (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE public.events_02_categories ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_02_categories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots_02_categories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots_02_categories (
    id integer DEFAULT nextval('public.snapshots_02_categories_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE ONLY public.events_02_categories
    ADD CONSTRAINT events_02_categories_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_02_categories
    ADD CONSTRAINT snapshots_02_categories_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_02_categories
    ADD CONSTRAINT event_02_categories_fk FOREIGN KEY (event_id) REFERENCES public.events_02_categories(id) MATCH FULL ON DELETE CASCADE;

-- for developing and testing is ok to grant ALL on user 'events'
-- however, in production we may prefer to grant only the necessary permissions (write)

GRANT ALL ON TABLE public.events_02_categories TO safe;
GRANT ALL ON TABLE public.snapshots_02_categories TO safe;
GRANT ALL ON SEQUENCE public.snapshots_02_categories_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_02_categories_id_seq TO safe;


-- migrate:down

