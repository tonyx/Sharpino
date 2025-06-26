-- migrate:up
-- alter table public.Snapshots_01_ingredient add column is_deleted boolean not null default false;
-- alter table public.Snapshots_01_dish add column is_deleted boolean not null default false;
-- alter table public.Snapshots_01_supplier add column is_deleted boolean not null default false;
-- migrate:down

