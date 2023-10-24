-- migrate:up

alter table events_02_todo add column published boolean not null default false;

-- migrate:down

