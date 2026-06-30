\restrict WliwXoJ9UTUFiHAP2LdmtFoUBzBmOzKkOEro5epPxarsHyy4OUwX2PpvEZFWduc

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
-- Name: insert_01_account_aggregate_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_account_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_account_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_account(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_account_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_account_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_account(event, aggregate_id, timestamp)
VALUES(event_in::bytea, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_counter_aggregate_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_counter_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_counter_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_counter(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_counter_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_counter_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_counter(event, aggregate_id, timestamp)
VALUES(event_in::bytea, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_account_aggregate_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_account_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_account_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_account(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_account_aggregate_event_and_return_id(bytea, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_account_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_account_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_account(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_account_aggregate_event_and_return_id_opt_lock(bytea, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_account_aggregate_event_and_return_id_opt_lock(event_in bytea, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_account
    WHERE events_01_account.aggregate_id = insert_md_01_account_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_account_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_account(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_account_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_account_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_account', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], NULL, extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_account_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_account(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_account_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_account_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_account(event, aggregate_id, timestamp, md)
VALUES(event_in::bytea, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_account_event_and_return_id(bytea, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_account_event_and_return_id(event_in bytea, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_account(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_counter_aggregate_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_counter_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_counter_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_counter(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_counter_aggregate_event_and_return_id(bytea, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_counter_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_counter_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_counter(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_counter_aggregate_event_and_return_id_opt_lock(bytea, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_counter_aggregate_event_and_return_id_opt_lock(event_in bytea, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_counter
    WHERE events_01_counter.aggregate_id = insert_md_01_counter_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_counter_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_counter(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_counter_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_counter_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_counter', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_counter_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_counter(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_counter_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_counter_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_counter(event, aggregate_id, timestamp, md)
VALUES(event_in::bytea, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_counter_event_and_return_id(bytea, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_counter_event_and_return_id(event_in bytea, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_counter(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::bytea, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: aggregate_events_01_account_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_account_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_account; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_account (
    id integer DEFAULT nextval('public.aggregate_events_01_account_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_counter_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_counter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_counter; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_counter (
    id integer DEFAULT nextval('public.aggregate_events_01_counter_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_account; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_account (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event bytea NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_account_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_account ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_account_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_counter; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_counter (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event bytea NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_counter_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_counter ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_counter_id_seq
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
-- Name: snapshots_01_account_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_account_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_account; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_account (
    id integer DEFAULT nextval('public.snapshots_01_account_id_seq'::regclass) NOT NULL,
    snapshot bytea NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_counter_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_counter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_counter; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_counter (
    id integer DEFAULT nextval('public.snapshots_01_counter_id_seq'::regclass) NOT NULL,
    snapshot bytea NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: aggregate_events_01_account aggregate_events_01_account_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_account
    ADD CONSTRAINT aggregate_events_01_account_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_counter aggregate_events_01_counter_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_counter
    ADD CONSTRAINT aggregate_events_01_counter_pkey PRIMARY KEY (id);


--
-- Name: events_01_account events_account_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_account
    ADD CONSTRAINT events_account_pkey PRIMARY KEY (id);


--
-- Name: events_01_counter events_counter_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_counter
    ADD CONSTRAINT events_counter_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_account snapshots_account_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_account
    ADD CONSTRAINT snapshots_account_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_counter snapshots_counter_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_counter
    ADD CONSTRAINT snapshots_counter_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_account aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_account
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_account(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_counter aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_counter
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_counter(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_account event_01_account_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_account
    ADD CONSTRAINT event_01_account_fk FOREIGN KEY (event_id) REFERENCES public.events_01_account(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_counter event_01_counter_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_counter
    ADD CONSTRAINT event_01_counter_fk FOREIGN KEY (event_id) REFERENCES public.events_01_counter(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict WliwXoJ9UTUFiHAP2LdmtFoUBzBmOzKkOEro5epPxarsHyy4OUwX2PpvEZFWduc


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20250712060304'),
    ('20250712185240'),
    ('20260307130135'),
    ('20260307130144'),
    ('20260529160000'),
    ('20260629160000'),
    ('20260629170000');
