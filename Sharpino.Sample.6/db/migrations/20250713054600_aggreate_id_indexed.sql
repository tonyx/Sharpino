-- migrate:up

create index ix_01_event_ingredient_id on public.events_01_ingredient(aggregate_id);
create index ix_01_aggregate_event_ingredient_id on public.aggregate_events_01_ingredient(aggregate_id);
create index ix_01_snapshots_ingredient_id on public.snapshots_01_ingredient(aggregate_id);

create index ix_01_event_dish_id on public.events_01_dish(aggregate_id);
create index ix_01_aggregate_event_dish_id on public.aggregate_events_01_dish(aggregate_id);
create index ix_01_snapshots_dish_id on public.snapshots_01_dish(aggregate_id);

create index ix_01_event_supplier_id on public.events_01_supplier(aggregate_id);
create index ix_01_aggregate_event_supplier_id on public.aggregate_events_01_supplier(aggregate_id);
create index ix_01_snapshots_supplier_id on public.snapshots_01_supplier(aggregate_id);



-- migrate:down

