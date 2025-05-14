
-- migrate:up 

GRANT ALL ON TABLE public.aggregate_events_01_invoices TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_invoices_id_seq to safe;
GRANT ALL ON TABLE public.events_01_invoices to safe;
GRANT ALL ON TABLE public.snapshots_01_invoices to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_invoices_id_seq to safe;

-- migrate:down