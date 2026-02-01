-- migrate:up

GRANT ALL ON TABLE public.aggregate_events_01_Todo TO sharpino;
GRANT ALL ON SEQUENCE public.aggregate_events_01_Todo_id_seq to sharpino;
GRANT ALL ON TABLE public.events_01_Todo to sharpino;
GRANT ALL ON TABLE public.snapshots_01_Todo to sharpino;
GRANT ALL ON SEQUENCE public.snapshots_01_Todo_id_seq to sharpino;

GRANT ALL ON TABLE public.aggregate_events_01_Materials TO sharpino;
GRANT ALL ON SEQUENCE public.aggregate_events_01_Materials_id_seq to sharpino;
GRANT ALL ON TABLE public.events_01_Materials to sharpino;
GRANT ALL ON TABLE public.snapshots_01_Materials to sharpino;
GRANT ALL ON SEQUENCE public.snapshots_01_Materials_id_seq to sharpino;

GRANT ALL ON TABLE public.aggregate_events_01_Products TO sharpino;
GRANT ALL ON SEQUENCE public.aggregate_events_01_Products_id_seq to sharpino;
GRANT ALL ON TABLE public.events_01_Products to sharpino;
GRANT ALL ON TABLE public.snapshots_01_Products to sharpino;
GRANT ALL ON SEQUENCE public.snapshots_01_Products_id_seq to sharpino;
          
GRANT ALL ON TABLE public.aggregate_events_01_WorkOrders TO sharpino;
GRANT ALL ON SEQUENCE public.aggregate_events_01_WorkOrders_id_seq to sharpino;
GRANT ALL ON TABLE public.events_01_WorkOrders to sharpino;
GRANT ALL ON TABLE public.snapshots_01_WorkOrders to sharpino;
GRANT ALL ON SEQUENCE public.snapshots_01_WorkOrders_id_seq to sharpino;
          
-- migrate:down
