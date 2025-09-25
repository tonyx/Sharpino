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
    md text
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
    md text
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
    md text
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
    md text
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
    md text
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
    md text
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
-- Name: aggregate_events_01_course aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_course
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_course(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_student aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_student
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_student(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_balance aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_balance
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_balance(id) MATCH FULL ON DELETE CASCADE;


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
    ('20250917135222');
