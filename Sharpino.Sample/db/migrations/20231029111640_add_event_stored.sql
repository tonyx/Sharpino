-- migrate:up

CREATE OR REPLACE FUNCTION insert_event_and_return_id(
    IN event_in text
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE 
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp) 
    -- VALUES(event_in, (now() at time zone 'utc')) RETURNING id INTO inserted_id;
    VALUES(event_in, now()) RETURNING id INTO inserted_id;
    return inserted_id; 
END;
$$

-- migrate:down


