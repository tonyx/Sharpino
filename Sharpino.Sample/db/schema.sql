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
-- Name: insert_01_tags_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_tags_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_tags(event, timestamp)
    -- VALUES(event_in::text, (now() at time zone 'utc')) RETURNING id INTO inserted_id;
    VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_01_tags_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_tags_event_and_return_id(event_in text, context_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_tags(event, timestamp, context_state_id)
    -- VALUES(event_in::text, (now() at time zone 'utc'), context_state_id) RETURNING id INTO inserted_id;
    VALUES(event_in::text, now(), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_01_todo_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_todo_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp)
    -- VALUES(event_in::text, (now() at time zone 'utc')) RETURNING id INTO inserted_id;
    VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_01_todo_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_todo_event_and_return_id(event_in text, context_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp, context_state_id)
    -- VALUES(event_in::text, (now() at time zone 'utc'), context_state_id) RETURNING id INTO inserted_id;
    VALUES(event_in::text, now(), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_02_categories_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_02_categories_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_categories(event, timestamp)
    -- VALUES(event_in::text, (now() at time zone 'utc')) RETURNING id INTO inserted_id;
    VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_02_categories_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_02_categories_event_and_return_id(event_in text, context_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_categories(event, timestamp, context_state_id)
    VALUES(event_in::text, (now() at time zone 'utc'), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_02_todo_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_02_todo_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_todo(event, timestamp)
    VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_02_todo_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_02_todo_event_and_return_id(event_in text, context_state_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_todo(event, timestamp, context_state_id)
    VALUES(event_in::text, (now() at time zone 'utc'), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp)
    -- VALUES(event_in, (now() at time zone 'utc')) RETURNING id INTO inserted_id;
    VALUES(event_in, now()) RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: set_classic_optimistic_lock_01_categories(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_01_categories()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events_01_categories_context_state_id_unique') THEN
ALTER TABLE events_01_categories
    ADD CONSTRAINT context_events_01_categories_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$;


--
-- Name: set_classic_optimistic_lock_01_tags(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_01_tags()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events_01_tags_context_state_id_unique') THEN
ALTER TABLE events_01_tags
    ADD CONSTRAINT context_events_01_tags_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$;


--
-- Name: set_classic_optimistic_lock_01_todo(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_01_todo()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events_01_todo_context_state_id_unique') THEN
ALTER TABLE events_01_todo
    ADD CONSTRAINT context_events_01_todo_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$;


--
-- Name: set_classic_optimistic_lock_02_todo(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.set_classic_optimistic_lock_02_todo()
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events_02_todo_context_state_id_unique') THEN
ALTER TABLE events_02_todo
    ADD CONSTRAINT context_events_02_todo_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_01_categories(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_01_categories()
    LANGUAGE plpgsql
    AS $$
BEGIN
ALTER TABLE events_01_categories
DROP CONSTRAINT IF EXISTS context_events_01_categories_context_state_id_unique;
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_01_tags(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_01_tags()
    LANGUAGE plpgsql
    AS $$
BEGIN
ALTER TABLE events_01_tags
DROP CONSTRAINT IF EXISTS context_events_01_tags_context_state_id_unique;
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_01_todo(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_01_todo()
    LANGUAGE plpgsql
    AS $$
BEGIN
ALTER TABLE events_01_todo
DROP CONSTRAINT IF EXISTS context_events_01_todo_context_state_id_unique;
END;
$$;


--
-- Name: un_set_classic_optimistic_lock_02_todo(); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.un_set_classic_optimistic_lock_02_todo()
    LANGUAGE plpgsql
    AS $$
BEGIN
    ALTER TABLE events_02_todo
    DROP CONSTRAINT IF EXISTS context_events_02_todo_context_state_id_unique;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: events_01_tags; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_tags (
    id integer NOT NULL,
    event text NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    context_state_id uuid
);


--
-- Name: events_01_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_tags ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_todo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_todo (
    id integer NOT NULL,
    event text NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    context_state_id uuid
);


--
-- Name: events_01_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_todo ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_02_categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_02_categories (
    id integer NOT NULL,
    event text NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    context_state_id uuid
);


--
-- Name: events_02_categories_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_02_categories ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_02_categories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_02_todo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_02_todo (
    id integer NOT NULL,
    event text NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    published boolean DEFAULT false NOT NULL,
    kafkaoffset bigint,
    kafkapartition integer,
    context_state_id uuid
);


--
-- Name: events_02_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_02_todo ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_02_todo_id_seq
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
-- Name: snapshots_01_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_tags; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_tags (
    id integer DEFAULT nextval('public.snapshots_01_tags_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_01_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_todo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_todo (
    id integer DEFAULT nextval('public.snapshots_01_todo_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_02_categories_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_02_categories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_02_categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_02_categories (
    id integer DEFAULT nextval('public.snapshots_02_categories_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_02_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_02_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_02_todo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_02_todo (
    id integer DEFAULT nextval('public.snapshots_02_todo_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: events_01_todo events_01_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_todo
    ADD CONSTRAINT events_01_todo_pkey PRIMARY KEY (id);


--
-- Name: events_02_categories events_02_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_02_categories
    ADD CONSTRAINT events_02_categories_pkey PRIMARY KEY (id);


--
-- Name: events_02_todo events_02_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_02_todo
    ADD CONSTRAINT events_02_todo_pkey PRIMARY KEY (id);


--
-- Name: events_01_tags events_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_tags
    ADD CONSTRAINT events_tags_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_todo snapshots_01_todos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_todo
    ADD CONSTRAINT snapshots_01_todos_pkey PRIMARY KEY (id);


--
-- Name: snapshots_02_categories snapshots_02_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_02_categories
    ADD CONSTRAINT snapshots_02_categories_pkey PRIMARY KEY (id);


--
-- Name: snapshots_02_todo snapshots_02_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_02_todo
    ADD CONSTRAINT snapshots_02_todo_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_tags snapshots_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_tags
    ADD CONSTRAINT snapshots_tags_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_tags event_01_tags_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_tags
    ADD CONSTRAINT event_01_tags_fk FOREIGN KEY (event_id) REFERENCES public.events_01_tags(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_todo event_01_todo_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_todo
    ADD CONSTRAINT event_01_todo_fk FOREIGN KEY (event_id) REFERENCES public.events_01_todo(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_02_categories event_02_categories_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_02_categories
    ADD CONSTRAINT event_02_categories_fk FOREIGN KEY (event_id) REFERENCES public.events_02_categories(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_02_todo event_02_todo_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_02_todo
    ADD CONSTRAINT event_02_todo_fk FOREIGN KEY (event_id) REFERENCES public.events_02_todo(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20230618084021'),
    ('20230618084147'),
    ('20230618084416'),
    ('20230618084628'),
    ('20231023130328'),
    ('20231023130943'),
    ('20231023131031'),
    ('20231023131113'),
    ('20231029111640'),
    ('20231029143915'),
    ('20231029144006'),
    ('20231029144032'),
    ('20231029144106'),
    ('20231214160437'),
    ('20231214160632'),
    ('20231214162357'),
    ('20231214162513'),
    ('20231216070623'),
    ('20231216070924'),
    ('20231216071037'),
    ('20231216071515'),
    ('20240311163944'),
    ('20240311164334'),
    ('20240311164540'),
    ('20240311164703');
