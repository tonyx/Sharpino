-- migrate:up
alter table events_01_tags add column kafkaoffset BIGINT;
alter table events_01_tags add column kafkapartition INTEGER;
GRANT ALL ON TABLE public.events_01_tags TO SAFE;

-- migrate:down

