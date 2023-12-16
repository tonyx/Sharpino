-- migrate:up
alter table events_02_todo add column kafkaoffset BIGINTEGER;
GRANT ALL ON TABLE public.events_02_todo TO SAFE;


-- migrate:down

