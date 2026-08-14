\set ON_ERROR_STOP on

BEGIN;

CREATE OR REPLACE FUNCTION pg_temp.demo_uuid(value text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT (substring(md5(value), 1, 8) || '-' ||
            substring(md5(value), 9, 4) || '-' ||
            substring(md5(value), 13, 4) || '-' ||
            substring(md5(value), 17, 4) || '-' ||
            substring(md5(value), 21, 12))::uuid;
$$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pos.store) THEN
        RAISE EXCEPTION 'La tienda no esta configurada. Completa primero la configuracion inicial.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pos.user_account WHERE "IsActive") THEN
        RAISE EXCEPTION 'No existe un usuario activo para asociar los datos de prueba.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pos.register WHERE "IsActive") THEN
        RAISE EXCEPTION 'No existe una caja activa para asociar los datos de prueba.';
    END IF;
END $$;

-- Los limites se derivan de la existencia actual y de la unidad de venta.
UPDATE pos.product
SET "MinimumStock" = CASE
        WHEN upper(coalesce("UnitOfMeasure", '')) = 'GRANEL' THEN 2.000
        WHEN "Stock" > 0 THEN greatest(1.000, ceil("Stock" * 0.20))
        ELSE 5.000
    END
WHERE "MinimumStock" <= 0;

UPDATE pos.product
SET "MaximumStock" = greatest(
        "MinimumStock" * 4,
        CASE WHEN "Stock" > 0 THEN ceil("Stock" * 1.50) ELSE 20.000 END,
        20.000)
WHERE "MaximumStock" <= 0 OR "MaximumStock" < "MinimumStock";

CREATE TEMP TABLE demo_context AS
SELECT
    (SELECT "Id" FROM pos.user_account WHERE "IsActive" ORDER BY "IsAdministrator" DESC, "CreatedAtUtc" LIMIT 1) AS user_id,
    (SELECT "Id" FROM pos.register WHERE "IsActive" ORDER BY "Name" LIMIT 1) AS register_id;

INSERT INTO pos.customer ("Id", "Name", "Phone", "Email", "TaxId", "CreditLimit", "CreditEnabled", "IsActive", "CreatedAtUtc")
SELECT
    pg_temp.demo_uuid('jetventa-demo-customer-' || number),
    names[number],
    '55' || lpad((10000000 + number * 7919)::text, 8, '0'),
    'cliente' || number || '@ejemplo.mx',
    '',
    CASE WHEN number % 3 = 0 THEN 3000.00 ELSE 0.00 END,
    number % 3 = 0,
    true,
    now() - (number || ' months')::interval
FROM generate_series(1, 12) AS number
CROSS JOIN (SELECT ARRAY['Maria Gonzalez','Jose Hernandez','Ana Martinez','Luis Garcia','Carmen Lopez','Jorge Ramirez','Laura Sanchez','Miguel Torres','Patricia Flores','Carlos Diaz','Rosa Mendoza','Daniel Ortiz'] AS names) source
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO pos.supplier ("Id", "Name", "Phone", "Email", "CreatedAtUtc")
SELECT
    pg_temp.demo_uuid('jetventa-demo-supplier-' || number),
    names[number],
    '55' || lpad((20000000 + number * 3571)::text, 8, '0'),
    'ventas' || number || '@proveedor-ejemplo.mx',
    now() - interval '2 years'
FROM generate_series(1, 5) AS number
CROSS JOIN (SELECT ARRAY['Distribuidora Centro','Abarrotes del Valle','Bebidas Nacionales','Limpieza y Hogar','Dulces y Botanas MX'] AS names) source
ON CONFLICT ("Id") DO NOTHING;

WITH ranked_products AS (
    SELECT "Id", row_number() OVER (ORDER BY "Description", "Id") AS position
    FROM pos.product
    WHERE "IsActive" AND "PrimarySupplierId" IS NULL
)
UPDATE pos.product product
SET "PrimarySupplierId" = pg_temp.demo_uuid('jetventa-demo-supplier-' || (((ranked.position - 1) % 5) + 1))
FROM ranked_products ranked
WHERE product."Id" = ranked."Id" AND ranked.position <= 500;

CREATE TEMP TABLE demo_days AS
SELECT
    day::date AS business_date,
    pg_temp.demo_uuid('jetventa-demo-shift-' || day::date) AS shift_id,
    6 + (abs(('x' || substring(md5(day::date::text), 1, 8))::bit(32)::int) % 7) AS sale_count
FROM generate_series(current_date - 364, current_date, interval '1 day') day;

INSERT INTO pos.shift ("Id", "RegisterId", "UserId", "InitialCash", "Status", "OpenedAtUtc", "ClosedAtUtc", "CountedCash", "Difference")
SELECT
    days.shift_id,
    context.register_id,
    context.user_id,
    1000.00,
    'Closed',
    days.business_date::timestamptz + interval '8 hours',
    days.business_date::timestamptz + interval '21 hours',
    1000.00,
    0.00
FROM demo_days days CROSS JOIN demo_context context
ON CONFLICT ("Id") DO NOTHING;

CREATE TEMP TABLE demo_sales AS
SELECT
    pg_temp.demo_uuid('jetventa-demo-sale-' || days.business_date || '-' || sale_number) AS sale_id,
    pg_temp.demo_uuid('jetventa-demo-sale-operation-' || days.business_date || '-' || sale_number) AS operation_id,
    days.shift_id,
    CASE WHEN sale_number % 5 = 0 THEN pg_temp.demo_uuid('jetventa-demo-customer-' || (((sale_number + extract(doy FROM days.business_date)::int) % 12) + 1)) END AS customer_id,
    days.business_date::timestamptz + interval '9 hours' + ((sale_number - 1) * interval '55 minutes') AS sold_at,
    1 + (abs(('x' || substring(md5(days.business_date::text || '-' || sale_number), 1, 8))::bit(32)::int) % 4) AS line_count,
    abs(('x' || substring(md5('payment-' || days.business_date::text || '-' || sale_number), 1, 8))::bit(32)::int) % 100 AS payment_selector
FROM demo_days days
CROSS JOIN LATERAL generate_series(1, days.sale_count) sale_number;

CREATE TEMP TABLE demo_products AS
SELECT "Id", "Price", "Stock", upper(coalesce("UnitOfMeasure", 'PIEZA')) AS unit, row_number() OVER (ORDER BY "Description", "Id") AS position
FROM pos.product
WHERE "IsActive" AND "Price" > 0
ORDER BY "Description", "Id"
LIMIT 350;

CREATE TEMP TABLE demo_raw_lines AS
SELECT DISTINCT ON (sales.sale_id, products."Id")
    sales.sale_id,
    sales.sold_at,
    products."Id" AS product_id,
    products."Price" AS unit_price,
    products."Stock" AS current_stock,
    CASE
        WHEN products.unit = 'GRANEL' THEN ((1 + (abs(('x' || substring(md5(sales.sale_id::text || '-' || line_number), 1, 8))::bit(32)::int) % 8)) * 0.250)::numeric(18,3)
        ELSE (1 + (abs(('x' || substring(md5(sales.sale_id::text || '-' || line_number), 1, 8))::bit(32)::int) % 5))::numeric(18,3)
    END AS quantity
FROM demo_sales sales
CROSS JOIN LATERAL generate_series(1, sales.line_count) line_number
JOIN demo_products products
  ON products.position = 1 + (abs(('x' || substring(md5('product-' || sales.sale_id::text || '-' || line_number), 1, 8))::bit(32)::int) % (SELECT count(*) FROM demo_products));

CREATE TEMP TABLE demo_lines AS
SELECT
    raw.*,
    raw.current_stock + coalesce(sum(raw.quantity) OVER (
        PARTITION BY raw.product_id
        ORDER BY raw.sold_at, raw.sale_id
        ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING), 0) + raw.quantity AS stock_before,
    raw.current_stock + coalesce(sum(raw.quantity) OVER (
        PARTITION BY raw.product_id
        ORDER BY raw.sold_at, raw.sale_id
        ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING), 0) AS stock_after
FROM demo_raw_lines raw;

INSERT INTO pos.sale ("Id", "OperationId", "ShiftId", "CustomerId", "Total", "Status", "CreatedAtUtc")
SELECT sales.sale_id, sales.operation_id, sales.shift_id, sales.customer_id, round(sum(lines.quantity * lines.unit_price), 2), 'Completed', sales.sold_at
FROM demo_sales sales
JOIN demo_lines lines ON lines.sale_id = sales.sale_id
GROUP BY sales.sale_id, sales.operation_id, sales.shift_id, sales.customer_id, sales.sold_at
ON CONFLICT ("OperationId") DO NOTHING;

INSERT INTO pos.sale_line ("Id", "SaleId", "ProductId", "Quantity", "UnitPrice", "LineTotal", "StockBefore", "StockAfter")
SELECT
    pg_temp.demo_uuid('jetventa-demo-line-' || lines.sale_id || '-' || lines.product_id),
    lines.sale_id,
    lines.product_id,
    lines.quantity,
    lines.unit_price,
    round(lines.quantity * lines.unit_price, 2),
    lines.stock_before,
    lines.stock_after
FROM demo_lines lines
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO pos.payment ("Id", "SaleId", "Method", "Amount", "Received", "Change")
SELECT
    pg_temp.demo_uuid('jetventa-demo-payment-' || sales.sale_id),
    sales.sale_id,
    CASE WHEN sales.payment_selector < 78 THEN 'Cash' WHEN sales.payment_selector < 92 THEN 'Card' ELSE 'Transfer' END,
    sale."Total",
    CASE WHEN sales.payment_selector < 78 THEN ceil(sale."Total" / 50.00) * 50.00 ELSE 0.00 END,
    CASE WHEN sales.payment_selector < 78 THEN ceil(sale."Total" / 50.00) * 50.00 - sale."Total" ELSE 0.00 END
FROM demo_sales sales
JOIN pos.sale sale ON sale."Id" = sales.sale_id
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO pos.inventory_movement ("Id", "ProductId", "SaleId", "OperationId", "UserId", "Quantity", "StockBefore", "StockAfter", "Reason", "CreatedAtUtc")
SELECT
    pg_temp.demo_uuid('jetventa-demo-inventory-' || lines.sale_id || '-' || lines.product_id),
    lines.product_id,
    lines.sale_id,
    pg_temp.demo_uuid('jetventa-demo-inventory-operation-' || lines.sale_id || '-' || lines.product_id),
    context.user_id,
    -lines.quantity,
    lines.stock_before,
    lines.stock_after,
    'Venta de prueba',
    lines.sold_at
FROM demo_lines lines CROSS JOIN demo_context context
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO pos.print_job ("Id", "SaleId", "Status", "Attempts", "CreatedAtUtc", "CompletedAtUtc")
SELECT pg_temp.demo_uuid('jetventa-demo-print-' || sale_id), sale_id, 'Completed', 1, sold_at, sold_at + interval '3 seconds'
FROM demo_sales
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO pos.cash_movement ("Id", "ShiftId", "Type", "Amount", "Reason", "CreatedAtUtc")
SELECT pg_temp.demo_uuid('jetventa-demo-cash-out-' || business_date), shift_id, 'Out', 35.00 + (extract(doy FROM business_date)::int % 5) * 10.00, 'Gasto operativo de prueba', business_date::timestamptz + interval '15 hours'
FROM demo_days
WHERE extract(dow FROM business_date)::int IN (2, 5)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO pos.cash_movement ("Id", "ShiftId", "Type", "Amount", "Reason", "CreatedAtUtc")
SELECT pg_temp.demo_uuid('jetventa-demo-cash-in-' || business_date), shift_id, 'In', 200.00, 'Cambio adicional de prueba', business_date::timestamptz + interval '12 hours'
FROM demo_days
WHERE extract(dow FROM business_date)::int = 1
ON CONFLICT ("Id") DO NOTHING;

-- Recalcula el corte histórico con efectivo real y movimientos de cada turno.
WITH totals AS (
    SELECT
        shift."Id" AS shift_id,
        shift."InitialCash"
            + coalesce((SELECT sum(payment."Amount") FROM pos.sale sale JOIN pos.payment payment ON payment."SaleId" = sale."Id" WHERE sale."ShiftId" = shift."Id" AND sale."Status" = 'Completed' AND payment."Method" = 'Cash'), 0)
            + coalesce((SELECT sum(movement."Amount") FROM pos.cash_movement movement WHERE movement."ShiftId" = shift."Id" AND movement."Type" = 'In'), 0)
            - coalesce((SELECT sum(movement."Amount") FROM pos.cash_movement movement WHERE movement."ShiftId" = shift."Id" AND movement."Type" = 'Out'), 0) AS expected,
        extract(doy FROM shift."OpenedAtUtc")::int AS day_number
    FROM pos.shift shift
    JOIN demo_days days ON days.shift_id = shift."Id"
)
UPDATE pos.shift shift
SET "CountedCash" = round(totals.expected + ((totals.day_number % 5) - 2) * 5.00, 2),
    "Difference" = ((totals.day_number % 5) - 2) * 5.00
FROM totals
WHERE shift."Id" = totals.shift_id;

CREATE TEMP TABLE demo_purchases AS
SELECT
    month_number,
    pg_temp.demo_uuid('jetventa-demo-purchase-' || month_number) AS purchase_id,
    pg_temp.demo_uuid('jetventa-demo-purchase-operation-' || month_number) AS operation_id,
    pg_temp.demo_uuid('jetventa-demo-supplier-' || (((month_number - 1) % 5) + 1)) AS supplier_id,
    current_date - (month_number || ' months')::interval + interval '10 hours' AS purchased_at
FROM generate_series(1, 24) month_number;

INSERT INTO pos.purchase ("Id", "OperationId", "SupplierId", "UserId", "Total", "Status", "CreatedAtUtc")
SELECT purchases.purchase_id, purchases.operation_id, purchases.supplier_id, context.user_id, 0.00, 'Received', purchases.purchased_at
FROM demo_purchases purchases CROSS JOIN demo_context context
ON CONFLICT ("OperationId") DO NOTHING;

INSERT INTO pos.purchase_line ("Id", "PurchaseId", "ProductId", "Quantity", "UnitCost", "LineTotal")
SELECT
    pg_temp.demo_uuid('jetventa-demo-purchase-line-' || purchases.purchase_id || '-' || products."Id"),
    purchases.purchase_id,
    products."Id",
    12.000,
    greatest(round(products."Price" * 0.70, 2), 0.01),
    greatest(round(products."Price" * 0.70, 2), 0.01) * 12.000
FROM demo_purchases purchases
JOIN demo_products products ON products.position BETWEEN ((purchases.month_number - 1) * 5 + 1) AND ((purchases.month_number - 1) * 5 + 5)
ON CONFLICT ("Id") DO NOTHING;

UPDATE pos.purchase purchase
SET "Total" = totals.total
FROM (SELECT "PurchaseId", round(sum("LineTotal"), 2) AS total FROM pos.purchase_line GROUP BY "PurchaseId") totals
WHERE purchase."Id" = totals."PurchaseId" AND purchase."Id" IN (SELECT purchase_id FROM demo_purchases);

COMMIT;

SELECT 'Productos con limites completos' AS concepto, count(*) AS total
FROM pos.product
WHERE "MinimumStock" > 0 AND "MaximumStock" >= "MinimumStock"
UNION ALL
SELECT 'Ventas', count(*) FROM pos.sale
UNION ALL
SELECT 'Partidas', count(*) FROM pos.sale_line
UNION ALL
SELECT 'Turnos cerrados', count(*) FROM pos.shift WHERE "Status" = 'Closed'
UNION ALL
SELECT 'Clientes', count(*) FROM pos.customer
UNION ALL
SELECT 'Compras', count(*) FROM pos.purchase;
