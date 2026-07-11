-- migrate:up

GRANT ALL ON TABLE public.aggregate_events_01_Todo TO sharpino;
GRANT ALL ON SEQUENCE public.aggregate_events_01_Todo_id_seq to sharpino;
GRANT ALL ON TABLE public.events_01_Todo to sharpino;
GRANT ALL ON TABLE public.snapshots_01_Todo to sharpino;
GRANT ALL ON SEQUENCE public.snapshots_01_Todo_id_seq to sharpino;

GRANT ALL ON TABLE public.aggregate_events_01_User TO sharpino;
GRANT ALL ON SEQUENCE public.aggregate_events_01_User_id_seq to sharpino;
GRANT ALL ON TABLE public.events_01_User to sharpino;
GRANT ALL ON TABLE public.snapshots_01_User to sharpino;
GRANT ALL ON SEQUENCE public.snapshots_01_User_id_seq to sharpino;
-- migrate:down
