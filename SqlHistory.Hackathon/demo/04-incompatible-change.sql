-- Optional demo: a change CREATE OR REPLACE cannot undo.
--
-- top_customers gets an extra output column, which requires DROP + CREATE. Restoring the
-- previous version would need the routine dropped again, so SQL Time Machine's dry run reports
-- PostgreSQL's "cannot change return type" error and refuses instead of dropping anything.

DO $$
BEGIN
    IF current_database() <> 'sqlhistory_demo' THEN
        RAISE EXCEPTION 'Refusing to run outside the sqlhistory_demo database (connected to %).', current_database();
    END IF;
END
$$;

BEGIN;

DROP FUNCTION analytics.top_customers(integer);

CREATE FUNCTION analytics.top_customers(p_limit integer DEFAULT 5)
RETURNS TABLE (customer_name text, total_spent numeric, order_count bigint)
LANGUAGE sql
STABLE
AS $$
    SELECT c.name, sum(i.quantity * i.unit_price), count(DISTINCT o.id)
    FROM sales.customers AS c
    JOIN sales.orders AS o ON o.customer_id = c.id
    JOIN sales.order_items AS i ON i.order_id = o.id
    WHERE o.status = 'paid'
    GROUP BY c.name
    ORDER BY 2 DESC
    LIMIT p_limit;
$$;

COMMIT;
