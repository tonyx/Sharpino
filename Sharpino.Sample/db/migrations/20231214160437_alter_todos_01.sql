-- migrate:up
alter table events_01_todo add column kafkaoffset BIGINTEGER;
GRANT ALL ON TABLE public.events_01_todo TO SAFE;


-- migrate:down

