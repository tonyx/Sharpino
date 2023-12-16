-- migrate:up

GRANT ALL ON TABLE public.events_02_categories TO safe;
GRANT ALL ON TABLE public.snapshots_02_categories TO safe;
GRANT ALL ON SEQUENCE public.snapshots_02_categories_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_02_categories_id_seq TO safe;

-- migrate:down

