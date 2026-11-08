\restrict S6rWTACdKoeK2tfHwTLF78wvLmwyHBsY0VswXj2JLzGbmobO3GYb0Bzc1TZCZSl

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
-- Name: insert_01_booking_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_booking_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_booking_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_booking(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_booking_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_booking_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_booking(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_row_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_row_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_row_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_row(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_row_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_row_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_row(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_theater_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_theater_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_theater(event, timestamp)
VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_01_voucher_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_voucher_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_voucher_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_voucher(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_voucher_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_voucher_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_voucher(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_booking_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_booking_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_booking_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_booking(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_booking_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_booking_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_booking_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_booking(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_booking_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_booking_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_booking', aggregate_id, last_event_id);
    INSERT INTO events_01_booking(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_booking(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_booking_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[], uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_booking_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[], correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_booking', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_booking(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_booking(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_booking_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_booking_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_booking(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_booking_event_and_return_id(text, uuid, integer, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_booking_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_booking(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_md_01_row_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_row_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_row_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_row(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_row_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_row_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_row_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_row(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_row_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_row_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_row', aggregate_id, last_event_id);
    INSERT INTO events_01_row(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_row(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_row_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[], uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_row_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[], correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_row', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_row(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_row(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_row_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_row_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_row(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_row_event_and_return_id(text, uuid, integer, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_row_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_row(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_md_01_theater_event_and_return_id(text, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_theater_event_and_return_id(event_in text, md_in text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_theater(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: insert_md_01_voucher_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_voucher_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_voucher_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_voucher(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_voucher_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_voucher_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_voucher_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_voucher(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_voucher_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_voucher_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_voucher', aggregate_id, last_event_id);
    INSERT INTO events_01_voucher(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_voucher(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_voucher_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[], uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_voucher_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[], correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    PERFORM check_last_event_id_opt_lock('events_01_voucher', aggregate_id, last_event_id);
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    INSERT INTO events_01_voucher(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO event_id;

    INSERT INTO aggregate_events_01_voucher(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_voucher_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_voucher_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_voucher(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_voucher_event_and_return_id(text, uuid, integer, text, uuid, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_voucher_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, correlation_id uuid DEFAULT NULL::uuid, causation_id uuid DEFAULT NULL::uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_voucher(event, aggregate_id, distance_from_latest_snapshot, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md, correlation_id, causation_id)
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__01_booking(uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__01_booking(in_aggregate_id uuid, in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_booking
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_booking
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__01_row(uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__01_row(in_aggregate_id uuid, in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_row
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_row
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__01_theater(integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__01_theater(in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_theater
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_01_theater
    WHERE event_id >= in_event_id;
END;
$$;


--
-- Name: patch_event_noop_and_purge_snapshots__01_voucher(uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.patch_event_noop_and_purge_snapshots__01_voucher(in_aggregate_id uuid, in_event_id integer, in_noop_payload text) RETURNS void
    LANGUAGE plpgsql
    AS $$
BEGIN
    UPDATE events_01_voucher
    SET event = in_noop_payload
    WHERE id = in_event_id AND aggregate_id = in_aggregate_id;

    DELETE FROM snapshots_01_voucher
    WHERE aggregate_id = in_aggregate_id
      AND event_id IS NOT NULL
      AND event_id >= in_event_id;
END;
$$;


--
-- Name: aggregate_events_01_booking_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_booking_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_booking; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_booking (
    id integer DEFAULT nextval('public.aggregate_events_01_booking_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_row_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_row_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_row; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_row (
    id integer DEFAULT nextval('public.aggregate_events_01_row_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_voucher_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_voucher_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_voucher; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_voucher (
    id integer DEFAULT nextval('public.aggregate_events_01_voucher_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_booking; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_booking (
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
-- Name: events_01_booking_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_booking ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_booking_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_row; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_row (
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
-- Name: events_01_row_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_row ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_row_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_theater; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_theater (
    id integer NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    correlation_id uuid,
    causation_id uuid
);


--
-- Name: events_01_theater_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_theater ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_theater_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_voucher; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_voucher (
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
-- Name: events_01_voucher_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_voucher ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_voucher_id_seq
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
-- Name: snapshots_01_booking_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_booking_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_booking; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_booking (
    id integer DEFAULT nextval('public.snapshots_01_booking_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_row_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_row_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_row; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_row (
    id integer DEFAULT nextval('public.snapshots_01_row_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_theater_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_theater_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_theater; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_theater (
    id integer DEFAULT nextval('public.snapshots_01_theater_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: snapshots_01_voucher_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_voucher_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_voucher; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_voucher (
    id integer DEFAULT nextval('public.snapshots_01_voucher_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: aggregate_events_01_booking aggregate_events_01_booking_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_booking
    ADD CONSTRAINT aggregate_events_01_booking_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_row aggregate_events_01_row_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_row
    ADD CONSTRAINT aggregate_events_01_row_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_voucher aggregate_events_01_voucher_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_voucher
    ADD CONSTRAINT aggregate_events_01_voucher_pkey PRIMARY KEY (id);


--
-- Name: events_01_booking events_booking_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_booking
    ADD CONSTRAINT events_booking_pkey PRIMARY KEY (id);


--
-- Name: events_01_row events_row_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_row
    ADD CONSTRAINT events_row_pkey PRIMARY KEY (id);


--
-- Name: events_01_theater events_theater_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_theater
    ADD CONSTRAINT events_theater_pkey PRIMARY KEY (id);


--
-- Name: events_01_voucher events_voucher_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_voucher
    ADD CONSTRAINT events_voucher_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_booking snapshots_booking_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_booking
    ADD CONSTRAINT snapshots_booking_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_row snapshots_row_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_row
    ADD CONSTRAINT snapshots_row_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_theater snapshots_theater_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_theater
    ADD CONSTRAINT snapshots_theater_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_voucher snapshots_voucher_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_voucher
    ADD CONSTRAINT snapshots_voucher_pkey PRIMARY KEY (id);


--
-- Name: ix_01_events_booking_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_booking_causation_id ON public.events_01_booking USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_booking_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_booking_correlation_id ON public.events_01_booking USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_01_events_row_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_row_causation_id ON public.events_01_row USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_row_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_row_correlation_id ON public.events_01_row USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_01_events_theater_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_theater_causation_id ON public.events_01_theater USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_theater_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_theater_correlation_id ON public.events_01_theater USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: ix_01_events_voucher_causation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_voucher_causation_id ON public.events_01_voucher USING btree (causation_id) WHERE (causation_id IS NOT NULL);


--
-- Name: ix_01_events_voucher_correlation_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_voucher_correlation_id ON public.events_01_voucher USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: aggregate_events_01_booking aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_booking
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_booking(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_row aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_row
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_row(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_voucher aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_voucher
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_voucher(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_booking event_01_booking_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_booking
    ADD CONSTRAINT event_01_booking_fk FOREIGN KEY (event_id) REFERENCES public.events_01_booking(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_row event_01_row_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_row
    ADD CONSTRAINT event_01_row_fk FOREIGN KEY (event_id) REFERENCES public.events_01_row(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_theater event_01_theater_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_theater
    ADD CONSTRAINT event_01_theater_fk FOREIGN KEY (event_id) REFERENCES public.events_01_theater(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_voucher event_01_voucher_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_voucher
    ADD CONSTRAINT event_01_voucher_fk FOREIGN KEY (event_id) REFERENCES public.events_01_voucher(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict S6rWTACdKoeK2tfHwTLF78wvLmwyHBsY0VswXj2JLzGbmobO3GYb0Bzc1TZCZSl


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20241024082338'),
    ('20241024083516'),
    ('20241024083748'),
    ('20241201155023'),
    ('20250613072815'),
    ('20260307145730'),
    ('20260307145755'),
    ('20260307145804'),
    ('20260529160000'),
    ('20260629160000'),
    ('20260629170000'),
    ('20260801200000'),
    ('20261221100000');
