-- migrate:up
-- alter table public.snapshots_01_network add column is_deleted boolean not null default false;
-- alter table public.snapshots_01_site add column is_deleted boolean not null default false;
-- alter table public.snapshots_01_truck add column is_deleted boolean not null default false;
-- migrate:down

