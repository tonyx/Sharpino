-- migrate:up

GRANT ALL ON TABLE public.aggregate_events_01_course TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_course_id_seq to safe;
GRANT ALL ON TABLE public.events_01_course to safe;
GRANT ALL ON TABLE public.snapshots_01_course to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_course_id_seq to safe;

GRANT ALL ON TABLE public.aggregate_events_01_person TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_person_id_seq to safe;
GRANT ALL ON TABLE public.events_01_person to safe;
GRANT ALL ON TABLE public.snapshots_01_person to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_person_id_seq to safe;
-- migrate:down

