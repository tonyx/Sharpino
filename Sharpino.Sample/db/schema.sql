\restrict sK2aiaOzgf3JWyb2tsMIt1ITog22LpZKYXt6j14Wy53Q7htS6VIoj77Ely0MMzU

-- Dumped from database version 17.9 (Homebrew)
-- Dumped by pg_dump version 17.9 (Homebrew)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: check_last_event_id_opt_lock(text, uuid, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.check_last_event_id_opt_lock(stream_name text, target_aggregate_id uuid, expected_last_event_id integer) RETURNS void
    LANGUAGE plpgsql
    AS $_$
DECLARE
    found_last_event_id integer;
    query text;
    full_stream_name text;
BEGIN
    full_stream_name := stream_name;
    IF NOT full_stream_name LIKE 'events_%' THEN
        IF full_stream_name LIKE '_%' THEN
            full_stream_name := 'events' || full_stream_name;
        ELSE
            full_stream_name := 'events_' || full_stream_name;
        END IF;
    END IF;

    -- If target_aggregate_id is null, try to resolve it from the expected_last_event_id
    IF target_aggregate_id IS NULL THEN
        query := format('SELECT aggregate_id FROM %I WHERE id = $1', full_stream_name);
        EXECUTE query INTO target_aggregate_id USING expected_last_event_id;
    END IF;

    IF target_aggregate_id IS NULL THEN
        IF expected_last_event_id > 0 THEN
            RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected event % not found to resolve aggregate', full_stream_name, expected_last_event_id;
        END IF;
    ELSE
        query := format('SELECT id FROM %I WHERE aggregate_id = $1 ORDER BY id DESC LIMIT 1', full_stream_name);
        EXECUTE query INTO found_last_event_id USING target_aggregate_id;

        IF expected_last_event_id = 0 THEN
            IF found_last_event_id IS NOT NULL THEN
                RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected no previous events, but found event %', full_stream_name, found_last_event_id;
            END IF;
        ELSIF expected_last_event_id > 0 THEN
            IF found_last_event_id IS NULL OR found_last_event_id <> expected_last_event_id THEN
                RAISE EXCEPTION 'Optimistic locking check failed for stream %: expected last event id %, but found %', full_stream_name, expected_last_event_id, found_last_event_id;
            END IF;
        END IF;
    END IF;
END;
$_$;


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
VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
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
VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
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
VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
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
-- Name: insert_md_01_tags_event_and_return_id(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_tags_event_and_return_id(event_in text, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_tags(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_md_01_todo_event_and_return_id(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_todo_event_and_return_id(event_in text, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_todo(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_md_02_categories_event_and_return_id(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_02_categories_event_and_return_id(event_in text, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_02_categories(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_md_02_todo_event_and_return_id(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_02_todo_event_and_return_id(event_in text, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_02_todo(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

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
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
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
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
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
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
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
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
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
-- Name: events_02_todo events_02_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_02_todo
    ADD CONSTRAINT events_02_todo_pkey PRIMARY KEY (id);


--
-- Name: events_02_categories events_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_02_categories
    ADD CONSTRAINT events_categories_pkey PRIMARY KEY (id);


--
-- Name: events_01_tags events_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_tags
    ADD CONSTRAINT events_tags_pkey PRIMARY KEY (id);


--
-- Name: events_01_todo events_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_todo
    ADD CONSTRAINT events_todo_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_02_todo snapshots_02_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_02_todo
    ADD CONSTRAINT snapshots_02_todo_pkey PRIMARY KEY (id);


--
-- Name: snapshots_02_categories snapshots_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_02_categories
    ADD CONSTRAINT snapshots_categories_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_tags snapshots_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_tags
    ADD CONSTRAINT snapshots_tags_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_todo snapshots_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_todo
    ADD CONSTRAINT snapshots_todo_pkey PRIMARY KEY (id);


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

\unrestrict sK2aiaOzgf3JWyb2tsMIt1ITog22LpZKYXt6j14Wy53Q7htS6VIoj77Ely0MMzU


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20230618084021'),
    ('20230618084147'),
    ('20230618084416'),
    ('20230618084628'),
    ('20260629160000');
