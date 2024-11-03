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


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20230618084021'),
    ('20230618084147'),
    ('20230618084416'),
    ('20230618084628');
