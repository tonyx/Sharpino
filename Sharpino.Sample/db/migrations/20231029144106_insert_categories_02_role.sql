-- migrate:up

GRANT EXECUTE on function insert_02_categories_event_and_return_id(TEXT) to SAFE; 

-- migrate:down

