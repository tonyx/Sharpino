-- migrate:up
GRANT ALL ON TABLE public.events_01_network TO safe;
GRANT ALL ON TABLE public.snapshots_01_network TO safe;
GRANT ALL ON SEQUENCE public.snapshots_01_network_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_01_network_id_seq TO safe;
          
GRANT ALL ON TABLE public.aggregate_events_01_site TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_site_id_seq to safe;
GRANT ALL ON TABLE public.events_01_site to safe;
GRANT ALL ON TABLE public.snapshots_01_site to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_site_id_seq to safe;
          
GRANT ALL ON TABLE public.aggregate_events_01_truck TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_truck_id_seq to safe;
GRANT ALL ON TABLE public.events_01_truck to safe;
GRANT ALL ON TABLE public.snapshots_01_truck to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_truck_id_seq to safe;
          
-- migrate:down

