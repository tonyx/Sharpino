-- migrate:up


-- CREATE OR REPLACE FUNCTION insert_event_and_return_id(
--     in table_name varchar,
--     IN event_in JSON
-- )
-- RETURNS int
-- LANGUAGE plpgsql
-- AS $$
-- DECLARE 
--     inserted_id integer;
-- BEGIN
--     EXECUTE format('INSERT INTO %I (event, timestamp) VALUES ($1, $2) RETURNING id INTO inserted_id', table_name)
--     USING event_in, now();
--     -- INSERT INTO events_01_todo(event, timestamp) 
--     -- VALUES(event_in, now()) RETURNING id INTO inserted_id;
--     -- return inserted_id; 
-- END;
-- $$



CREATE OR REPLACE FUNCTION insert_event_and_return_id(
    IN event_in JSON
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE 
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp) 
    VALUES(event_in, now()) RETURNING id INTO inserted_id;
    return inserted_id; 
END;
$$

-- migrate:down


