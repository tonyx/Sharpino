-- migrate:up

-- ALTER TABLE public.events_01_kitchen
--     ADD COLUMN context_state_id uuid;
-- 
-- CREATE OR REPLACE FUNCTION insert_01_kitchen_event_and_return_id(
--     IN event_in TEXT,
--     IN context_state_id uuid
-- )
-- RETURNS int
-- LANGUAGE plpgsql
-- AS $$
-- DECLARE 
--     inserted_id integer;
-- BEGIN
--     INSERT INTO events_01_kitchen(event, timestamp, context_state_id) 
--     VALUES(event_in::text, now(), context_state_id) RETURNING id INTO inserted_id;
--     return inserted_id; 
-- END;
-- $$;
-- 
-- 
-- CREATE OR REPLACE FUNCTION insert_md_01_kitchen_aggregate_event_and_return_id(
--     IN event_in text,
--     IN aggregate_id uuid,
--     IN md text   
-- )
-- RETURNS int
--     
-- LANGUAGE plpgsql
-- AS $$
-- DECLARE
-- inserted_id integer;
--     event_id integer;
-- BEGIN
--     event_id := insert_md_01_kitchen_event_and_return_id(event_in, aggregate_id, md);
-- 
-- INSERT INTO aggregate_events_01_kitchen(aggregate_id, event_id)
-- VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
-- return event_id;
-- END;
-- $$;



-- migrate:down

