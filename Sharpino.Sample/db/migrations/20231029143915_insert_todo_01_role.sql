-- migrate:up

GRANT EXECUTE on function insert_01_todo_event_and_return_id(TEXT) to SAFE; 

-- migrate:down

