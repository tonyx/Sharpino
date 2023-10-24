-- migrate:up

alter table events_01_tags add column published boolean not null default false;

-- migrate:down

