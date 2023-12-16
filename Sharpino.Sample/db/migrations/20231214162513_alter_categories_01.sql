-- migrate:up
alter table events_02_categories add column kafkaoffset BIGINTEGER;
grant all on table public.events_02_categories to SAFE;
-- migrate:down

