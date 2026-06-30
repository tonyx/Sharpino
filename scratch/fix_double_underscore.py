import os

def fix_migration_files():
    target_content = """    full_stream_name := stream_name;
    IF NOT full_stream_name LIKE 'events_%' THEN
        full_stream_name := 'events_' || full_stream_name;
    END IF;"""

    replacement_content = """    full_stream_name := stream_name;
    IF NOT full_stream_name LIKE 'events_%' THEN
        IF full_stream_name LIKE '_%' THEN
            full_stream_name := 'events' || full_stream_name;
        ELSE
            full_stream_name := 'events_' || full_stream_name;
        END IF;
    END IF;"""

    for root, dirs, files in os.walk('.'):
        for file in files:
            if file.endswith('.sql'):
                path = os.path.join(root, file)
                try:
                    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
                        content = f.read()
                    if target_content in content:
                        new_content = content.replace(target_content, replacement_content)
                        with open(path, 'w', encoding='utf-8') as f:
                            f.write(new_content)
                        print(f"Fixed double underscore in: {path}")
                except Exception as e:
                    print(f"Error processing {path}: {e}")

if __name__ == '__main__':
    fix_migration_files()
