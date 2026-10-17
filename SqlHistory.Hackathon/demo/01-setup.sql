-- SQL Time Machine demo: sample tables, data and routines.
-- Idempotent: running it again resets the demo objects to their baseline.
--
--   psql -h localhost -U <user> -d sqlhistory_demo -v ON_ERROR_STOP=1 -f 01-setup.sql

DO $$
BEGIN
    IF current_database() <> 'sqlhistory_demo' THEN
        RAISE EXCEPTION 'Refusing to run outside the sqlhistory_demo database (connected to %).', current_database();
    END IF;
END
$$;

BEGIN;

-- Only objects created by this script are removed.
DROP SCHEMA IF EXISTS sales CASCADE;
DROP SCHEMA IF EXISTS analytics CASCADE;
DROP FUNCTION IF EXISTS public.active_users();
DROP TABLE IF EXISTS public.users;

CREATE SCHEMA sales;
CREATE SCHEMA analytics;

-- ---------- tables ----------

CREATE TABLE public.users (
    id         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email      text NOT NULL UNIQUE,
    is_active  boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE sales.customers (
    id      integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name    text NOT NULL,
    country text NOT NULL
);

CREATE TABLE sales.orders (
    id          integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id integer NOT NULL REFERENCES sales.customers (id),
    status      text NOT NULL CHECK (status IN ('pending', 'paid', 'cancelled', 'refunded')),
    created_at  timestamptz NOT NULL
);

CREATE TABLE sales.order_items (
    order_id   integer NOT NULL REFERENCES sales.orders (id),
    product    text NOT NULL,
    quantity   integer NOT NULL CHECK (quantity > 0),
    unit_price numeric(10, 2) NOT NULL CHECK (unit_price >= 0)
);

CREATE TABLE sales.payments (
    id       integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id integer NOT NULL REFERENCES sales.orders (id),
    amount   numeric(10, 2) NOT NULL,
    paid_at  timestamptz NOT NULL DEFAULT now()
);

-- ---------- data ----------

INSERT INTO public.users (email, is_active) VALUES
    ('ana@example.com', true),
    ('ben@example.com', true),
    ('chen@example.com', false),
    ('dana@example.com', true),
    ('eli@example.com', false),
    ('fatima@example.com', true),
    ('giorgi@example.com', true),
    ('hana@example.com', false);

INSERT INTO sales.customers (name, country) VALUES
    ('Acme Corp', 'US'),
    ('Globex', 'DE'),
    ('Initech', 'GE'),
    ('Umbrella', 'JP');

INSERT INTO sales.orders (customer_id, status, created_at) VALUES
    (1, 'paid',      '2026-01-14'),
    (2, 'paid',      '2026-01-20'),
    (3, 'cancelled', '2026-02-02'),
    (1, 'paid',      '2026-02-11'),
    (4, 'refunded',  '2026-02-25'),
    (2, 'pending',   '2026-03-03'),
    (3, 'paid',      '2026-03-18');

INSERT INTO sales.order_items (order_id, product, quantity, unit_price) VALUES
    (1, 'Keyboard', 2, 49.90),
    (1, 'Mouse', 2, 19.90),
    (2, 'Monitor', 1, 229.00),
    (3, 'Laptop', 1, 1299.00),
    (4, 'Docking station', 1, 189.00),
    (5, 'Laptop', 2, 1299.00),
    (6, 'Headset', 3, 79.00),
    (7, 'Webcam', 4, 59.00);

INSERT INTO sales.payments (order_id, amount, paid_at) VALUES
    (1, 139.60, '2026-01-14'),
    (2, 229.00, '2026-01-21'),
    (4, 189.00, '2026-02-11'),
    (7, 236.00, '2026-03-19');

-- ---------- routines ----------

-- Demo step 1: the baseline. 02-remove-active-filter.sql removes the WHERE clause.
CREATE FUNCTION public.active_users()
RETURNS bigint
LANGUAGE sql
STABLE
AS $$
    SELECT count(*)
    FROM public.users
    WHERE is_active = true;
$$;

-- Two overloads of the same name: each gets its own history.
CREATE FUNCTION sales.order_total(p_order_id integer)
RETURNS numeric
LANGUAGE sql
STABLE
AS $$
    SELECT coalesce(sum(quantity * unit_price), 0)
    FROM sales.order_items
    WHERE order_id = p_order_id;
$$;

CREATE FUNCTION sales.order_total(p_order_id integer, p_tax_rate numeric)
RETURNS numeric
LANGUAGE sql
STABLE
AS $$
    SELECT round(sales.order_total(p_order_id) * (1 + p_tax_rate), 2);
$$;

CREATE PROCEDURE sales.record_payment(p_order_id integer, p_amount numeric)
LANGUAGE plpgsql
AS $$
DECLARE
    v_paid numeric;
BEGIN
    IF p_amount <= 0 THEN
        RAISE EXCEPTION 'Payment amount must be positive, got %', p_amount;
    END IF;

    INSERT INTO sales.payments (order_id, amount) VALUES (p_order_id, p_amount);

    SELECT coalesce(sum(amount), 0) INTO v_paid
    FROM sales.payments
    WHERE order_id = p_order_id;

    IF v_paid >= sales.order_total(p_order_id) THEN
        UPDATE sales.orders SET status = 'paid' WHERE id = p_order_id AND status = 'pending';
    END IF;
END;
$$;

CREATE FUNCTION analytics.monthly_revenue(p_year integer)
RETURNS TABLE (month integer, revenue numeric)
LANGUAGE sql
STABLE
AS $$
    SELECT extract(month FROM o.created_at)::integer AS month,
           sum(i.quantity * i.unit_price) AS revenue
    FROM sales.orders AS o
    JOIN sales.order_items AS i ON i.order_id = o.id
    WHERE o.status = 'paid'
      AND extract(year FROM o.created_at) = p_year
    GROUP BY 1
    ORDER BY 1;
$$;

CREATE FUNCTION analytics.top_customers(p_limit integer DEFAULT 5)
RETURNS TABLE (customer_name text, total_spent numeric)
LANGUAGE sql
STABLE
AS $$
    SELECT c.name, sum(i.quantity * i.unit_price)
    FROM sales.customers AS c
    JOIN sales.orders AS o ON o.customer_id = c.id
    JOIN sales.order_items AS i ON i.order_id = o.id
    WHERE o.status = 'paid'
    GROUP BY c.name
    ORDER BY 2 DESC
    LIMIT p_limit;
$$;

COMMIT;

SELECT 'Demo ready: active_users() = ' || public.active_users() AS result;
