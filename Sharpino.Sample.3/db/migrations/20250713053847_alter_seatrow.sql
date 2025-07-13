-- migrate:up

   create index ix_01_events_seatrow_id on public.events_01_seatrow(aggregate_id);
   create index ix_01_aggregate_events_seatrow_id on public.aggregate_events_01_seatrow(aggregate_id);
   create index ix_01_snapshot_seatrow_id on public.snapshots_01_seatrow(aggregate_id);

-- migrate:down

