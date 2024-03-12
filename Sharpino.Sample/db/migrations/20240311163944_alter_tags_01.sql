-- migrate:up

ALTER TABLE public.events_01_tags
    ADD COLUMN context_state_id uuid;

CREATE OR REPLACE FUNCTION insert_01_tags_event_and_return_id(
    IN event_in TEXT,
    IN context_state_id uuid
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE 
    inserted_id integer;
BEGIN
    INSERT INTO events_01_tags(event, timestamp, context_state_id) 
    VALUES(event_in::JSON, now(), context_state_id) RETURNING id INTO inserted_id;
    return inserted_id; 
END;
$$;

CREATE OR REPLACE PROCEDURE set_classic_optimistic_lock_01_tags() AS $$
BEGIN 
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'context_events_01_tags_context_state_id_unique') THEN
ALTER TABLE events_01_tags
    ADD CONSTRAINT context_events_01_tags_context_state_id_unique UNIQUE (context_state_id);
END IF;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE PROCEDURE un_set_classic_optimistic_lock_01_tags() AS $$
BEGIN
ALTER TABLE events_01_tags
DROP CONSTRAINT IF EXISTS context_events_01_tags_context_state_id_unique; 
END;
$$ LANGUAGE plpgsql;
-- migrate:down

