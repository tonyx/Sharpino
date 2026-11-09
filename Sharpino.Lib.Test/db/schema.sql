\restrict m9SJR5Rx79zNyuLTqEQiwwfmats3spgDpbyUiDP0LFdZAdLou6aunAXzAxRr3R5

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
-- Name: insert_md_01_samplecontext_event_and_return_id(text, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_samplecontext_event_and_return_id(event_in text, md_in text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_sampleContext(event, timestamp, md, correlation_id, causation_id)
VALUES(event_in::text, now(), md_in, correlation_id, causation_id) RETURNING id INTO inserted_id;
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
-- Name: insert_md_01_sampleobject_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(text, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_sampleObject
    WHERE events_01_sampleObject.aggregate_id = insert_md_01_sampleObject_aggregate_event_and_return_id_opt_lock.aggregate_id
    ORDER BY id DESC LIMIT 1;

    IF last_event_id = 0 THEN
        IF found_last_event_id IS NOT NULL THEN
            RAISE EXCEPTION 'Optimistic locking check failed: expected no previous events, but found event %', found_last_event_id;
        END IF;
    ELSIF last_event_id > 0 THEN
        IF found_last_event_id IS NULL OR found_last_event_id <> last_event_id THEN
            RAISE EXCEPTION 'Optimistic locking check failed: expected last event id %, but found %', last_event_id, found_last_event_id;
        END IF;
    END IF;

    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(text, uuid, integer, text, integer, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_sampleObject
    WHERE events_01_sampleObject.aggregate_id = insert_md_01_sampleObject_aggregate_event_and_return_id_opt_lock.aggregate_id
    ORDER BY id DESC LIMIT 1;

    IF last_event_id = 0 THEN
        IF found_last_event_id IS NOT NULL THEN
            RAISE EXCEPTION 'Optimistic locking check failed: expected no previous events, but found event %', found_last_event_id;
        END IF;
    ELSIF last_event_id > 0 THEN
        IF found_last_event_id IS NULL OR found_last_event_id <> last_event_id THEN
            RAISE EXCEPTION 'Optimistic locking check failed: expected last event id %, but found %', last_event_id, found_last_event_id;
        END IF;
    END IF;

    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md, correlation_id, causation_id);

    INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_sampleObject', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_sampleObject(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(text, uuid, integer, text, integer, text[], integer[], uuid[], uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_aggregate_event_and_return_id_opt_loc(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[], correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_sampleObject', aggregate_id, last_event_id);

    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_sampleObject_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md, correlation_id, causation_id);

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
-- Name: insert_md_01_sampleobject_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_sampleObject(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_sampleobject_event_and_return_id(text, uuid, integer, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_sampleobject_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_sampleObject(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots_01_samplecontext(integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots_01_samplecontext(target_event_id integer, noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_sampleContext
    SET event = noop_payload
    WHERE id = target_event_id;

    DELETE FROM snapshots_01_sampleContext
    WHERE event_id >= target_event_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots_01_sampleobject(uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots_01_sampleobject(target_aggregate_id uuid, target_event_id integer, noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_sampleObject
    SET event = noop_payload
    WHERE aggregate_id = target_aggregate_id AND id = target_event_id;

    DELETE FROM snapshots_01_sampleObject
    WHERE aggregate_id = target_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= target_event_id;
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
    md text,
    correlation_id uuid,
    causation_id uuid
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
    md text,
    distance_from_latest_snapshot integer,
    correlation_id uuid,
    causation_id uuid
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
-- Name: ix_01_events_samplecontext_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_samplecontext_causation_id ON public.events_01_samplecontext USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_samplecontext_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_samplecontext_correlation_id ON public.events_01_samplecontext USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_01_events_sampleobject_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_sampleobject_causation_id ON public.events_01_sampleobject USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_sampleobject_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_sampleobject_correlation_id ON public.events_01_sampleobject USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


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

\unrestrict m9SJR5Rx79zNyuLTqEQiwwfmats3spgDpbyUiDP0LFdZAdLou6aunAXzAxRr3R5


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20250420134002'),
    ('20251208085305'),
    ('20260307161019'),
    ('20260529160000'),
    ('20260629160000'),
    ('20260629170000'),
    ('20260801200000'),
    ('20261220135214'),
    ('20261221100000');
