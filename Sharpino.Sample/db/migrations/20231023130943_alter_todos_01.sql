-- migrate:up

alter table events_01_todo add column published boolean not null default false;

CREATE OR REPLACE FUNCTION insert_01_todo_event_and_return_id(
    IN event_in TEXT
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE 
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp) 
    -- VALUES(event_in::text, (now() at time zone 'utc')) RETURNING id INTO inserted_id;
    VALUES(event_in::text, now()) RETURNING id INTO inserted_id;
    return inserted_id; 
END;
$$

-- migrate:down

