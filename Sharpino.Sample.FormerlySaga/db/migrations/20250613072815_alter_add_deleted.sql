-- migrate:up

alter table public.snapshots_01_row add column is_deleted boolean not null default false;
alter table public.snapshots_01_booking add column is_deleted boolean not null default false;
alter table public.snapshots_01_voucher add column is_deleted boolean not null default false;

-- migrate:down

