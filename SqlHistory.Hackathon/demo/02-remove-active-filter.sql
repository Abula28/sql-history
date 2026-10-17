-- Demo step 3: a "harmless refactor" that silently changes the result.
-- The WHERE clause is removed, so inactive users are counted too (5 -> 8).

DO $$
BEGIN
    IF current_database() <> 'sqlhistory_demo' THEN
        RAISE EXCEPTION 'Refusing to run outside the sqlhistory_demo database (connected to %).', current_database();
    END IF;
END
$$;

CREATE OR REPLACE FUNCTION public.active_users()
RETURNS bigint
LANGUAGE sql
STABLE
AS $$
    SELECT count(*)
    FROM public.users;
$$;

SELECT 'active_users() now returns ' || public.active_users() AS result;
