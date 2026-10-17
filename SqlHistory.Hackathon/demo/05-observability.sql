-- Optional demo: data and objects for the performance, query-plan and tracing features.
-- Safe to run more than once. Does not modify any routine created by 01-setup.sql.
--
-- * Bulk order history dated 2025 (2026 demo results stay the same), so plans have
--   realistic shapes: with a handful of rows every plan is a trivial sequential scan.
-- * Indexes on the foreign keys: order_items lookups by order become index scans, while
--   lookups by product stay sequential scans.
-- * public.get_all_users(), called by the DemoApi (SqlHistory.Hackathon/DemoApi).

DO $$
BEGIN
    IF current_database() <> 'sqlhistory_demo' THEN
        RAISE EXCEPTION 'Refusing to run outside the sqlhistory_demo database (connected to %).', current_database();
    END IF;
END
$$;

BEGIN;

INSERT INTO sales.customers (name, country)
SELECT 'Customer ' || g, (ARRAY['US', 'DE', 'GE', 'JP', 'FR'])[1 + g % 5]
FROM generate_series(1, 2000) AS g
WHERE NOT EXISTS (SELECT 1 FROM sales.customers WHERE name = 'Customer 1');

INSERT INTO sales.orders (customer_id, status, created_at)
SELECT c.id,
       (ARRAY['paid', 'paid', 'paid', 'pending', 'cancelled', 'refunded'])[1 + g % 6],
       timestamptz '2025-01-01' + (g % 365) * interval '1 day' + (g % 1440) * interval '1 minute'
FROM generate_series(1, 100000) AS g
JOIN sales.customers AS c ON c.name = 'Customer ' || (1 + g % 2000)
WHERE NOT EXISTS (SELECT 1 FROM sales.orders WHERE created_at < '2026-01-01');

INSERT INTO sales.order_items (order_id, product, quantity, unit_price)
SELECT o.id,
       (ARRAY['Keyboard', 'Mouse', 'Monitor', 'Laptop', 'Headset', 'Webcam', 'Dock'])[1 + (o.id * 7 + n) % 7],
       1 + (o.id + n) % 4,
       (ARRAY[49.90, 19.90, 229.00, 1299.00, 79.00, 59.00, 189.00])[1 + (o.id * 7 + n) % 7]
FROM sales.orders AS o
CROSS JOIN generate_series(1, 2) AS n
WHERE o.created_at < '2026-01-01'
  AND NOT EXISTS (SELECT 1 FROM sales.order_items AS i WHERE i.order_id = o.id);

CREATE INDEX IF NOT EXISTS order_items_order_id_idx ON sales.order_items (order_id);
CREATE INDEX IF NOT EXISTS orders_customer_id_idx ON sales.orders (customer_id);
CREATE INDEX IF NOT EXISTS orders_created_at_idx ON sales.orders (created_at);

-- PL/pgSQL on purpose: it is never inlined, so pg_stat_user_functions (track_functions) counts
-- every call. Its query plan is opaque to EXPLAIN: callers only see a "Function Scan".
CREATE OR REPLACE FUNCTION public.get_all_users()
RETURNS TABLE (id integer, email text, is_active boolean)
LANGUAGE plpgsql
STABLE
AS $$
BEGIN
    RETURN QUERY
    SELECT u.id, u.email, u.is_active
    FROM public.users AS u
    ORDER BY u.id;
END;
$$;

COMMIT;

ANALYZE sales.customers;
ANALYZE sales.orders;
ANALYZE sales.order_items;

SELECT 'orders: ' || count(*) FROM sales.orders;
