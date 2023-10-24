-- migrate:up

alter table events_02_categories add column published boolean not null default false;

-- migrate:down

