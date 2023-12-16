-- migrate:up
GRANT ALL ON TABLE public.events_02_todo TO safe;
GRANT ALL ON TABLE public.snapshots_02_todo TO safe;
GRANT ALL ON SEQUENCE public.snapshots_02_todo_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events_02_todo_id_seq TO safe;


-- migrate:down

