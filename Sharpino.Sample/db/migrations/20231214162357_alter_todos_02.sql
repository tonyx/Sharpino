-- migrate:up
alter table events_02_todo add column kafkaoffset BIGINT;
alter table events_02_todo add column kafkapartition INTEGER;
GRANT ALL ON TABLE public.events_02_todo TO SAFE;


-- migrate:down

