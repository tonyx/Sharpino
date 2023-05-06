-- postgres script of creation of tables for Tags aggregate

-- I assume there is a 'events' user created as follows:
-- create user events with password 'events';
-- remember to configure the connection string in the Conf.fs accordingly
-- or just use the in memory event store for development and testing (Conf.fs -> storageType)

CREATE TABLE public.events_01_todos (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE public.events_01_todos ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_todos_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

CREATE SEQUENCE public.snapshots_01_todos_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;

CREATE TABLE public.snapshots_01_todos (
    id integer DEFAULT nextval('public.snapshots_01_todos_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);

ALTER TABLE ONLY public.events_01_todos
    ADD CONSTRAINT events_01_todos_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_todos
    ADD CONSTRAINT snapshots_01_todos_pkey PRIMARY KEY (id);

ALTER TABLE ONLY public.snapshots_01_todos
    ADD CONSTRAINT event_01_todos_fk FOREIGN KEY (event_id) REFERENCES public.events_01_tags(id) MATCH FULL ON DELETE CASCADE;

-- for developing and testing is ok to grant ALL on user 'events'
-- however, in production we may prefer to grant only the necessary permissions (write)

GRANT ALL ON TABLE public.events_01_todos TO events;
GRANT ALL ON TABLE public.snapshots_01_todos TO safe;
GRANT ALL ON SEQUENCE public.snapshots_01_todos_id_seq TO events;
GRANT ALL ON SEQUENCE public.events_todos_01_id_seq TO events;



