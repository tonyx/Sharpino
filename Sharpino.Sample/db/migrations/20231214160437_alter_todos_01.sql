-- migrate:up
alter table events_01_todo add column kafkaoffset BIGINT;
alter table events_01_todo add column kafkapartition INTEGER;
GRANT ALL ON TABLE public.events_01_todo TO SAFE;


-- migrate:down

