SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--

-- *not* creating schema, since initdb creates it


--
-- Name: insert_01_dish_aggregate_event_and_return_id(text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_dish_aggregate_event_and_return_id(event_in text, aggregate_id uuid, aggregate_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_dish_event_and_return_id(event_in, aggregate_id, aggregate_state_id);

INSERT INTO aggregate_events_01_dish(aggregate_id, event_id, aggregate_state_id )
VALUES(aggregate_id, event_id, aggregate_state_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_dish_event_and_return_id(text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_dish_event_and_return_id(event_in text, aggregate_id uuid, aggregate_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_dish(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id, now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_ingredient_aggregate_event_and_return_id(text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_ingredient_aggregate_event_and_return_id(event_in text, aggregate_id uuid, aggregate_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_ingredient_event_and_return_id(event_in, aggregate_id, aggregate_state_id);

INSERT INTO aggregate_events_01_ingredient(aggregate_id, event_id, aggregate_state_id )
VALUES(aggregate_id, event_id, aggregate_state_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_ingredient_event_and_return_id(text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_ingredient_event_and_return_id(event_in text, aggregate_id uuid, aggregate_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_ingredient(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id, now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_kitchen_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_kitchen_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
    INSERT INTO events_01_kitchen(event, timestamp)
    VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
    return inserted_id;

END;
$$;


--
-- Name: insert_01_kitchen_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_kitchen_event_and_return_id(event_in text, context_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_kitchen(event, timestamp, context_state_id)
    VALUES(event_in::text, now(), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_01_supplier_aggregate_event_and_return_id(text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_supplier_aggregate_event_and_return_id(event_in text, aggregate_id uuid, aggregate_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_supplier_event_and_return_id(event_in, aggregate_id, aggregate_state_id);

INSERT INTO aggregate_events_01_supplier(aggregate_id, event_id, aggregate_state_id )
VALUES(aggregate_id, event_id, aggregate_state_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_supplier_event_and_return_id(text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_supplier_event_and_return_id(event_in text, aggregate_id uuid, aggregate_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_supplier(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id, now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: set_classic_optimistic_lock_01_dish(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_01_dish()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'aggregate_events_01_dish_aggregate_id_state_id_unique') THEN
ALTER TABLE aggregate_events_01_dish
    ADD CONSTRAINT aggregate_events_01_dish_aggregate_id_state_id_unique UNIQUE (aggregate_state_id);
END IF;
END;
$$;


--
-- Name: set_classic_optimistic_lock_01_ingredient(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_01_ingredient()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'aggregate_events_01_ingredient_aggregate_id_state_id_unique') THEN
ALTER TABLE aggregate_events_01_ingredient
    ADD CONSTRAINT aggregate_events_01_ingredient_aggregate_id_state_id_unique UNIQUE (aggregate_state_id);
END IF;
END;
$$;


--
-- Name: set_classic_optimistic_lock_01_kitchen(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_01_kitchen()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events_01_kitchen_context_state_id_unique') THEN
ALTER TABLE aggregate_events_01_kitchen
    ADD CONSTRAINT context_events_01_kitchen_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$;


--
-- Name: set_classic_optimistic_lock_01_supplier(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_01_supplier()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'aggregate_events_01_supplier_aggregate_id_state_id_unique') THEN
ALTER TABLE aggregate_events_01_supplier
    ADD CONSTRAINT aggregate_events_01_supplier_aggregate_id_state_id_unique UNIQUE (aggregate_state_id);
END IF;
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_01_dish(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_01_dish()
    LANGUAGE plpgsql
    AS $$
BEGIN
ALTER TABLE aggregate_events_01_dish
DROP CONSTRAINT IF EXISTS aggregate_events_01_dish_aggregate_id_state_id_unique;
    -- You can have more SQL statements as needed
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_01_ingredient(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_01_ingredient()
    LANGUAGE plpgsql
    AS $$
BEGIN
ALTER TABLE aggregate_events_01_ingredient
DROP CONSTRAINT IF EXISTS aggregate_events_01_ingredient_aggregate_id_state_id_unique;
    -- You can have more SQL statements as needed
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_01_kitchen(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_01_kitchen()
    LANGUAGE plpgsql
    AS $$
BEGIN
ALTER TABLE events_01_kitchen
DROP CONSTRAINT IF EXISTS context_events_01_kitchen_context_state_id_unique;
    -- You can have more SQL statements as needed
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_01_supplier(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_01_supplier()
    LANGUAGE plpgsql
    AS $$
BEGIN
ALTER TABLE aggregate_events_01_supplier
DROP CONSTRAINT IF EXISTS aggregate_events_01_supplier_aggregate_id_state_id_unique;
    -- You can have more SQL statements as needed
END;
$$;


--
-- Name: aggregate_events_01_dish_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_dish_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_dish; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_dish (
    id integer DEFAULT nextval('public.aggregate_events_01_dish_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    aggregate_state_id uuid,
    event_id integer
);


--
-- Name: aggregate_events_01_ingredient_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_ingredient_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_ingredient; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_ingredient (
    id integer DEFAULT nextval('public.aggregate_events_01_ingredient_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    aggregate_state_id uuid,
    event_id integer
);


--
-- Name: aggregate_events_01_supplier_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_supplier_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_supplier; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_supplier (
    id integer DEFAULT nextval('public.aggregate_events_01_supplier_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    aggregate_state_id uuid,
    event_id integer
);


--
-- Name: events_01_dish; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_dish (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: events_01_dish_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_dish ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_dish_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_ingredient; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_ingredient (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: events_01_ingredient_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_ingredient ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_ingredient_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_kitchen; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_kitchen (
    id integer NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    "timestamp" timestamp without time zone NOT NULL,
    context_state_id uuid
);


--
-- Name: events_01_kitchen_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_kitchen ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_kitchen_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_supplier; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_supplier (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: events_01_supplier_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_supplier ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_supplier_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: schema_migrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.schema_migrations (
    version character varying(128) NOT NULL
);


--
-- Name: snapshots_01_dish_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_dish_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_dish; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_dish (
    id integer DEFAULT nextval('public.snapshots_01_dish_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    aggregate_state_id uuid,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_01_ingredient_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_ingredient_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_ingredient; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_ingredient (
    id integer DEFAULT nextval('public.snapshots_01_ingredient_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    aggregate_state_id uuid,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_01_kitchen_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_kitchen_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_kitchen; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_kitchen (
    id integer DEFAULT nextval('public.snapshots_01_kitchen_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_01_supplier_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_supplier_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_supplier; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_supplier (
    id integer DEFAULT nextval('public.snapshots_01_supplier_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    aggregate_state_id uuid,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: aggregate_events_01_dish aggregate_events_01_dish_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_dish
    ADD CONSTRAINT aggregate_events_01_dish_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_ingredient aggregate_events_01_ingredient_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_ingredient
    ADD CONSTRAINT aggregate_events_01_ingredient_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_supplier aggregate_events_01_supplier_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_supplier
    ADD CONSTRAINT aggregate_events_01_supplier_pkey PRIMARY KEY (id);


--
-- Name: events_01_dish events_dish_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_dish
    ADD CONSTRAINT events_dish_pkey PRIMARY KEY (id);


--
-- Name: events_01_ingredient events_ingredient_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_ingredient
    ADD CONSTRAINT events_ingredient_pkey PRIMARY KEY (id);


--
-- Name: events_01_kitchen events_kitchen_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_kitchen
    ADD CONSTRAINT events_kitchen_pkey PRIMARY KEY (id);


--
-- Name: events_01_supplier events_supplier_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_supplier
    ADD CONSTRAINT events_supplier_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_dish snapshots_dish_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_dish
    ADD CONSTRAINT snapshots_dish_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_ingredient snapshots_ingredient_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_ingredient
    ADD CONSTRAINT snapshots_ingredient_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_kitchen snapshots_kitchen_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_kitchen
    ADD CONSTRAINT snapshots_kitchen_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_supplier snapshots_supplier_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_supplier
    ADD CONSTRAINT snapshots_supplier_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_ingredient aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_ingredient
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_ingredient(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_dish aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_dish
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_dish(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_supplier aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_supplier
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_supplier(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_dish event_01_dish_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_dish
    ADD CONSTRAINT event_01_dish_fk FOREIGN KEY (event_id) REFERENCES public.events_01_dish(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_ingredient event_01_ingredient_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_ingredient
    ADD CONSTRAINT event_01_ingredient_fk FOREIGN KEY (event_id) REFERENCES public.events_01_ingredient(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_kitchen event_01_kitchen_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_kitchen
    ADD CONSTRAINT event_01_kitchen_fk FOREIGN KEY (event_id) REFERENCES public.events_01_kitchen(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_supplier event_01_supplier_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_supplier
    ADD CONSTRAINT event_01_supplier_fk FOREIGN KEY (event_id) REFERENCES public.events_01_supplier(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20240304170702'),
    ('20240304171643'),
    ('20240304172119'),
    ('20240304172910'),
    ('20240309151856'),
    ('20240309152430'),
    ('20240311195419');
