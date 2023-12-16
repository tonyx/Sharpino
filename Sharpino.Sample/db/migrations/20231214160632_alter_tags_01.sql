-- migrate:up
alter table events_01_tags add column kafkaoffset BIGINTEGER;
GRANT ALL ON TABLE public.events_01_tags TO SAFE;

-- migrate:down

