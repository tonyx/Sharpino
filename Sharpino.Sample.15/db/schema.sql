\restrict 6FQ1vIL7aQn4rHv2UhrQtpaUFzDBJkHWFPDqxSJpeIEfDvoPtxPw41rqxDFUv6u

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
-- Name: insert_01_enrollments_aggregate_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_enrollments_aggregate_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_01_enrollments_event_and_return_id(event_in, aggregate_id);

INSERT INTO aggregate_events_01_enrollments(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_01_enrollments_event_and_return_id(text, uuid); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_01_enrollments_event_and_return_id(event_in text, aggregate_id uuid) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_enrollments(event, aggregate_id, timestamp)
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
-- Name: insert_md_01_enrollments_aggregate_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_enrollments_aggregate_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
    event_id integer;
BEGIN
    event_id := insert_md_01_enrollments_event_and_return_id(event_in, aggregate_id, md);

INSERT INTO aggregate_events_01_enrollments(aggregate_id, event_id)
VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
return event_id;
END;
$$;


--
-- Name: insert_md_01_enrollments_event_and_return_id(text, uuid, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.insert_md_01_enrollments_event_and_return_id(event_in text, aggregate_id uuid, md text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_enrollments(event, aggregate_id, timestamp, md)
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
-- Name: aggregate_events_01_course_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_course_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregate_events_01_course; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_course (
    id integer DEFAULT nextval('public.aggregate_events_01_course_id_seq'::regclass) NOT NULL,
    aggregate_id uuid NOT NULL,
    event_id integer
);


--
-- Name: aggregate_events_01_enrollments_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.aggregate_events_01_enrollments_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: aggregate_events_01_enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.aggregate_events_01_enrollments (
    id integer DEFAULT nextval('public.aggregate_events_01_enrollments_id_seq'::regclass) NOT NULL,
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
-- Name: events_01_enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.events_01_enrollments (
    id integer NOT NULL,
    aggregate_id uuid NOT NULL,
    event text NOT NULL,
    published boolean DEFAULT false NOT NULL,
    "timestamp" timestamp without time zone NOT NULL,
    md text
);


--
-- Name: events_01_enrollments_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.events_01_enrollments ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_enrollments_id_seq
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
-- Name: schema_migrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.schema_migrations (
    version character varying(128) NOT NULL
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
-- Name: snapshots_01_enrollments_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.snapshots_01_enrollments_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: snapshots_01_enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.snapshots_01_enrollments (
    id integer DEFAULT nextval('public.snapshots_01_enrollments_id_seq'::regclass) NOT NULL,
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
-- Name: aggregate_events_01_course aggregate_events_01_course_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_course
    ADD CONSTRAINT aggregate_events_01_course_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_enrollments aggregate_events_01_enrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_enrollments
    ADD CONSTRAINT aggregate_events_01_enrollments_pkey PRIMARY KEY (id);


--
-- Name: aggregate_events_01_student aggregate_events_01_student_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_student
    ADD CONSTRAINT aggregate_events_01_student_pkey PRIMARY KEY (id);


--
-- Name: events_01_course events_course_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_course
    ADD CONSTRAINT events_course_pkey PRIMARY KEY (id);


--
-- Name: events_01_enrollments events_enrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_enrollments
    ADD CONSTRAINT events_enrollments_pkey PRIMARY KEY (id);


--
-- Name: events_01_student events_student_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.events_01_student
    ADD CONSTRAINT events_student_pkey PRIMARY KEY (id);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: snapshots_01_course snapshots_course_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_course
    ADD CONSTRAINT snapshots_course_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_enrollments snapshots_enrollments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_enrollments
    ADD CONSTRAINT snapshots_enrollments_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_student snapshots_student_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_student
    ADD CONSTRAINT snapshots_student_pkey PRIMARY KEY (id);


--
-- Name: ix_01_aggregate_events_course_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_course_id ON public.aggregate_events_01_course USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_enrollments_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_enrollments_id ON public.aggregate_events_01_enrollments USING btree (aggregate_id);


--
-- Name: ix_01_aggregate_events_student_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_aggregate_events_student_id ON public.aggregate_events_01_student USING btree (aggregate_id);


--
-- Name: ix_01_events_course_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_course_id ON public.events_01_course USING btree (aggregate_id);


--
-- Name: ix_01_events_course_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_course_timestamp ON public.events_01_course USING btree ("timestamp");


--
-- Name: ix_01_events_enrollments_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_enrollments_id ON public.events_01_enrollments USING btree (aggregate_id);


--
-- Name: ix_01_events_enrollments_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_enrollments_timestamp ON public.events_01_enrollments USING btree ("timestamp");


--
-- Name: ix_01_events_student_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_student_id ON public.events_01_student USING btree (aggregate_id);


--
-- Name: ix_01_events_student_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_events_student_timestamp ON public.events_01_student USING btree ("timestamp");


--
-- Name: ix_01_snapshot_course_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_course_id ON public.snapshots_01_course USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_enrollments_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_enrollments_id ON public.snapshots_01_enrollments USING btree (aggregate_id);


--
-- Name: ix_01_snapshot_student_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshot_student_id ON public.snapshots_01_student USING btree (aggregate_id);


--
-- Name: ix_01_snapshots_course_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_course_timestamp ON public.snapshots_01_course USING btree ("timestamp");


--
-- Name: ix_01_snapshots_enrollments_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_enrollments_timestamp ON public.snapshots_01_enrollments USING btree ("timestamp");


--
-- Name: ix_01_snapshots_student_timestamp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_01_snapshots_student_timestamp ON public.snapshots_01_student USING btree ("timestamp");


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
-- Name: aggregate_events_01_enrollments aggregate_events_01_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.aggregate_events_01_enrollments
    ADD CONSTRAINT aggregate_events_01_fk FOREIGN KEY (event_id) REFERENCES public.events_01_enrollments(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_course event_01_course_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_course
    ADD CONSTRAINT event_01_course_fk FOREIGN KEY (event_id) REFERENCES public.events_01_course(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_enrollments event_01_enrollments_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_enrollments
    ADD CONSTRAINT event_01_enrollments_fk FOREIGN KEY (event_id) REFERENCES public.events_01_enrollments(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_student event_01_student_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.snapshots_01_student
    ADD CONSTRAINT event_01_student_fk FOREIGN KEY (event_id) REFERENCES public.events_01_student(id) MATCH FULL ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict 6FQ1vIL7aQn4rHv2UhrQtpaUFzDBJkHWFPDqxSJpeIEfDvoPtxPw41rqxDFUv6u


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20251224135111'),
    ('20251224135116'),
    ('20251224135123'),
    ('20251224135538');
