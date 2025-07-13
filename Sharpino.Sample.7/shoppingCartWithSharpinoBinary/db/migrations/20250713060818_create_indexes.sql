-- migrate:up

create index ix_01_events_good_id on public.events_01_good(aggregate_id);
create index ix_01_aggregate_events_good_id on public.aggregate_events_01_good(aggregate_id);
create index ix_01_snapshot_good_id on public.snapshots_01_good(aggregate_id);

create index ix_01_events_cart_id on public.events_01_cart(aggregate_id);
create index ix_01_aggregate_events_cart_id on public.aggregate_events_01_cart(aggregate_id);
create index ix_01_snapshot_cart_id on public.snapshots_01_cart(aggregate_id);

-- migrate:down

