-- migrate:up

alter table events_02_todo add column published boolean not null default false;

CREATE OR REPLACE FUNCTION insert_02_todo_event_and_return_id(
    IN event_in TEXT
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE 
    inserted_id integer;
BEGIN
    INSERT INTO events_02_todo(event, timestamp) 
    VALUES(event_in::JSON, now()) RETURNING id INTO inserted_id;
    return inserted_id; 
END;
$$

-- migrate:down

