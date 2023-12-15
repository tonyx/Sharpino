-- migrate:up
alter table events_02_categories add column kafkaoffset BIGINT;

-- migrate:down

