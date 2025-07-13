-- migrate:up

GRANT ALL ON TABLE public.aggregate_events_01_counter TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_counter_id_seq to safe;
GRANT ALL ON TABLE public.events_01_counter to safe;
GRANT ALL ON TABLE public.snapshots_01_counter to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_counter_id_seq to safe;
          
GRANT ALL ON TABLE public.aggregate_events_01_account TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_account_id_seq to safe;
GRANT ALL ON TABLE public.events_01_account to safe;
GRANT ALL ON TABLE public.snapshots_01_account to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_account_id_seq to safe;

-- migrate:down

