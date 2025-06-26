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
-- Name: insert_enhanced_01_booking_aggregate_event_and_return_id(text, integer, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_enhanced_01_booking_aggregate_event_and_return_id(event_in text, last_event_id integer, p_aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$

DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_booking_event_and_return_id(event_in, p_aggregate_id, md);

INSERT INTO aggregate_events_01_booking(aggregate_id, event_id)
SELECT p_aggregate_id, event_id
    WHERE (SELECT MAX(id) FROM aggregate_events_01_booking WHERE aggregate_id = p_aggregate_id) = last_event_id
    RETURNING id INTO inserted_id;

IF inserted_id = -1 THEN
        ROLLBACK;
return -1;
END IF;

return event_id;

COMMIT;
END;

$$;


--
-- Name: insert_enhanced_01_row_aggregate_event_and_return_id(text, integer, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_enhanced_01_row_aggregate_event_and_return_id(event_in text, last_event_id integer, p_aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$

DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_row_event_and_return_id(event_in, p_aggregate_id, md);

INSERT INTO aggregate_events_01_row(aggregate_id, event_id)
SELECT p_aggregate_id, event_id
    WHERE (SELECT MAX(id) FROM aggregate_events_01_row WHERE aggregate_id = p_aggregate_id) = last_event_id
    RETURNING id INTO inserted_id;

IF inserted_id = -1 THEN
        ROLLBACK;
return -1;
END IF;

return event_id;

COMMIT;
END;

$$;


--
-- Name: insert_enhanced_01_voucher_aggregate_event_and_return_id(text, integer, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_enhanced_01_voucher_aggregate_event_and_return_id(event_in text, last_event_id integer, p_aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$

DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_voucher_event_and_return_id(event_in, p_aggregate_id, md);

INSERT INTO aggregate_events_01_voucher(aggregate_id, event_id)
SELECT p_aggregate_id, event_id
    WHERE (SELECT MAX(id) FROM aggregate_events_01_voucher WHERE aggregate_id = p_aggregate_id) = last_event_id
    RETURNING id INTO inserted_id;

IF inserted_id = -1 THEN
        ROLLBACK;
return -1;
END IF;

return event_id;

COMMIT;
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
-- Name: insert_md_01_theater_event_and_return_id(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_theater_event_and_return_id(event_in text, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_theater(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
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
    md text
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
    md text
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
    md text
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
    md text
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
-- Name: aggregate_events_01_row aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_row
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_row(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_booking aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_booking
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_booking(id) MATCH FULL ON DELETE CASCADE;


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


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20241024082338'),
    ('20241024083516'),
    ('20241024083748'),
    ('20241201155023'),
    ('20241224084319'),
    ('20250613072815');
