-- migrate:up

GRANT ALL ON TABLE public.events_01_kitchen TO safe;
GRANT ALL ON TABLE public.snapshots_01_kitchen TO safe;
GRANT ALL ON SEQUENCE public.snapshots_01_kitchen_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_01_kitchen_id_seq TO safe;
          
GRANT ALL ON TABLE public.aggregate_events_01_dish TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_dish_id_seq to safe;
GRANT ALL ON TABLE public.events_01_dish to safe;
GRANT ALL ON TABLE public.snapshots_01_dish to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_dish_id_seq to safe;

GRANT ALL ON TABLE public.aggregate_events_01_ingredient TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_ingredient_id_seq to safe;
GRANT ALL ON TABLE public.events_01_ingredient to safe;
GRANT ALL ON TABLE public.snapshots_01_ingredient to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_ingredient_id_seq to safe;


-- migrate:down

