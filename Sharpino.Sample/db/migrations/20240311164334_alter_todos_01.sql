-- migrate:up

ALTER TABLE public.events_01_todo
    ADD COLUMN context_state_id uuid;

CREATE OR REPLACE FUNCTION insert_01_todo_event_and_return_id(
    IN event_in TEXT,
    IN context_state_id uuid
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE 
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp, context_state_id) 
    -- VALUES(event_in::text, (now() at time zone 'utc'), context_state_id) RETURNING id INTO inserted_id;
    VALUES(event_in::text, now(), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id; 
END;
$$;

CREATE OR REPLACE PROCEDURE set_classic_optimistic_lock_01_todo() AS $$
BEGIN 
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events_01_todo_context_state_id_unique') THEN
ALTER TABLE events_01_todo
    ADD CONSTRAINT context_events_01_todo_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE PROCEDURE un_set_classic_optimistic_lock_01_todo() AS $$
BEGIN
ALTER TABLE events_01_todo
DROP CONSTRAINT IF EXISTS context_events_01_todo_context_state_id_unique; 
END;
$$ LANGUAGE plpgsql;


-- migrate:down

