\restrict U5umMFMKplO20XTOdfxFdgE0vLE8csKtL8GWtlI9JAgaYYZ2NdL5d03ykayNk8d

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
    lock_key bigint;
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
        -- Acquire a per-aggregate advisory lock for the duration of this transaction.
        -- This prevents concurrent writes to the same aggregate from interleaving
        -- between our SELECT (below) and the INSERT performed by the caller.
        -- pg_advisory_xact_lock is released automatically on COMMIT / ROLLBACK.
        lock_key := hashtext(full_stream_name || '|' || target_aggregate_id::text);
        PERFORM pg_advisory_xact_lock(lock_key);

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
-- Name: insert_md_01_tags_event_and_return_id(text, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_tags_event_and_return_id(event_in text, md_in text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_tags(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_md_01_todo_event_and_return_id(text, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_todo_event_and_return_id(event_in text, md_in text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_md_02_categories_event_and_return_id(text, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_02_categories_event_and_return_id(event_in text, md_in text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_categories(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_md_02_todo_event_and_return_id(text, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_02_todo_event_and_return_id(event_in text, md_in text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_todo(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__01_tags(integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__01_tags(in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_tags
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_01_tags
    WHERE event_id >= in_event_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__01_todo(integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__01_todo(in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_todo
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_01_todo
    WHERE event_id >= in_event_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__02_categories(integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__02_categories(in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_02_categories
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_02_categories
    WHERE event_id >= in_event_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__02_todo(integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__02_todo(in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_02_todo
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_02_todo
    WHERE event_id >= in_event_id;
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
    md text,
    correlation_id uuid,
    causation_id uuid
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
    md text,
    correlation_id uuid,
    causation_id uuid
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
    md text,
    correlation_id uuid,
    causation_id uuid
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
    md text,
    correlation_id uuid,
    causation_id uuid
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
-- Name: ix_01_events_tags_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_tags_causation_id ON public.events_01_tags USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_tags_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_tags_correlation_id ON public.events_01_tags USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_01_events_todo_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_todo_causation_id ON public.events_01_todo USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_todo_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_todo_correlation_id ON public.events_01_todo USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_02_events_categories_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_02_events_categories_causation_id ON public.events_02_categories USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_02_events_categories_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_02_events_categories_correlation_id ON public.events_02_categories USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_02_events_todo_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_02_events_todo_causation_id ON public.events_02_todo USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_02_events_todo_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_02_events_todo_correlation_id ON public.events_02_todo USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


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

\unrestrict U5umMFMKplO20XTOdfxFdgE0vLE8csKtL8GWtlI9JAgaYYZ2NdL5d03ykayNk8d


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20230618084021'),
    ('20230618084147'),
    ('20230618084416'),
    ('20230618084628'),
    ('20260629160000'),
    ('20260801200000'),
    ('20261221100000');
