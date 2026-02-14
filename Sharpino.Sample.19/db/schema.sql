\restrict dyBBtnjkdAuDR0HDHKUj6BHOFpY8HfmTXkagZho1ZgoHQicHdAaUhB7DEnFxpHO

-- Dumped from database version 15.15 (Debian 15.15-1.pgdg13+1)
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
-- Name: insert_01_todo_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_todo_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_Todo_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_Todo(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_todo_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_todo_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_Todo(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_todo_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_todo_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_Todo_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_Todo(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_todo_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_todo_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_Todo(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: aggregate_events_01_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_todo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_todo (
    id integer DEFAULT nextval('public.aggregate_events_01_todo_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_todo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_todo (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
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
-- Name: schema_migrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.schema_migrations (
    version character varying(128) NOT NULL
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
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: aggregate_events_01_todo aggregate_events_01_todo_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_todo
    ADD CONSTRAINT aggregate_events_01_todo_event_id_key UNIQUE (event_id);


--
-- Name: aggregate_events_01_todo aggregate_events_01_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_todo
    ADD CONSTRAINT aggregate_events_01_todo_pkey PRIMARY KEY (id);


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
-- Name: snapshots_01_todo snapshots_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_todo
    ADD CONSTRAINT snapshots_todo_pkey PRIMARY KEY (id);


--
-- Name: ix_01_aggregate_events_todo_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_todo_id ON public.aggregate_events_01_todo USING btree (aggregate_id);


--
-- Name: ix_01_events_todo_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_todo_id ON public.events_01_todo USING btree (aggregate_id);


--
-- Name: ix_01_events_todo_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_todo_timestamp ON public.events_01_todo USING btree ("timestamp");


--
-- Name: ix_01_snapshot_todo_aggregate_id_and_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_todo_aggregate_id_and_id ON public.snapshots_01_todo USING btree (aggregate_id, id DESC);


--
-- Name: ix_01_snapshot_todo_event_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_todo_event_id ON public.snapshots_01_todo USING btree (event_id);


--
-- Name: ix_01_snapshot_todo_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_todo_id ON public.snapshots_01_todo USING btree (aggregate_id);


--
-- Name: ix_01_snapshots_todo_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_todo_timestamp ON public.snapshots_01_todo USING btree ("timestamp");


--
-- Name: aggregate_events_01_todo aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_todo
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_todo(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_todo event_01_todo_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_todo
    ADD CONSTRAINT event_01_todo_fk FOREIGN KEY (event_id) REFERENCES public.events_01_todo(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict dyBBtnjkdAuDR0HDHKUj6BHOFpY8HfmTXkagZho1ZgoHQicHdAaUhB7DEnFxpHO


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20260115115559'),
    ('20260115115952');
