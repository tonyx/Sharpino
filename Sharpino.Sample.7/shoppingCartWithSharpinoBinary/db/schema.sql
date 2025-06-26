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
-- Name: insert_01_cart_aggregate_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_cart_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_cart_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_cart(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_cart_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_cart_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_cart(event, aggregate_id, timestamp)
VALUES(event_in::bytea, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_good_aggregate_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_good_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_good_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_good(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_good_event_and_return_id(bytea, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_good_event_and_return_id(event_in bytea, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_good(event, aggregate_id, timestamp)
VALUES(event_in::bytea, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_goodscontainer_event_and_return_id(bytea); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_goodscontainer_event_and_return_id(event_in bytea) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_goodsContainer(event, timestamp)
VALUES(event_in::bytea, now()) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: insert_enhanced_01_cart_aggregate_event_and_return_id(bytea, integer, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_enhanced_01_cart_aggregate_event_and_return_id(event_in bytea, last_event_id integer, p_aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$

DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_cart_event_and_return_id(event_in, p_aggregate_id, md);

INSERT INTO aggregate_events_01_cart(aggregate_id, event_id)
SELECT p_aggregate_id, event_id
    WHERE (SELECT MAX(id) FROM aggregate_events_01_cart WHERE aggregate_id = p_aggregate_id) = last_event_id
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
-- Name: insert_enhanced_01_good_aggregate_event_and_return_id(bytea, integer, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_enhanced_01_good_aggregate_event_and_return_id(event_in bytea, last_event_id integer, p_aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$

DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_good_event_and_return_id(event_in, p_aggregate_id, md);

INSERT INTO aggregate_events_01_good(aggregate_id, event_id)
SELECT p_aggregate_id, event_id
    WHERE (SELECT MAX(id) FROM aggregate_events_01_good WHERE aggregate_id = p_aggregate_id) = last_event_id
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
-- Name: insert_md_01_cart_aggregate_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_cart_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_cart_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_cart(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_cart_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_cart_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_cart(event, aggregate_id, timestamp, md)
VALUES(event_in::bytea, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_good_aggregate_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_good_aggregate_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_good_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_good(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_good_event_and_return_id(bytea, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_good_event_and_return_id(event_in bytea, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_good(event, aggregate_id, timestamp, md)
VALUES(event_in::bytea, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_goodscontainer_event_and_return_id(bytea, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_goodscontainer_event_and_return_id(event_in bytea, md_in text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_goodsContainer(event, timestamp, md)
VALUES(event_in::bytea, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;

END;
$$;


--
-- Name: aggregate_events_01_cart_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_cart_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_cart; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_cart (
    id integer DEFAULT nextval('public.aggregate_events_01_cart_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_good_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_good_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_good; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_good (
    id integer DEFAULT nextval('public.aggregate_events_01_good_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_cart; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_cart (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event bytea NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_cart_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_cart ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_cart_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_good; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_good (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event bytea NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_good_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_good ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_good_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_goodscontainer; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_goodscontainer (
    id integer NOT NULL,
    event bytea NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_goodscontainer_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_goodscontainer ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_goodscontainer_id_seq
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
-- Name: snapshots_01_cart_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_cart_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_cart; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_cart (
    id integer DEFAULT nextval('public.snapshots_01_cart_id_seq'::regclass) NOT NULL,
    snapshot bytea NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_good_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_good_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_good; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_good (
    id integer DEFAULT nextval('public.snapshots_01_good_id_seq'::regclass) NOT NULL,
    snapshot bytea NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_goodscontainer_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_goodscontainer_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_goodscontainer; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_goodscontainer (
    id integer DEFAULT nextval('public.snapshots_01_goodscontainer_id_seq'::regclass) NOT NULL,
    snapshot bytea NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


--
-- Name: aggregate_events_01_cart aggregate_events_01_cart_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_cart
    ADD CONSTRAINT aggregate_events_01_cart_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_good aggregate_events_01_good_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_good
    ADD CONSTRAINT aggregate_events_01_good_pkey PRIMARY KEY (id);


--
-- Name: events_01_goodscontainer events_01_goodscontainer_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_goodscontainer
    ADD CONSTRAINT events_01_goodscontainer_pkey PRIMARY KEY (id);


--
-- Name: events_01_cart events_cart_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_cart
    ADD CONSTRAINT events_cart_pkey PRIMARY KEY (id);


--
-- Name: events_01_good events_good_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_good
    ADD CONSTRAINT events_good_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_goodscontainer snapshots_01_goodscontainer_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_goodscontainer
    ADD CONSTRAINT snapshots_01_goodscontainer_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_cart snapshots_cart_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_cart
    ADD CONSTRAINT snapshots_cart_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_good snapshots_good_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_good
    ADD CONSTRAINT snapshots_good_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_good aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_good
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_good(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_cart aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_cart
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_cart(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_cart event_01_cart_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_cart
    ADD CONSTRAINT event_01_cart_fk FOREIGN KEY (event_id) REFERENCES public.events_01_cart(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_good event_01_good_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_good
    ADD CONSTRAINT event_01_good_fk FOREIGN KEY (event_id) REFERENCES public.events_01_good(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_goodscontainer event_01_goodscontainer_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_goodscontainer
    ADD CONSTRAINT event_01_goodscontainer_fk FOREIGN KEY (event_id) REFERENCES public.events_01_goodscontainer(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20250117130242'),
    ('20250117130803'),
    ('20250117131138'),
    ('20250612130637'),
    ('20250717142617');
