
-- migrate:up 

GRANT ALL ON TABLE public.events{ContextVersion}{ContextName} TO safe;
GRANT ALL ON TABLE public.snapshots{ContextVersion}{ContextName} TO safe;
GRANT ALL ON SEQUENCE public.snapshots{ContextVersion}{ContextName}_id_seq TO safe;
GRANT ALL ON SEQUENCE public.events{ContextVersion}{ContextName}_id_seq TO safe;

-- migrate:down