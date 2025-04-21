-- migrate:up
GRANT ALL ON TABLE public.aggregate_events_01_sampleobject TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_sampleobject_id_seq to safe;
GRANT ALL ON TABLE public.events_01_sampleobject to safe;
GRANT ALL ON TABLE public.snapshots_01_sampleobject to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_sampleobject_id_seq to safe;
-- migrate:down

