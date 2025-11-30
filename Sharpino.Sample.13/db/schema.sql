\restrict YrlX5ubV6lQ2afTNed1EdEcZA6odK5tuBSHoYhEFUIHRxvmO2T5T0Rm1dgJKsQf

-- Dumped from database version 14.4
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
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--

-- *not* creating schema, since initdb creates it


--
-- Name: insert_01_reservationfornicknames_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_reservationfornicknames_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_ReservationForNickNames_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_ReservationForNickNames(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_reservationfornicknames_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_reservationfornicknames_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_ReservationForNickNames(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_01_user_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_user_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_User_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_User(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_user_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_user_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_User(event, aggregate_id, timestamp)
VALUES(event_in::text, aggregate_id,  now()) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_reservationfornicknames_aggregate_event_and_return(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservationfornicknames_aggregate_event_and_return(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_ReservationForNickNames_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_ReservationForNickNames(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_reservationfornicknames_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_reservationfornicknames_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_ReservationForNickNames(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: insert_md_01_user_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_user_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_User_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_User(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_user_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_user_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_User(event, aggregate_id, timestamp, md)
VALUES(event_in::text, aggregate_id, now(), md) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;


--
-- Name: aggregate_events_01_reservationfornicknames_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_reservationfornicknames_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_reservationfornicknames; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_reservationfornicknames (
    id integer DEFAULT nextval('public.aggregate_events_01_reservationfornicknames_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_user_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_user_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_user; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_user (
    id integer DEFAULT nextval('public.aggregate_events_01_user_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: events_01_reservationfornicknames; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_reservationfornicknames (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_reservationfornicknames_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_reservationfornicknames ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_reservationfornicknames_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_user; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_user (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_user_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_user ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_user_id_seq
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
-- Name: snapshots_01_reservationfornicknames_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_reservationfornicknames_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_reservationfornicknames; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_reservationfornicknames (
    id integer DEFAULT nextval('public.snapshots_01_reservationfornicknames_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: snapshots_01_user_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_user_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_user; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_user (
    id integer DEFAULT nextval('public.snapshots_01_user_id_seq'::regclass) NOT NULL,
    snapshot text NOT NULL,
    event_id integer,
    aggregate_id uuid NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL
);


--
-- Name: aggregate_events_01_reservationfornicknames aggregate_events_01_reservationfornicknames_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_reservationfornicknames
    ADD CONSTRAINT aggregate_events_01_reservationfornicknames_event_id_key UNIQUE (event_id);


--
-- Name: aggregate_events_01_reservationfornicknames aggregate_events_01_reservationfornicknames_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_reservationfornicknames
    ADD CONSTRAINT aggregate_events_01_reservationfornicknames_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_user aggregate_events_01_user_event_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_user
    ADD CONSTRAINT aggregate_events_01_user_event_id_key UNIQUE (event_id);


--
-- Name: aggregate_events_01_user aggregate_events_01_user_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_user
    ADD CONSTRAINT aggregate_events_01_user_pkey PRIMARY KEY (id);


--
-- Name: events_01_reservationfornicknames events_reservationfornicknames_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_reservationfornicknames
    ADD CONSTRAINT events_reservationfornicknames_pkey PRIMARY KEY (id);


--
-- Name: events_01_user events_user_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_user
    ADD CONSTRAINT events_user_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_reservationfornicknames snapshots_reservationfornicknames_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_reservationfornicknames
    ADD CONSTRAINT snapshots_reservationfornicknames_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_user snapshots_user_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_user
    ADD CONSTRAINT snapshots_user_pkey PRIMARY KEY (id);


--
-- Name: ix_01_aggregate_events_reservationfornicknames_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_reservationfornicknames_id ON public.aggregate_events_01_reservationfornicknames USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_user_id ON public.aggregate_events_01_user USING btree (aggregate_id);


--
-- Name: ix_01_events_reservationfornicknames_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_reservationfornicknames_id ON public.events_01_reservationfornicknames USING btree (aggregate_id);


--
-- Name: ix_01_events_reservationfornicknames_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_reservationfornicknames_timestamp ON public.events_01_reservationfornicknames USING btree ("timestamp");


--
-- Name: ix_01_events_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_user_id ON public.events_01_user USING btree (aggregate_id);


--
-- Name: ix_01_events_user_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_user_timestamp ON public.events_01_user USING btree ("timestamp");


--
-- Name: ix_01_snapshot_reservationfornicknames_event_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_reservationfornicknames_event_id ON public.snapshots_01_reservationfornicknames USING btree (event_id);


--
-- Name: ix_01_snapshot_reservationfornicknames_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_reservationfornicknames_id ON public.snapshots_01_reservationfornicknames USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_user_event_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_user_event_id ON public.snapshots_01_user USING btree (event_id);


--
-- Name: ix_01_snapshot_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_user_id ON public.snapshots_01_user USING btree (aggregate_id);


--
-- Name: ix_01_snapshots_reservationfornicknames_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_reservationfornicknames_timestamp ON public.snapshots_01_reservationfornicknames USING btree ("timestamp");


--
-- Name: ix_01_snapshots_user_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_user_timestamp ON public.snapshots_01_user USING btree ("timestamp");


--
-- Name: aggregate_events_01_user aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_user
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_user(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: aggregate_events_01_reservationfornicknames aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_reservationfornicknames
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_reservationfornicknames(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_reservationfornicknames event_01_reservationfornicknames_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_reservationfornicknames
    ADD CONSTRAINT event_01_reservationfornicknames_fk FOREIGN KEY (event_id) REFERENCES public.events_01_reservationfornicknames(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_user event_01_user_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_user
    ADD CONSTRAINT event_01_user_fk FOREIGN KEY (event_id) REFERENCES public.events_01_user(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict YrlX5ubV6lQ2afTNed1EdEcZA6odK5tuBSHoYhEFUIHRxvmO2T5T0Rm1dgJKsQf


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20251130111314'),
    ('20251130111325'),
    ('20251130115849');
