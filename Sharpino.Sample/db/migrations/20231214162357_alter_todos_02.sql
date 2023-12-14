-- migrate:up
alter table events_02_todo add column kafkaoffset BIGINT;

-- migrate:down

