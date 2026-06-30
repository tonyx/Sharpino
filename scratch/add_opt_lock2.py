import os
import re

def process_migrations():
    migration_pattern = re.compile(
        r'CREATE\s+(?:OR\s+REPLACE\s+)?FUNCTION\s+(insert_md_(\w+)_aggregate_event_and_return_id_opt_lock)\(', 
        re.IGNORECASE
    )

    # Dictionary mapping migration directory to a list of (prefix_name, full_function_name)
    dirs_to_aggregates = {}

    for root, dirs, files in os.walk('.'):
        if 'db/migrations' in root.replace('\\', '/'):
            # Skip Sample.9 since we manually created separate migrations for it
            if 'Sharpino.Sample.9' in root:
                continue
            for file in files:
                if file.endswith('.sql') and file != '20260629160000_add_check_last_event_id_opt_lock.sql':
                    path = os.path.join(root, file)
                    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
                        content = f.read()
                    matches = migration_pattern.findall(content)
                    if matches:
                        if root not in dirs_to_aggregates:
                            dirs_to_aggregates[root] = set()
                        for full_name, prefix_name in matches:
                            dirs_to_aggregates[root].add((prefix_name, full_name))

    for migration_dir, aggregates in dirs_to_aggregates.items():
        if not aggregates:
            continue
        
        up_functions = []
        down_functions = []
        
        # Sort aggregates to have a deterministic output
        for prefix_name, full_name in sorted(aggregates):
            # Construct names:
            # stream_name is events_<prefix_name>
            # insert helper is insert_md_<prefix_name>_event_and_return_id
            # aggregate_events table is aggregate_events_<prefix_name>
            # new function name is insert_md_<prefix_name>_aggregate_event_and_return_id_opt_lock2
            
            opt_lock2_func = f"insert_md_{prefix_name}_aggregate_event_and_return_id_opt_lock2"
            events_table = f"events_{prefix_name}"
            insert_helper = f"insert_md_{prefix_name}_event_and_return_id"
            agg_events_table = f"aggregate_events_{prefix_name}"
            
            up_func = f"""CREATE OR REPLACE FUNCTION {opt_lock2_func}(
    IN event_in text,
    IN aggregate_id uuid,
    IN distance_from_latest_snapshot int,
    IN md text,   
    IN last_event_id int,
    IN extra_stream_names text[],
    IN extra_event_ids int[],
    IN extra_aggregate_ids uuid[]
)
RETURNS int
    
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
    event_id integer;
BEGIN
    -- Perform the main optimistic locking check for the aggregate itself
    PERFORM check_last_event_id_opt_lock('{events_table}', aggregate_id, last_event_id);

    -- Perform the checks for extra constraints
    IF extra_stream_names IS NOT NULL THEN
        FOR i IN 1..cardinality(extra_stream_names) LOOP
            PERFORM check_last_event_id_opt_lock(extra_stream_names[i], extra_aggregate_ids[i], extra_event_ids[i]);
        END LOOP;
    END IF;

    event_id := {insert_helper}(event_in, aggregate_id, distance_from_latest_snapshot, md);

    INSERT INTO {agg_events_table}(aggregate_id, event_id)
    VALUES(aggregate_id, event_id) RETURNING id INTO inserted_id;
    return event_id;
END;
$$;"""
            up_functions.append(up_func)
            
            down_func = f"DROP FUNCTION IF EXISTS {opt_lock2_func}(\n    text, uuid, int, text, int, text[], int[], uuid[]\n);"
            down_functions.append(down_func)
            
        migration_content = "-- migrate:up\n\n" + "\n\n".join(up_functions) + "\n\n-- migrate:down\n\n" + "\n\n".join(down_functions) + "\n"
        
        migration_file_path = os.path.join(migration_dir, "20260629170000_alter_aggregates_opt_lock2.sql")
        print(f"Writing to {migration_file_path}")
        with open(migration_file_path, "w", encoding="utf-8") as out:
            out.write(migration_content)

if __name__ == '__main__':
    process_migrations()
