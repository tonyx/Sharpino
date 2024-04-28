
-- migrate:up 

GRANT ALL ON TABLE public.aggregate_events{AggregateVersion}{AggregateStorageName} TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events{AggregateVersion}{AggregateStorageName}_id_seq to safe;
GRANT ALL ON TABLE public.events{AggregateVersion}{AggregateStorageName} to safe;
GRANT ALL ON TABLE public.snapshots{AggregateVersion}{AggregateStorageName} to safe;
GRANT ALL ON SEQUENCE public.snapshots{AggregateVersion}{AggregateStorageName}_id_seq to safe;

-- migrate:down