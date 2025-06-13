-- migrate:up
alter table public.snapshots_01_seatrow add column is_deleted boolean not null default false;

-- migrate:down

