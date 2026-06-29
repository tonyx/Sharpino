\restrict UeiHHYmhxe7EMymSA0GbfwUBtPJUVUWdkOT31GdruUYbbwvQrlEEfEnDVwlak3T

-- Dumped from database version 15.17 (Debian 15.17-1.pgdg13+1)
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

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: L2CacheTable; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."L2CacheTable" (
    "Id" character varying(449) NOT NULL,
    "Value" bytea NOT NULL,
    "ExpiresAtTime" timestamp with time zone NOT NULL,
    "SlidingExpirationInSeconds" bigint,
    "AbsoluteExpiration" timestamp with time zone
);


--
-- Name: schema_migrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.schema_migrations (
    version character varying(128) NOT NULL
);


--
-- Name: L2CacheTable PK_L2CacheTable; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."L2CacheTable"
    ADD CONSTRAINT "PK_L2CacheTable" PRIMARY KEY ("Id");


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: Index_ExpiresAtTime; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "Index_ExpiresAtTime" ON public."L2CacheTable" USING btree ("ExpiresAtTime");


--
-- PostgreSQL database dump complete
--

\unrestrict UeiHHYmhxe7EMymSA0GbfwUBtPJUVUWdkOT31GdruUYbbwvQrlEEfEnDVwlak3T


--
-- Dbmate schema migrations
--

INSERT INTO public.schema_migrations (version) VALUES
    ('20260612072809');
