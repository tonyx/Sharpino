--
-- PostgreSQL database dump
--

-- Dumped from database version 14.4
-- Dumped by pg_dump version 15.0

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
-- Name: public; Type: SCHEMA; Schema: -; Owner: postgres
--

-- *not* creating schema, since initdb creates it


ALTER SCHEMA public OWNER TO postgres;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: events_01_tags; Type: TABLE; Schema: public; Owner: safe
--

CREATE TABLE public.events_01_tags (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.events_01_tags OWNER TO safe;

--
-- Name: events_01_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

ALTER TABLE public.events_01_tags ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_01_todo; Type: TABLE; Schema: public; Owner: safe
--

CREATE TABLE public.events_01_todo (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.events_01_todo OWNER TO safe;

--
-- Name: events_01_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

ALTER TABLE public.events_01_todo ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_01_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_02_categories; Type: TABLE; Schema: public; Owner: safe
--

CREATE TABLE public.events_02_categories (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.events_02_categories OWNER TO safe;

--
-- Name: events_02_categories_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

ALTER TABLE public.events_02_categories ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_02_categories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_02_tags; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.events_02_tags (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.events_02_tags OWNER TO postgres;

--
-- Name: events_02_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.events_02_tags ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_02_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_02_todo; Type: TABLE; Schema: public; Owner: safe
--

CREATE TABLE public.events_02_todo (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.events_02_todo OWNER TO safe;

--
-- Name: events_02_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

ALTER TABLE public.events_02_todo ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_02_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_tags; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.events_tags (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.events_tags OWNER TO postgres;

--
-- Name: events_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.events_tags ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: events_todo; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.events_todo (
    id integer NOT NULL,
    event json NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.events_todo OWNER TO postgres;

--
-- Name: events_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.events_todo ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.events_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: snapshots_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.snapshots_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE public.snapshots_tags_id_seq OWNER TO postgres;

--
-- Name: snapshots_01_tags; Type: TABLE; Schema: public; Owner: safe
--

CREATE TABLE public.snapshots_01_tags (
    id integer DEFAULT nextval('public.snapshots_tags_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.snapshots_01_tags OWNER TO safe;

--
-- Name: snapshots_01_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

CREATE SEQUENCE public.snapshots_01_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE public.snapshots_01_tags_id_seq OWNER TO safe;

--
-- Name: snapshots_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.snapshots_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE public.snapshots_todo_id_seq OWNER TO postgres;

--
-- Name: snapshots_01_todo; Type: TABLE; Schema: public; Owner: safe
--

CREATE TABLE public.snapshots_01_todo (
    id integer DEFAULT nextval('public.snapshots_todo_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.snapshots_01_todo OWNER TO safe;

--
-- Name: snapshots_01_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

CREATE SEQUENCE public.snapshots_01_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE public.snapshots_01_todo_id_seq OWNER TO safe;

--
-- Name: snapshots_02_categories_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

CREATE SEQUENCE public.snapshots_02_categories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE public.snapshots_02_categories_id_seq OWNER TO safe;

--
-- Name: snapshots_02_categories; Type: TABLE; Schema: public; Owner: safe
--

CREATE TABLE public.snapshots_02_categories (
    id integer DEFAULT nextval('public.snapshots_02_categories_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.snapshots_02_categories OWNER TO safe;

--
-- Name: snapshots_02_tags_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.snapshots_02_tags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE public.snapshots_02_tags_id_seq OWNER TO postgres;

--
-- Name: snapshots_02_tags; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.snapshots_02_tags (
    id integer DEFAULT nextval('public.snapshots_02_tags_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.snapshots_02_tags OWNER TO postgres;

--
-- Name: snapshots_02_todo_id_seq; Type: SEQUENCE; Schema: public; Owner: safe
--

CREATE SEQUENCE public.snapshots_02_todo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE public.snapshots_02_todo_id_seq OWNER TO safe;

--
-- Name: snapshots_02_todo; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.snapshots_02_todo (
    id integer DEFAULT nextval('public.snapshots_02_todo_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.snapshots_02_todo OWNER TO postgres;

--
-- Name: snapshots_tags; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.snapshots_tags (
    id integer DEFAULT nextval('public.snapshots_tags_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.snapshots_tags OWNER TO postgres;

--
-- Name: snapshots_todo; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.snapshots_todo (
    id integer DEFAULT nextval('public.snapshots_todo_id_seq'::regclass) NOT NULL,
    snapshot json NOT NULL,
    event_id integer NOT NULL,
    "timestamp" timestamp without time zone NOT NULL
);


ALTER TABLE public.snapshots_todo OWNER TO postgres;

--
-- Data for Name: events_01_tags; Type: TABLE DATA; Schema: public; Owner: safe
--

COPY public.events_01_tags (id, event, "timestamp") FROM stdin;
3579	{"Case": "TagAdded", "Fields": [{"Id": "fc7bec14-22ad-4dd7-8481-ea85f546a1ba", "Name": "test", "$type": "Sharpino.Sample.Models.TagsModel+Tag, Sharpino.Sample", "Color": {"Case": "Blue"}}]}	2023-06-18 08:19:10.313457
3580	{"Case": "TagAdded", "Fields": [{"Id": "65222589-0d7c-4d35-8594-b50a5dcf1a56", "Name": "test2", "$type": "Sharpino.Sample.Models.TagsModel+Tag, Sharpino.Sample", "Color": {"Case": "Red"}}]}	2023-06-18 08:19:10.314447
3581	{"Case": "TagRemoved", "Fields": ["fc7bec14-22ad-4dd7-8481-ea85f546a1ba"]}	2023-06-18 08:19:10.317345
\.


--
-- Data for Name: events_01_todo; Type: TABLE DATA; Schema: public; Owner: safe
--

COPY public.events_01_todo (id, event, "timestamp") FROM stdin;
49179	{"Case": "TodoAdded", "Fields": [{"Id": "9db69318-48f6-4cce-878c-633cf6b7665f", "$type": "Sharpino.Sample.Models.TodosModel+Todo, Sharpino.Sample", "TagIds": ["fc7bec14-22ad-4dd7-8481-ea85f546a1ba", "65222589-0d7c-4d35-8594-b50a5dcf1a56"], "CategoryIds": [], "Description": "test"}]}	2023-06-18 08:19:10.316128
49180	{"Case": "TagRefRemoved", "Fields": ["fc7bec14-22ad-4dd7-8481-ea85f546a1ba"]}	2023-06-18 08:19:10.317346
\.


--
-- Data for Name: events_02_categories; Type: TABLE DATA; Schema: public; Owner: safe
--

COPY public.events_02_categories (id, event, "timestamp") FROM stdin;
\.


--
-- Data for Name: events_02_tags; Type: TABLE DATA; Schema: public; Owner: postgres
--

COPY public.events_02_tags (id, event, "timestamp") FROM stdin;
\.


--
-- Data for Name: events_02_todo; Type: TABLE DATA; Schema: public; Owner: safe
--

COPY public.events_02_todo (id, event, "timestamp") FROM stdin;
\.


--
-- Data for Name: events_tags; Type: TABLE DATA; Schema: public; Owner: postgres
--

COPY public.events_tags (id, event, "timestamp") FROM stdin;
687	{"Case": "TagAdded", "Fields": [{"Id": "40d2c443-2a8a-4f44-a948-113a04ce1317", "Name": "test", "Color": {"Case": "Blue"}}]}	2023-05-06 08:24:53.749993
688	{"Case": "TagAdded", "Fields": [{"Id": "d8fd68fa-bc3b-4ef9-8d74-975503aa1d8f", "Name": "test2", "Color": {"Case": "Red"}}]}	2023-05-06 08:24:53.75179
689	{"Case": "TagRemoved", "Fields": ["40d2c443-2a8a-4f44-a948-113a04ce1317"]}	2023-05-06 08:24:53.755232
\.


--
-- Data for Name: events_todo; Type: TABLE DATA; Schema: public; Owner: postgres
--

COPY public.events_todo (id, event, "timestamp") FROM stdin;
2190	{"Case": "TodoAdded", "Fields": [{"Id": "7864d58b-ae89-4aca-84f6-109fc53e72a4", "TagIds": ["40d2c443-2a8a-4f44-a948-113a04ce1317", "d8fd68fa-bc3b-4ef9-8d74-975503aa1d8f"], "CategoryIds": [], "Description": "test"}]}	2023-05-06 08:24:53.753007
2191	{"Case": "TagRefRemoved", "Fields": ["40d2c443-2a8a-4f44-a948-113a04ce1317"]}	2023-05-06 08:24:53.755233
\.


--
-- Data for Name: snapshots_01_tags; Type: TABLE DATA; Schema: public; Owner: safe
--

COPY public.snapshots_01_tags (id, snapshot, event_id, "timestamp") FROM stdin;
12593	{"Tags": {"tags": [{"Id": "fc7bec14-22ad-4dd7-8481-ea85f546a1ba", "Name": "test", "$type": "Sharpino.Sample.Models.TagsModel+Tag, Sharpino.Sample", "Color": {"Case": "Blue"}}], "$type": "Sharpino.Sample.Models.TagsModel+Tags, Sharpino.Sample"}, "$type": "Sharpino.Sample.TagsAggregate+TagsAggregate, Sharpino.Sample"}	3579	2023-06-18 08:19:10.313457
\.


--
-- Data for Name: snapshots_01_todo; Type: TABLE DATA; Schema: public; Owner: safe
--

COPY public.snapshots_01_todo (id, snapshot, event_id, "timestamp") FROM stdin;
90622	{"$type": "Sharpino.Sample.TodosAggregate+TodosAggregate, Sharpino.Sample", "todos": {"$type": "Sharpino.Sample.Models.TodosModel+Todos, Sharpino.Sample", "todos": [{"Id": "9db69318-48f6-4cce-878c-633cf6b7665f", "$type": "Sharpino.Sample.Models.TodosModel+Todo, Sharpino.Sample", "TagIds": ["fc7bec14-22ad-4dd7-8481-ea85f546a1ba", "65222589-0d7c-4d35-8594-b50a5dcf1a56"], "CategoryIds": [], "Description": "test"}]}, "categories": {"$type": "Sharpino.Sample.Models.CategoriesModel+Categories, Sharpino.Sample", "categories": []}}	49179	2023-06-18 08:19:10.316128
\.


--
-- Data for Name: snapshots_02_categories; Type: TABLE DATA; Schema: public; Owner: safe
--

COPY public.snapshots_02_categories (id, snapshot, event_id, "timestamp") FROM stdin;
\.


--
-- Data for Name: snapshots_02_tags; Type: TABLE DATA; Schema: public; Owner: postgres
--

COPY public.snapshots_02_tags (id, snapshot, event_id, "timestamp") FROM stdin;
\.


--
-- Data for Name: snapshots_02_todo; Type: TABLE DATA; Schema: public; Owner: postgres
--

COPY public.snapshots_02_todo (id, snapshot, event_id, "timestamp") FROM stdin;
\.


--
-- Data for Name: snapshots_tags; Type: TABLE DATA; Schema: public; Owner: postgres
--

COPY public.snapshots_tags (id, snapshot, event_id, "timestamp") FROM stdin;
404	{"Tags": {"tags": [{"Id": "40d2c443-2a8a-4f44-a948-113a04ce1317", "Name": "test", "$type": "Tonyx.EventSourcing.Sample.Tags.Models.TagsModel+Tag, Micro_ES_FSharp_Lib.Sample", "Color": {"Case": "Blue"}}], "$type": "Tonyx.EventSourcing.Sample.Tags.Models.TagsModel+Tags, Micro_ES_FSharp_Lib.Sample"}, "$type": "Tonyx.EventSourcing.Sample.TagsAggregate+TagsAggregate, Micro_ES_FSharp_Lib.Sample"}	687	2023-05-06 08:24:53.749993
\.


--
-- Data for Name: snapshots_todo; Type: TABLE DATA; Schema: public; Owner: postgres
--

COPY public.snapshots_todo (id, snapshot, event_id, "timestamp") FROM stdin;
1236	{"$type": "Tonyx.EventSourcing.Sample.TodosAggregate+TodosAggregate, Micro_ES_FSharp_Lib.Sample", "todos": {"$type": "Tonyx.EventSourcing.Sample.Todos.Models.TodosModel+Todos, Micro_ES_FSharp_Lib.Sample", "todos": [{"Id": "7864d58b-ae89-4aca-84f6-109fc53e72a4", "$type": "Tonyx.EventSourcing.Sample.Todos.Models.TodosModel+Todo, Micro_ES_FSharp_Lib.Sample", "TagIds": ["40d2c443-2a8a-4f44-a948-113a04ce1317", "d8fd68fa-bc3b-4ef9-8d74-975503aa1d8f"], "CategoryIds": [], "Description": "test"}]}, "categories": {"$type": "Tonyx.EventSourcing.Sample.Todos.Models.CategoriesModel+Categories, Micro_ES_FSharp_Lib.Sample", "categories": []}}	2190	2023-05-06 08:24:53.753007
\.


--
-- Name: events_01_tags_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.events_01_tags_id_seq', 3581, true);


--
-- Name: events_01_todo_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.events_01_todo_id_seq', 49180, true);


--
-- Name: events_02_categories_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.events_02_categories_id_seq', 3483, true);


--
-- Name: events_02_tags_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.events_02_tags_id_seq', 100, true);


--
-- Name: events_02_todo_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.events_02_todo_id_seq', 5058, true);


--
-- Name: events_tags_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.events_tags_id_seq', 689, true);


--
-- Name: events_todo_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.events_todo_id_seq', 2191, true);


--
-- Name: snapshots_01_tags_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.snapshots_01_tags_id_seq', 1, false);


--
-- Name: snapshots_01_todo_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.snapshots_01_todo_id_seq', 1, false);


--
-- Name: snapshots_02_categories_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.snapshots_02_categories_id_seq', 1193, true);


--
-- Name: snapshots_02_tags_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.snapshots_02_tags_id_seq', 35, true);


--
-- Name: snapshots_02_todo_id_seq; Type: SEQUENCE SET; Schema: public; Owner: safe
--

SELECT pg_catalog.setval('public.snapshots_02_todo_id_seq', 2569, true);


--
-- Name: snapshots_tags_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.snapshots_tags_id_seq', 12593, true);


--
-- Name: snapshots_todo_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.snapshots_todo_id_seq', 90622, true);


--
-- Name: events_01_tags events_01_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.events_01_tags
    ADD CONSTRAINT events_01_tags_pkey PRIMARY KEY (id);


--
-- Name: events_01_todo events_01_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.events_01_todo
    ADD CONSTRAINT events_01_todo_pkey PRIMARY KEY (id);


--
-- Name: events_02_categories events_02_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.events_02_categories
    ADD CONSTRAINT events_02_categories_pkey PRIMARY KEY (id);


--
-- Name: events_02_todo events_02_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.events_02_todo
    ADD CONSTRAINT events_02_todo_pkey PRIMARY KEY (id);


--
-- Name: events_tags events_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.events_tags
    ADD CONSTRAINT events_tags_pkey PRIMARY KEY (id);


--
-- Name: events_todo events_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.events_todo
    ADD CONSTRAINT events_todo_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_tags snapshots_01_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.snapshots_01_tags
    ADD CONSTRAINT snapshots_01_tags_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_todo snapshots_01_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.snapshots_01_todo
    ADD CONSTRAINT snapshots_01_todo_pkey PRIMARY KEY (id);


--
-- Name: snapshots_02_categories snapshots_02_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.snapshots_02_categories
    ADD CONSTRAINT snapshots_02_categories_pkey PRIMARY KEY (id);


--
-- Name: snapshots_02_todo snapshots_02_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.snapshots_02_todo
    ADD CONSTRAINT snapshots_02_todo_pkey PRIMARY KEY (id);


--
-- Name: snapshots_tags snapshots_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.snapshots_tags
    ADD CONSTRAINT snapshots_tags_pkey PRIMARY KEY (id);


--
-- Name: snapshots_todo snapshots_todo_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.snapshots_todo
    ADD CONSTRAINT snapshots_todo_pkey PRIMARY KEY (id);


--
-- Name: snapshots_01_tags event_01_tags_fk; Type: FK CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.snapshots_01_tags
    ADD CONSTRAINT event_01_tags_fk FOREIGN KEY (event_id) REFERENCES public.events_01_tags(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_01_todo event_01_todo_fk; Type: FK CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.snapshots_01_todo
    ADD CONSTRAINT event_01_todo_fk FOREIGN KEY (event_id) REFERENCES public.events_01_todo(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_02_categories event_02_categories_fk; Type: FK CONSTRAINT; Schema: public; Owner: safe
--

ALTER TABLE ONLY public.snapshots_02_categories
    ADD CONSTRAINT event_02_categories_fk FOREIGN KEY (event_id) REFERENCES public.events_02_categories(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_02_todo event_02_todo_fk; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.snapshots_02_todo
    ADD CONSTRAINT event_02_todo_fk FOREIGN KEY (event_id) REFERENCES public.events_02_todo(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_tags event_tags_fk; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.snapshots_tags
    ADD CONSTRAINT event_tags_fk FOREIGN KEY (event_id) REFERENCES public.events_tags(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: snapshots_todo event_todo_fk; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.snapshots_todo
    ADD CONSTRAINT event_todo_fk FOREIGN KEY (event_id) REFERENCES public.events_todo(id) MATCH FULL ON DELETE CASCADE;


--
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: postgres
--

REVOKE USAGE ON SCHEMA public FROM PUBLIC;
GRANT ALL ON SCHEMA public TO PUBLIC;


--
-- Name: TABLE events_02_tags; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.events_02_tags TO safe;


--
-- Name: SEQUENCE events_02_tags_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.events_02_tags_id_seq TO safe;


--
-- Name: TABLE events_tags; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.events_tags TO safe;


--
-- Name: SEQUENCE events_tags_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.events_tags_id_seq TO safe;


--
-- Name: TABLE events_todo; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.events_todo TO safe;


--
-- Name: SEQUENCE events_todo_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.events_todo_id_seq TO safe;


--
-- Name: SEQUENCE snapshots_tags_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.snapshots_tags_id_seq TO safe;


--
-- Name: SEQUENCE snapshots_todo_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.snapshots_todo_id_seq TO safe;


--
-- Name: SEQUENCE snapshots_02_tags_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.snapshots_02_tags_id_seq TO safe;


--
-- Name: TABLE snapshots_02_tags; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.snapshots_02_tags TO safe;


--
-- Name: TABLE snapshots_02_todo; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.snapshots_02_todo TO safe;


--
-- Name: TABLE snapshots_tags; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.snapshots_tags TO safe;


--
-- Name: TABLE snapshots_todo; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.snapshots_todo TO safe;


--
-- PostgreSQL database dump complete
--

