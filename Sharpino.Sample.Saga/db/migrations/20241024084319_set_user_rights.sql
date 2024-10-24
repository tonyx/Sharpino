-- migrate:up

GRANT ALL ON TABLE public.events_01_theater TO safe;
GRANT ALL ON TABLE public.snapshots_01_theater TO safe;
GRANT ALL ON SEQUENCE public.snapshots_01_theater_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_01_theater_id_seq TO safe;

GRANT ALL ON TABLE public.aggregate_events_01_row TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_row_id_seq to safe;
GRANT ALL ON TABLE public.events_01_row to safe;
GRANT ALL ON TABLE public.snapshots_01_row to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_row_id_seq to safe;

GRANT ALL ON TABLE public.aggregate_events_01_booking TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_booking_id_seq to safe;
GRANT ALL ON TABLE public.events_01_booking to safe;
GRANT ALL ON TABLE public.snapshots_01_booking to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_booking_id_seq to safe;
          
-- migrate:down

