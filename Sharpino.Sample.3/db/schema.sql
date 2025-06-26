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
-- Name: insert_01_seatrow_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_seatrow_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_seatrow_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_seatrow(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_seatrow_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_seatrow_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_seatrow(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_stadium_event_and_return_id(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_stadium_event_and_return_id(event_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_stadium(event, timestamp)
VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_enhanced_01_balance_aggregate_event_and_return_id(text, integer, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_enhanced_01_balance_aggregate_event_and_return_id(event_in text, last_event_id integer, p_aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$

DECLARE
inserted_id integer;
    event_id integer;
    max_id integer := (SELECT MAX(id) FROM events_01_balance WHERE aggregate_id = p_aggregate_id);
BEGIN

IF (max_id = last_event_id or (last_event_id = 0 and max_id is null)) THEN
    event_id := insert_md_01_balance_event_and_return_id(event_in, p_aggregate_id, md);
INSERT INTO aggregate_events_01_balance(aggregate_id, event_id)
VALUES(p_aggregate_id, event_id);
END IF;

return event_id;

COMMIT;
END;

$$;


--
-- Name: insert_enhanced_01_seatrow_aggregate_event_and_return_id(text, integer, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_enhanced_01_seatrow_aggregate_event_and_return_id(event_in text, last_event_id integer, p_aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$

DECLARE
inserted_id integer;
    event_id integer;
    max_id integer := (SELECT MAX(id) FROM events_01_seatrow WHERE aggregate_id = p_aggregate_id);
BEGIN

IF (max_id = last_event_id or (last_event_id = 0 and max_id is null)) THEN
    event_id := insert_md_01_seatrow_event_and_return_id(event_in, p_aggregate_id, md);
INSERT INTO aggregate_events_01_seatrow(aggregate_id, event_id)
VALUES(p_aggregate_id, event_id);
END IF;

return event_id;

COMMIT;
END;

$$;


--
-- Name: insert_md_01_seatrow_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_seatrow_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_seatrow_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_seatrow(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_seatrow_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_seatrow_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_seatrow(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_stadium_event_and_return_id(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_stadium_event_and_return_id(event_in text, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_stadium(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: aggregate_events_01_seatrow_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_seatrow_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_seatrow; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_seatrow (
    id integer DEFAULT nextval('public.aggregate_events_01_seatrow_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_seatrow; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_seatrow (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_seatrow_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_seatrow ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_seatrow_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_stadium; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_stadium (
    id integer NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_stadium_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_stadium ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_stadium_id_seq
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
-- Name: snapshots_01_seatrow_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_seatrow_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_seatrow; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_seatrow (
    id integer DEFAULT nextval('public.snapshots_01_seatrow_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_stadium_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_stadium_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_stadium; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_stadium (
    id integer DEFAULT nextval('public.snapshots_01_stadium_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: aggregate_events_01_seatrow aggregate_events_01_seatrow_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_seatrow
    ADD CONSTRAINT aggregate_events_01_seatrow_pkey PRIMARY KEY (id);


--
-- Name: events_01_seatrow events_seatrow_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_seatrow
    ADD CONSTRAINT events_seatrow_pkey PRIMARY KEY (id);


--
-- Name: events_01_stadium events_stadium_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_stadium
    ADD CONSTRAINT events_stadium_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_seatrow snapshots_seatrow_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_seatrow
    ADD CONSTRAINT snapshots_seatrow_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_stadium snapshots_stadium_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_stadium
    ADD CONSTRAINT snapshots_stadium_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_seatrow aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_seatrow
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_seatrow(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_seatrow event_01_seatrow_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_seatrow
    ADD CONSTRAINT event_01_seatrow_fk FOREIGN KEY (event_id) REFERENCES public.events_01_seatrow(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_stadium event_01_stadium_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_stadium
    ADD CONSTRAINT event_01_stadium_fk FOREIGN KEY (event_id) REFERENCES public.events_01_stadium(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20241101091436'),
    ('20241101091716');
