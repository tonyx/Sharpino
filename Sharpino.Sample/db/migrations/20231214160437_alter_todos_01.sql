-- migrate:up
alter table events_01_todo add column kafkaoffset BIGINT;

-- migrate:down

