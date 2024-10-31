-- migrate:up

CREATE OR REPLACE FUNCTION insert_md_01_todo_event_and_return_id(
    IN event_in text, md_in text
)
RETURNS int
       
LANGUAGE plpgsql
AS $$
DECLARE
inserted_id integer;
BEGIN
INSERT INTO events_01_todo(event, timestamp, md)
VALUES(event_in::text, now(), md_in) RETURNING id INTO inserted_id;
return inserted_id;
END;
$$;

-- migrate:down

