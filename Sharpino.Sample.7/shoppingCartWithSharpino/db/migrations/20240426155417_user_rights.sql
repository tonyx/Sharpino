-- migrate:up
GRANT ALL ON TABLE public.aggregate_events_01_cart TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_cart_id_seq to safe;
GRANT ALL ON TABLE public.events_01_cart to safe;
GRANT ALL ON TABLE public.snapshots_01_cart to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_cart_id_seq to safe;

GRANT ALL ON TABLE public.aggregate_events_01_good TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_good_id_seq to safe;
GRANT ALL ON TABLE public.events_01_good to safe;
GRANT ALL ON TABLE public.snapshots_01_good to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_good_id_seq to safe;

GRANT ALL ON TABLE public.events_01_goodsContainer TO safe;
GRANT ALL ON TABLE public.snapshots_01_goodsContainer TO safe;
GRANT ALL ON SEQUENCE public.snapshots_01_goodsContainer_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_01_goodsContainer_id_seq TO safe;

-- GRANT ALL ON TABLE public.aggregate_undo_commands_buffer_01_good TO safe;
-- GRANT ALL ON SEQUENCE public.aggregate_undo_commands_buffer_01_good_id_seq TO safe;

-- GRANT ALL ON TABLE public.aggregate_undo_commands_buffer_01_cart TO safe;
-- GRANT ALL ON SEQUENCE public.aggregate_undo_commands_buffer_01_cart_id_seq TO safe;

-- migrate:down

