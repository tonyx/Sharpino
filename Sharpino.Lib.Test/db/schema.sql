\restrict MrIBRcLmn3caG1ABLEx73gVoWzwjQ9CzD4FqjcGnn4vh2c16p1AFvhBaPxVOg6k

-- Dumped from database version 14.4
-- Dumped by pg_dump version 18.0

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
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--

-- *not* creating schema, since initdb creates it


--
-- Name: insert_01_samplecontext_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_samplecontext_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_sampleContext(event, timestamp)
VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_01_sampleobject_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_sampleobject_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_sampleObject_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_sampleobject_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_sampleobject_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_sampleObject(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_samplecontext_event_and_return_id(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_samplecontext_event_and_return_id(event_in text, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_sampleContext(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_md_01_sampleobject_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_sampleobject_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_sampleObject(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: aggregate_events_01_sampleobject_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_sampleobject_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_sampleobject; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_sampleobject (
    id integer DEFAULT nextval('public.aggregate_events_01_sampleobject_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_samplecontext; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_samplecontext (
    id integer NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_samplecontext_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_samplecontext ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_samplecontext_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_sampleobject; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_sampleobject (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_sampleobject_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_sampleobject ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_sampleobject_id_seq
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
-- Name: snapshots_01_samplecontext_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_samplecontext_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_samplecontext; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_samplecontext (
    id integer DEFAULT nextval('public.snapshots_01_samplecontext_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_01_sampleobject_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_sampleobject_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_sampleobject; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_sampleobject (
    id integer DEFAULT nextval('public.snapshots_01_sampleobject_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: aggregate_events_01_sampleobject aggregate_events_01_sampleobject_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_sampleobject
    ADD CONSTRAINT aggregate_events_01_sampleobject_pkey PRIMARY KEY (id);


--
-- Name: events_01_samplecontext events_01_samplecontext_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_samplecontext
    ADD CONSTRAINT events_01_samplecontext_pkey PRIMARY KEY (id);


--
-- Name: events_01_sampleobject events_sampleobject_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_sampleobject
    ADD CONSTRAINT events_sampleobject_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_samplecontext snapshots_01_samplecontext_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_samplecontext
    ADD CONSTRAINT snapshots_01_samplecontext_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_sampleobject snapshots_sampleobject_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_sampleobject
    ADD CONSTRAINT snapshots_sampleobject_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_sampleobject aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_sampleobject
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_sampleobject(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_samplecontext event_01_samplecontext_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_samplecontext
    ADD CONSTRAINT event_01_samplecontext_fk FOREIGN KEY (event_id) REFERENCES public.events_01_samplecontext(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_sampleobject event_01_sampleobject_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_sampleobject
    ADD CONSTRAINT event_01_sampleobject_fk FOREIGN KEY (event_id) REFERENCES public.events_01_sampleobject(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict MrIBRcLmn3caG1ABLEx73gVoWzwjQ9CzD4FqjcGnn4vh2c16p1AFvhBaPxVOg6k


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20250420134002'),
    ('20251208085305'),
    ('20251220135214');
