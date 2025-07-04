-- migrate:up
GRANT ALL ON TABLE public.aggregate_events_01_item TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_item_id_seq to safe;
GRANT ALL ON TABLE public.events_01_item to safe;
GRANT ALL ON TABLE public.snapshots_01_item to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_item_id_seq to safe;

GRANT ALL ON TABLE public.aggregate_events_01_reservations TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_reservations_id_seq to safe;
GRANT ALL ON TABLE public.events_01_reservations to safe;
GRANT ALL ON TABLE public.snapshots_01_reservations to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_reservations_id_seq to safe;

GRANT ALL ON TABLE public.aggregate_events_01_course TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_course_id_seq to safe;
GRANT ALL ON TABLE public.events_01_course to safe;
GRANT ALL ON TABLE public.snapshots_01_course to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_course_id_seq to safe;
          
GRANT ALL ON TABLE public.aggregate_events_01_student TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_student_id_seq to safe;
GRANT ALL ON TABLE public.events_01_student to safe;
GRANT ALL ON TABLE public.snapshots_01_student to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_student_id_seq to safe;
          
GRANT ALL ON TABLE public.aggregate_events_01_balance TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_balance_id_seq to safe;
GRANT ALL ON TABLE public.events_01_balance to safe;
GRANT ALL ON TABLE public.snapshots_01_balance to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_balance_id_seq to safe;
          
GRANT ALL ON TABLE public.aggregate_events_01_teacher TO safe;
GRANT ALL ON SEQUENCE public.aggregate_events_01_teacher_id_seq to safe;
GRANT ALL ON TABLE public.events_01_teacher to safe;
GRANT ALL ON TABLE public.snapshots_01_teacher to safe;
GRANT ALL ON SEQUENCE public.snapshots_01_teacher_id_seq to safe;
-- migrate:down

