-- migrate:up

create index ix_01_events_item_id on public.events_01_item(aggregate_id);
create index ix_01_aggregate_events_item_id on public.aggregate_events_01_item(aggregate_id);
create index ix_01_snapshot_item_id on public.snapshots_01_item(aggregate_id);

create index ix_01_events_reservations_id on public.events_01_reservations(aggregate_id);
create index ix_01_aggregate_events_reservations_id on public.aggregate_events_01_reservations(aggregate_id);
create index ix_01_snapshot_reservations_id on public.snapshots_01_reservations(aggregate_id);

create index ix_01_events_course_id on public.events_01_course(aggregate_id);
create index ix_01_aggregate_events_course_id on public.aggregate_events_01_course(aggregate_id);
create index ix_01_snapshot_course_id on public.snapshots_01_course(aggregate_id);

create index ix_01_events_student_id on public.events_01_student(aggregate_id);
create index ix_01_aggregate_events_student_id on public.aggregate_events_01_student(aggregate_id);
create index ix_01_snapshot_student_id on public.snapshots_01_student(aggregate_id);

create index ix_01_events_balance_id on public.events_01_balance(aggregate_id);
create index ix_01_aggregate_events_balance_id on public.aggregate_events_01_balance(aggregate_id);
create index ix_01_snapshot_balance_id on public.snapshots_01_balance(aggregate_id);

create index ix_01_events_teacher_id on public.events_01_teacher(aggregate_id);
create index ix_01_aggregate_events_teacher_id on public.aggregate_events_01_teacher(aggregate_id);
create index ix_01_snapshot_teacher_id on public.snapshots_01_teacher(aggregate_id);

-- migrate:down

