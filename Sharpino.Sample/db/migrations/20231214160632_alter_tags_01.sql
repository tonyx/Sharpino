-- migrate:up
alter table events_01_tags add column kafkaoffset BIGINT;

-- migrate:down

