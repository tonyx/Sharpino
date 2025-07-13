-- migrate:up

create index ix_01_events_site_id on public.events_01_site(aggregate_id);
create index ix_01_aggregate_events_site_id on public.aggregate_events_01_site(aggregate_id);
create index ix_01_snapshot_site_id on public.snapshots_01_site(aggregate_id);

create index ix_01_events_truck_id on public.events_01_truck(aggregate_id);
create index ix_01_aggregate_events_truck_id on public.aggregate_events_01_truck(aggregate_id);
create index ix_01_snapshot_truck_id on public.snapshots_01_truck(aggregate_id);

-- migrate:down

