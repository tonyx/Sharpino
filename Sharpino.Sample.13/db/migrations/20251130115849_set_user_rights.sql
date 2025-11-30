-- migrate:up 

GRANT ALL ON TABLE public.aggregate_events_01_User TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_User_id_seq to safe;
GRANT ALL ON TABLE public.events_01_User to safe;
GRANT ALL ON TABLE public.snapshots_01_User to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_User_id_seq to safe;

GRANT ALL ON TABLE public.aggregate_events_01_ReservationForNickNames TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_ReservationForNickNames_id_seq to safe;
GRANT ALL ON TABLE public.events_01_ReservationForNickNames to safe;
GRANT ALL ON TABLE public.snapshots_01_ReservationForNickNames to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_ReservationForNickNames_id_seq to safe;
          
-- migrate:down

