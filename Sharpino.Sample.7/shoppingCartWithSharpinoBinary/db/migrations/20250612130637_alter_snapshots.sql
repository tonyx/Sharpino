-- migrate:up
alter table public.snapshots_01_good add column is_deleted boolean not null default false;
alter table public.snapshots_01_cart add column is_deleted boolean not null default false;

-- migrate:down

