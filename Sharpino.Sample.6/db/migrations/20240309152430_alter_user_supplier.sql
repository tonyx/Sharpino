-- migrate:up

GRANT ALL ON TABLE public.aggregate_events_01_supplier TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_supplier_id_seq to safe;
GRANT ALL ON TABLE public.events_01_supplier to safe;
GRANT ALL ON TABLE public.snapshots_01_supplier to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_supplier_id_seq to safe;


-- migrate:down

