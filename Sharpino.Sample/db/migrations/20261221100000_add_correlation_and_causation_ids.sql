-- migrate:up

-- Add correlation_id and causation_id to events_01_tags
ALTER TABLE public.events_01_tags
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_tags_correlation_id 
    ON public.events_01_tags(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_tags_causation_id 
    ON public.events_01_tags(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Add correlation_id and causation_id to events_01_todo
ALTER TABLE public.events_01_todo
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_01_events_todo_correlation_id 
    ON public.events_01_todo(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_01_events_todo_causation_id 
    ON public.events_01_todo(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Add correlation_id and causation_id to events_02_todo
ALTER TABLE public.events_02_todo
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_02_events_todo_correlation_id 
    ON public.events_02_todo(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_02_events_todo_causation_id 
    ON public.events_02_todo(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Add correlation_id and causation_id to events_02_categories
ALTER TABLE public.events_02_categories
    ADD COLUMN IF NOT EXISTS correlation_id uuid,
    ADD COLUMN IF NOT EXISTS causation_id uuid;

CREATE INDEX IF NOT EXISTS ix_02_events_categories_correlation_id 
    ON public.events_02_categories(correlation_id) 
    WHERE correlation_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_02_events_categories_causation_id 
    ON public.events_02_categories(causation_id) 
    WHERE causation_id IS NOT NULL;

-- Drop obsolete functions to avoid ambiguity
DROP FUNCTION IF EXISTS insert_md_01_tags_event_and_return_id(text, text);
DROP FUNCTION IF EXISTS insert_md_01_todo_event_and_return_id(text, text);
DROP FUNCTION IF EXISTS insert_md_02_todo_event_and_return_id(text, text);
DROP FUNCTION IF EXISTS insert_md_02_categories_event_and_return_id(text, text);

-- Tags insert_md (4 params)
CREATE OR REPLACE FUNCTION insert_md_01_tags_event_and_return_id(
    IN event_in text, 
    IN md_in text,
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_tags(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Todo 01 insert_md (4 params)
CREATE OR REPLACE FUNCTION insert_md_01_todo_event_and_return_id(
    IN event_in text, 
    IN md_in text,
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_01_todo(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Todo 02 insert_md (4 params)
CREATE OR REPLACE FUNCTION insert_md_02_todo_event_and_return_id(
    IN event_in text, 
    IN md_in text,
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_todo(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Categories 02 insert_md (4 params)
CREATE OR REPLACE FUNCTION insert_md_02_categories_event_and_return_id(
    IN event_in text, 
    IN md_in text,
    IN correlation_id uuid DEFAULT NULL,
    IN causation_id uuid DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    inserted_id integer;
BEGIN
    INSERT INTO events_02_categories(event, timestamp, md, correlation_id, causation_id)
    VALUES(event_in::text, now(), md_in, correlation_id, causation_id) 
    RETURNING id INTO inserted_id;
    return inserted_id;
END;
$$;

-- Atomic patch for tags
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_tags(
    IN in_event_id integer,
    IN in_noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_tags
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_01_tags
    WHERE event_id >= in_event_id;
END;
$$;

-- Atomic patch for todo 01
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__01_todo(
    IN in_event_id integer,
    IN in_noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_01_todo
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_01_todo
    WHERE event_id >= in_event_id;
END;
$$;

-- Atomic patch for todo 02
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__02_todo(
    IN in_event_id integer,
    IN in_noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_02_todo
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_02_todo
    WHERE event_id >= in_event_id;
END;
$$;

-- Atomic patch for categories 02
CREATE OR REPLACE FUNCTION patch_event_noop_and_purge_snapshots__02_categories(
    IN in_event_id integer,
    IN in_noop_payload text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE events_02_categories
    SET event = in_noop_payload
    WHERE id = in_event_id;

    DELETE FROM snapshots_02_categories
    WHERE event_id >= in_event_id;
END;
$$;

-- migrate:down

