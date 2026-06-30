\restrict Ft5MnCBD188xxJLhwsnOA0XoOBlI3ExjiiAf6aSRcUMw1E6Uc776foeiEGVbiSr

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
-- Name: insert_01_balance_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_balance_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_balance_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_balance(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_balance_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_balance_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_balance(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_course_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_course_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_course_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_course(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_course_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_course_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_course(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_item_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_item_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_item_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_item(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_item_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_item_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_item(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_reservations_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_reservations_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_reservations_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_reservations(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_reservations_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_reservations_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_reservations(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_student_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_student_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_student_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_student(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_student_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_student_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_student(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_teacher_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_teacher_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_teacher_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_teacher(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_teacher_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_teacher_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_teacher(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_balance_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_balance_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_balance_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_balance(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_balance_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_balance_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_balance_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_balance(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_balance_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_balance_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_balance
    WHERE events_01_balance.aggregate_id = insert_md_01_balance_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_balance_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_balance(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_balance_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_balance_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the balance aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_balance', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_balance_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_balance(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_balance_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_balance_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_balance(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_balance_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_balance_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_balance(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_course_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_course_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_course_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_course(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_course_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_course_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_course_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_course(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_course_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_course_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_course
    WHERE events_01_course.aggregate_id = insert_md_01_course_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_course_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_course(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_course_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_course_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the course aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_course', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_course_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_course(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_course_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_course_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_course(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_course_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_course_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_course(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_item_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_item_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_item_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_item(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_item_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_item_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_item_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_item(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_item_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_item_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_item
    WHERE events_01_item.aggregate_id = insert_md_01_item_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_item_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_item(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_item_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_item_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the item aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_item', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_item_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_item(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_item_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_item_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_item(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_item_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_item_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_item(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_reservations_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservations_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_reservations_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_reservations(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_reservations_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservations_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_reservations_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_reservations(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_reservations_aggregate_event_and_return_id_opt_loc(text, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservations_aggregate_event_and_return_id_opt_loc(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_reservations
    WHERE events_01_reservations.aggregate_id = insert_md_01_reservations_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_reservations_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_reservations(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_reservations_aggregate_event_and_return_id_opt_loc(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservations_aggregate_event_and_return_id_opt_loc(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the reservations aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_reservations', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_reservations_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_reservations(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_reservations_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservations_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_reservations(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_reservations_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservations_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_reservations(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_student_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_student_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_student_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_student(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_student_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_student_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_student_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_student(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_student_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_student_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_student
    WHERE events_01_student.aggregate_id = insert_md_01_student_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_student_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_student(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_student_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_student_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the student aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_student', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_student_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_student(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_student_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_student_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_student(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_student_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_student_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_student(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_teacher_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_teacher_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_teacher_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_teacher(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_teacher_aggregate_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_teacher_aggregate_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_teacher_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

INSERT INTO aggregate_events_01_teacher(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_teacher_aggregate_event_and_return_id_opt_lock(text, uuid, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_teacher_aggregate_event_and_return_id_opt_lock(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
    found_last_event_id integer;
BEGIN
    SELECT id INTO found_last_event_id
    FROM events_01_teacher
    WHERE events_01_teacher.aggregate_id = insert_md_01_teacher_aggregate_event_and_return_id_opt_lock.aggregate_id
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

    event_id := insert_md_01_teacher_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_teacher(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_teacher_aggregate_event_and_return_id_opt_lock2(text, uuid, integer, text, integer, text[], integer[], uuid[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_teacher_aggregate_event_and_return_id_opt_lock2(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text, last_event_id integer, extra_stream_names text[], extra_event_ids integer[], extra_aggregate_ids uuid[]) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the teacher aggregate itself
    PERFORM check_last_event_id_opt_lock('events_01_teacher', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := insert_md_01_teacher_event_and_return_id(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO aggregate_events_01_teacher(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;


--
-- Name: insert_md_01_teacher_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_teacher_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_teacher(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_teacher_event_and_return_id(text, uuid, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_teacher_event_and_return_id(event_in text, aggregate_id uuid, distance_from_latest_snapshot integer, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_teacher(event, aggregate_id, distance_from_latest_snapshot, timestamp, md)
VALUES(event_in::text, aggregate_id, distance_from_latest_snapshot, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: aggregate_events_01_balance_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_balance_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_balance; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_balance (
    id integer DEFAULT nextval('public.aggregate_events_01_balance_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_course_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_course_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_course; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_course (
    id integer DEFAULT nextval('public.aggregate_events_01_course_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_item; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_item (
    id integer DEFAULT nextval('public.aggregate_events_01_item_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_reservations_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_reservations_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_reservations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_reservations (
    id integer DEFAULT nextval('public.aggregate_events_01_reservations_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_student_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_student_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_student; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_student (
    id integer DEFAULT nextval('public.aggregate_events_01_student_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_teacher_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_teacher_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_teacher; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_teacher (
    id integer DEFAULT nextval('public.aggregate_events_01_teacher_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_balance; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_balance (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_balance_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_balance ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_balance_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_course; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_course (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_course_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_course ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_course_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_item; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_item (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_item ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_reservations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_reservations (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_reservations_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_reservations ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_reservations_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_student; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_student (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_student_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_student ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_student_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_teacher; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_teacher (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text,
    distance_from_latest_snapshot integer
);


--
-- Name: events_01_teacher_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_teacher ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_teacher_id_seq
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
-- Name: snapshots_01_balance_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_balance_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_balance; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_balance (
    id integer DEFAULT nextval('public.snapshots_01_balance_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_course_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_course_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_course; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_course (
    id integer DEFAULT nextval('public.snapshots_01_course_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_item; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_item (
    id integer DEFAULT nextval('public.snapshots_01_item_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_reservations_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_reservations_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_reservations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_reservations (
    id integer DEFAULT nextval('public.snapshots_01_reservations_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_student_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_student_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_student; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_student (
    id integer DEFAULT nextval('public.snapshots_01_student_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_teacher_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_teacher_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_teacher; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_teacher (
    id integer DEFAULT nextval('public.snapshots_01_teacher_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: aggregate_events_01_balance aggregate_events_01_balance_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_balance
    ADD CONSTRAINT aggregate_events_01_balance_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_course aggregate_events_01_course_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_course
    ADD CONSTRAINT aggregate_events_01_course_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_item aggregate_events_01_item_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_item
    ADD CONSTRAINT aggregate_events_01_item_event_id_key UNIQUE (event_id);


--
-- Name: aggregate_events_01_item aggregate_events_01_item_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_item
    ADD CONSTRAINT aggregate_events_01_item_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_reservations aggregate_events_01_reservations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_reservations
    ADD CONSTRAINT aggregate_events_01_reservations_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_student aggregate_events_01_student_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_student
    ADD CONSTRAINT aggregate_events_01_student_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_teacher aggregate_events_01_teacher_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_teacher
    ADD CONSTRAINT aggregate_events_01_teacher_pkey PRIMARY KEY (id);


--
-- Name: events_01_balance events_balance_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_balance
    ADD CONSTRAINT events_balance_pkey PRIMARY KEY (id);


--
-- Name: events_01_course events_course_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_course
    ADD CONSTRAINT events_course_pkey PRIMARY KEY (id);


--
-- Name: events_01_item events_item_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_item
    ADD CONSTRAINT events_item_pkey PRIMARY KEY (id);


--
-- Name: events_01_reservations events_reservations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_reservations
    ADD CONSTRAINT events_reservations_pkey PRIMARY KEY (id);


--
-- Name: events_01_student events_student_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_student
    ADD CONSTRAINT events_student_pkey PRIMARY KEY (id);


--
-- Name: events_01_teacher events_teacher_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_teacher
    ADD CONSTRAINT events_teacher_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_balance snapshots_balance_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_balance
    ADD CONSTRAINT snapshots_balance_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_course snapshots_course_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_course
    ADD CONSTRAINT snapshots_course_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_item snapshots_item_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_item
    ADD CONSTRAINT snapshots_item_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_reservations snapshots_reservations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_reservations
    ADD CONSTRAINT snapshots_reservations_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_student snapshots_student_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_student
    ADD CONSTRAINT snapshots_student_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_teacher snapshots_teacher_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_teacher
    ADD CONSTRAINT snapshots_teacher_pkey PRIMARY KEY (id);


--
-- Name: ix_01_aggregate_events_balance_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_balance_id ON public.aggregate_events_01_balance USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_course_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_course_id ON public.aggregate_events_01_course USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_item_id ON public.aggregate_events_01_item USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_reservations_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_reservations_id ON public.aggregate_events_01_reservations USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_student_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_student_id ON public.aggregate_events_01_student USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_teacher_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_teacher_id ON public.aggregate_events_01_teacher USING btree (aggregate_id);


--
-- Name: ix_01_events_balance_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_balance_id ON public.events_01_balance USING btree (aggregate_id);


--
-- Name: ix_01_events_balance_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_balance_timestamp ON public.events_01_balance USING btree ("timestamp");


--
-- Name: ix_01_events_course_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_course_id ON public.events_01_course USING btree (aggregate_id);


--
-- Name: ix_01_events_course_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_course_timestamp ON public.events_01_course USING btree ("timestamp");


--
-- Name: ix_01_events_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_item_id ON public.events_01_item USING btree (aggregate_id);


--
-- Name: ix_01_events_item_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_item_timestamp ON public.events_01_item USING btree ("timestamp");


--
-- Name: ix_01_events_reservations_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_reservations_id ON public.events_01_reservations USING btree (aggregate_id);


--
-- Name: ix_01_events_reservations_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_reservations_timestamp ON public.events_01_reservations USING btree ("timestamp");


--
-- Name: ix_01_events_student_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_student_id ON public.events_01_student USING btree (aggregate_id);


--
-- Name: ix_01_events_student_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_student_timestamp ON public.events_01_student USING btree ("timestamp");


--
-- Name: ix_01_events_teacher_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_teacher_id ON public.events_01_teacher USING btree (aggregate_id);


--
-- Name: ix_01_events_teacher_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_teacher_timestamp ON public.events_01_teacher USING btree ("timestamp");


--
-- Name: ix_01_snapshot_balance_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_balance_id ON public.snapshots_01_balance USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_course_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_course_id ON public.snapshots_01_course USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_event_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_event_item_id ON public.snapshots_01_item USING btree (event_id);


--
-- Name: ix_01_snapshot_item_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_item_id ON public.snapshots_01_item USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_reservations_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_reservations_id ON public.snapshots_01_reservations USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_student_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_student_id ON public.snapshots_01_student USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_teacher_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_teacher_id ON public.snapshots_01_teacher USING btree (aggregate_id);


--
-- Name: ix_01_snapshots_balance_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_balance_timestamp ON public.snapshots_01_balance USING btree ("timestamp");


--
-- Name: ix_01_snapshots_course_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_course_timestamp ON public.snapshots_01_course USING btree ("timestamp");


--
-- Name: ix_01_snapshots_item_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_item_timestamp ON public.snapshots_01_item USING btree ("timestamp");


--
-- Name: ix_01_snapshots_reservations_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_reservations_timestamp ON public.snapshots_01_reservations USING btree ("timestamp");


--
-- Name: ix_01_snapshots_student_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_student_timestamp ON public.snapshots_01_student USING btree ("timestamp");


--
-- Name: ix_01_snapshots_teacher_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_teacher_timestamp ON public.snapshots_01_teacher USING btree ("timestamp");


--
-- Name: aggregate_events_01_balance aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_balance
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_balance(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_course aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_course
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_course(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_item aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_item
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_item(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_reservations aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_reservations
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_reservations(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_student aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_student
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_student(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_teacher aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_teacher
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_teacher(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_balance event_01_balance_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_balance
    ADD CONSTRAINT event_01_balance_fk FOREIGN KEY (event_id) REFERENCES public.events_01_balance(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_course event_01_course_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_course
    ADD CONSTRAINT event_01_course_fk FOREIGN KEY (event_id) REFERENCES public.events_01_course(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_item event_01_item_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_item
    ADD CONSTRAINT event_01_item_fk FOREIGN KEY (event_id) REFERENCES public.events_01_item(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_reservations event_01_reservations_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_reservations
    ADD CONSTRAINT event_01_reservations_fk FOREIGN KEY (event_id) REFERENCES public.events_01_reservations(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_student event_01_student_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_student
    ADD CONSTRAINT event_01_student_fk FOREIGN KEY (event_id) REFERENCES public.events_01_student(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_teacher event_01_teacher_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_teacher
    ADD CONSTRAINT event_01_teacher_fk FOREIGN KEY (event_id) REFERENCES public.events_01_teacher(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict Ft5MnCBD188xxJLhwsnOA0XoOBlI3ExjiiAf6aSRcUMw1E6Uc776foeiEGVbiSr


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20250611114911'),
    ('20250612173532'),
    ('20250613121749'),
    ('20250613121754'),
    ('20250616094111'),
    ('20250619183125'),
    ('20250713061243'),
    ('20260307122626'),
    ('20260307122819'),
    ('20260307122954'),
    ('20260307123021'),
    ('20260307123130'),
    ('20260307123335'),
    ('20260529160000'),
    ('20260629134821'),
    ('20260629134827'),
    ('20260629150212'),
    ('20260629150416'),
    ('20260629150719'),
    ('20260629151011'),
    ('20260629160000');
