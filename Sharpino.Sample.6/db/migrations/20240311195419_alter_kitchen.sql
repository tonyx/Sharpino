-- migrate:up

ALTER TABLE public.events_01_kitchen
    ADD COLUMN context_state_id uuid;

CREATE OR REPLACE FUNCTION insert_01_kitchen_event_and_return_id(
    IN event_in TEXT,
    IN context_state_id uuid
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE 
    inserted_id integer;
BEGIN
    INSERT INTO events_01_kitchen(event, timestamp, context_state_id) 
    VALUES(event_in::text, now(), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id; 
END;
$$;
-- migrate:down

