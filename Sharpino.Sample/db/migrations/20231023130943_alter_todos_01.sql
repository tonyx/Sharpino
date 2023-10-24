-- migrate:up

alter table events_01_todo add column published boolean not null default false;

-- migrate:down

