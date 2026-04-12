using FluentAssertions;

namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Defines a multi-version database schema evolution audit scenario that forces the model to diff five consecutive DDL
/// snapshots, classify breaking vs. non-breaking changes per a migration-rules policy, detect ORM entity drift,
/// and produce per-hop migration plans cross-referenced against a registered consumer registry.
/// </summary>
internal static class DatabaseSchemaEvolutionAuditTask
{
    private const string CompletionMarker = "DB_SCHEMA_EVOLUTION_AUDIT_COMPLETE";

    /// <summary>
    /// Creates the database schema evolution audit task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "DatabaseSchemaEvolutionAudit",
        conversationName: "e2e-db-schema-evolution-audit",
        systemPrompt:
            "You are a database migration analyst running inside a TokenGuard E2E test. " +
            "Your job is to compare five database schema versions, classify every DDL change according to the " +
            "provided migration rules, detect ORM entity drift, and produce a structured set of migration and " +
            "impact artefacts. " +
            "You MUST use the provided tools for every file operation — do not invent table names, column names, " +
            "or consumer details without reading the actual source files first. " +
            "Work through every schema version pair methodically: read both schemas, read the matching ORM files, " +
            "diff them, then write the output. " +
            "When all artefacts are complete, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: perform a full database schema evolution audit across five schema versions.\n\n" +
            "Step 1 – list all files in the workspace, then read every source file before producing any output:\n" +
            "  schema-v1.sql, schema-v2.sql, schema-v3.sql, schema-v4.sql, schema-v5.sql,\n" +
            "  orm-v1.txt, orm-v2.txt, orm-v3.txt, orm-v4.txt, orm-v5.txt,\n" +
            "  migration-rules.txt, consumer-registry.txt.\n\n" +
            "Step 2 – for each consecutive version pair, create a schema diff report using the exact filenames below.\n" +
            "  Each diff report must contain four sections:\n" +
            "    ## Added – new tables, columns, or indexes not present in the previous version.\n" +
            "    ## Removed – tables, columns, or indexes that existed in the previous version but are gone.\n" +
            "    ## Changed – tables or columns that exist in both versions but differ (type, nullability, default, constraint).\n" +
            "    ## Classification – for every entry in Removed or Changed, label it BREAKING or NON-BREAKING per migration-rules.txt.\n" +
            "  Filenames: schema-diff-v1-to-v2.md, schema-diff-v2-to-v3.md, schema-diff-v3-to-v4.md, schema-diff-v4-to-v5.md.\n\n" +
            "Step 3 – for each version, compare schema-v{n}.sql against orm-v{n}.txt.\n" +
            "  Detect any table or column present in the schema but missing or misnamed in the ORM (or vice versa).\n" +
            "  Create 'orm-drift-report.md' with one section per version (## Version N).\n" +
            "  Each section must list every drift finding as: DRIFT | table.column | schema_name vs. orm_name.\n" +
            "  Versions with no drift must still appear with the note 'No drift detected.'\n\n" +
            "Step 4 – create 'migration-plan.md' with one section per version hop (v1→v2, v2→v3, v3→v4, v4→v5).\n" +
            "  Each section must list the concrete DBA steps required to migrate a live database from the prior version.\n" +
            "  Steps must reference the exact column and table names from the schema files.\n" +
            "  Hops with no breaking changes must still appear and state 'No breaking changes; migration is additive.'\n\n" +
            "Step 5 – create 'consumer-impact-matrix.md' with one section per consumer from consumer-registry.txt.\n" +
            "  Each section must list every BREAKING change from Steps 2–3 that affects that consumer's pinned version\n" +
            "  and the specific tables or columns they use.\n" +
            "  Consumers unaffected by any breaking change must still appear with the note 'No impact detected.'\n\n" +
            "Step 6 – create 'breaking-changes-summary.md' — a chronological list of every BREAKING change across\n" +
            "  all four version hops, with: version hop, affected table and column, change type, and description.\n\n" +
            "Step 7 – read back each of the six output files to confirm they contain the expected content.\n" +
            "  Do not claim completion until all six output files exist and are verified.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync,
        size: TaskSize.ExtraLarge);

    /// <summary>
    /// Seeds the workspace with five verbose DDL schema files, five ORM entity snapshots, a migration-rules
    /// policy document, and a consumer registry.
    /// </summary>
    private static async Task SeedAsync(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "schema-v1.sql"), SchemaV1);
        await File.WriteAllTextAsync(Path.Combine(dir, "schema-v2.sql"), SchemaV2);
        await File.WriteAllTextAsync(Path.Combine(dir, "schema-v3.sql"), SchemaV3);
        await File.WriteAllTextAsync(Path.Combine(dir, "schema-v4.sql"), SchemaV4);
        await File.WriteAllTextAsync(Path.Combine(dir, "schema-v5.sql"), SchemaV5);
        await File.WriteAllTextAsync(Path.Combine(dir, "orm-v1.txt"), OrmV1);
        await File.WriteAllTextAsync(Path.Combine(dir, "orm-v2.txt"), OrmV2);
        await File.WriteAllTextAsync(Path.Combine(dir, "orm-v3.txt"), OrmV3);
        await File.WriteAllTextAsync(Path.Combine(dir, "orm-v4.txt"), OrmV4);
        await File.WriteAllTextAsync(Path.Combine(dir, "orm-v5.txt"), OrmV5);
        await File.WriteAllTextAsync(Path.Combine(dir, "migration-rules.txt"), MigrationRules);
        await File.WriteAllTextAsync(Path.Combine(dir, "consumer-registry.txt"), ConsumerRegistry);
    }

    /// <summary>
    /// Verifies that all six output artefacts were produced and contain the expected structural markers.
    /// </summary>
    private static async Task AssertAsync(string dir, string? finalText)
    {
        var diff12 = await File.ReadAllTextAsync(Path.Combine(dir, "schema-diff-v1-to-v2.md"));
        var diff23 = await File.ReadAllTextAsync(Path.Combine(dir, "schema-diff-v2-to-v3.md"));
        var diff34 = await File.ReadAllTextAsync(Path.Combine(dir, "schema-diff-v3-to-v4.md"));
        var diff45 = await File.ReadAllTextAsync(Path.Combine(dir, "schema-diff-v4-to-v5.md"));
        var ormDrift = await File.ReadAllTextAsync(Path.Combine(dir, "orm-drift-report.md"));
        var migPlan = await File.ReadAllTextAsync(Path.Combine(dir, "migration-plan.md"));
        var consumerMatrix = await File.ReadAllTextAsync(Path.Combine(dir, "consumer-impact-matrix.md"));
        var breakingSummary = await File.ReadAllTextAsync(Path.Combine(dir, "breaking-changes-summary.md"));

        // v1 → v2: reviews table added, tags column added — all non-breaking
        diff12.Should().Contain("## Added", because: "schema-diff-v1-to-v2.md must have an Added section");
        diff12.Should().Contain("reviews", because: "the reviews table was added in v2 and must appear in the diff");
        diff12.Should().Contain("tags", because: "the tags column was added to products in v2");
        diff12.Should().Contain("shipping_address", because: "shipping_address was added to orders in v2");
        diff12.Should().NotContain("BREAKING", because: "all v1→v2 changes are non-breaking additions");

        // v2 → v3: phone→phone_number rename (BREAKING), categories.sort_order dropped (BREAKING), audit_log added
        diff23.Should().Contain("BREAKING", because: "schema-diff-v2-to-v3.md must classify breaking changes");
        diff23.Should().Contain("phone_number", because: "the phone→phone_number rename is a breaking change in v3");
        diff23.Should().Contain("sort_order", because: "categories.sort_order was dropped in v3, a breaking change");
        diff23.Should().Contain("audit_log", because: "the audit_log table was added in v3");

        // v3 → v4: orders.status VARCHAR→ENUM (BREAKING), inventory.qty→quantity (BREAKING), products.weight added
        diff34.Should().Contain("BREAKING", because: "schema-diff-v3-to-v4.md must classify breaking changes");
        diff34.Should().Contain("status", because: "the orders.status type change must appear in the diff");
        diff34.Should().Contain("quantity", because: "the inventory.qty→quantity rename is a breaking change in v4");
        diff34.Should().Contain("weight", because: "products.weight was added in v4 as a non-breaking addition");

        // v4 → v5: products.legacy_code dropped (BREAKING), shipping_address split (BREAKING), users.mfa_enabled added
        diff45.Should().Contain("BREAKING", because: "schema-diff-v4-to-v5.md must classify breaking changes");
        diff45.Should().Contain("legacy_code", because: "products.legacy_code was dropped in v5, a breaking change");
        diff45.Should().Contain("shipping_street", because: "shipping_address was split into shipping_street in v5");
        diff45.Should().Contain("mfa_enabled", because: "users.mfa_enabled was added in v5 as a non-breaking addition");

        // ORM drift: v3 ORM has Phone not PhoneNumber; v4 ORM has Qty not Quantity
        ormDrift.Should().Contain("Version 3", because: "orm-drift-report.md must have a section for Version 3");
        ormDrift.Should().Contain("phone_number", because: "orm-v3.txt still maps to 'Phone' causing drift on phone_number");
        ormDrift.Should().Contain("Version 4", because: "orm-drift-report.md must have a section for Version 4");
        ormDrift.Should().Contain("quantity", because: "orm-v4.txt still maps to 'Qty' causing drift on quantity");

        // Migration plan covers all hops
        migPlan.Should().Contain("v1", because: "migration-plan.md must cover the v1→v2 hop");
        migPlan.Should().Contain("v2", because: "migration-plan.md must cover the v2→v3 hop");
        migPlan.Should().Contain("phone_number", because: "migration plan must describe the phone rename step");
        migPlan.Should().Contain("quantity", because: "migration plan must describe the qty→quantity rename step");

        // Consumer impact
        consumerMatrix.Should().Contain("ConsumerA", because: "consumer-impact-matrix.md must include every registered consumer");
        consumerMatrix.Should().Contain("ConsumerD", because: "ConsumerD uses categories.sort_order which was dropped in v3");
        consumerMatrix.Should().Contain("ConsumerF", because: "ConsumerF uses orders.shipping_address which was split in v5");
        consumerMatrix.Should().Contain("ConsumerB", because: "ConsumerB uses users.phone which was renamed in v3");

        // Breaking changes summary
        breakingSummary.Should().Contain("BREAKING", because: "breaking-changes-summary.md must list breaking changes");
        breakingSummary.Should().Contain("phone_number", because: "the phone rename must appear in the summary");
        breakingSummary.Should().Contain("sort_order", because: "the sort_order removal must appear in the summary");
        breakingSummary.Should().Contain("legacy_code", because: "the legacy_code removal must appear in the summary");
        breakingSummary.Should().Contain("quantity", because: "the qty rename must appear in the summary");

        finalText.Should().Contain(CompletionMarker, because: "the final model message must contain the completion marker");
    }

    // -------------------------------------------------------------------------
    // Schema DDL source files
    // -------------------------------------------------------------------------

    private static readonly string SchemaV1 =
        "-- ============================================================\n" +
        "-- Database Schema – Version 1.0\n" +
        "-- Released: 2023-01-20\n" +
        "-- Engine: PostgreSQL 15\n" +
        "-- Description: Initial production schema. Establishes core\n" +
        "--   domain tables for users, products, orders, inventory,\n" +
        "--   and categories. All foreign keys enforced at DB level.\n" +
        "-- ============================================================\n\n" +

        "CREATE TABLE users (\n" +
        "  id            UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  email         VARCHAR(320)  NOT NULL,\n" +
        "  name          VARCHAR(200)  NOT NULL,\n" +
        "  phone         VARCHAR(30)   NULL,\n" +
        "  role          VARCHAR(20)   NOT NULL DEFAULT 'viewer',\n" +
        "  password_hash VARCHAR(128)  NOT NULL,\n" +
        "  is_active     BOOLEAN       NOT NULL DEFAULT TRUE,\n" +
        "  created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_users PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_users_email UNIQUE (email),\n" +
        "  CONSTRAINT ck_users_role CHECK (role IN ('admin','editor','viewer'))\n" +
        ");\n" +
        "COMMENT ON TABLE  users IS 'Registered platform users.';\n" +
        "COMMENT ON COLUMN users.phone IS 'Optional contact phone; E.164 format preferred.';\n" +
        "COMMENT ON COLUMN users.role  IS 'Access level: admin > editor > viewer.';\n\n" +

        "CREATE INDEX ix_users_email ON users (email);\n" +
        "CREATE INDEX ix_users_role  ON users (role);\n\n" +

        "CREATE TABLE categories (\n" +
        "  id         UUID         NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name       VARCHAR(100) NOT NULL,\n" +
        "  parent_id  UUID         NULL REFERENCES categories (id) ON DELETE SET NULL,\n" +
        "  sort_order INTEGER      NOT NULL DEFAULT 0,\n" +
        "  created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_categories PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_categories_name UNIQUE (name)\n" +
        ");\n" +
        "COMMENT ON TABLE  categories IS 'Two-level product category hierarchy.';\n" +
        "COMMENT ON COLUMN categories.sort_order IS 'Display ordering hint for UI renderers.';\n\n" +

        "CREATE TABLE products (\n" +
        "  id          UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name        VARCHAR(200)  NOT NULL,\n" +
        "  description TEXT          NULL,\n" +
        "  price       NUMERIC(12,2) NOT NULL,\n" +
        "  legacy_code VARCHAR(50)   NULL,\n" +
        "  category_id UUID          NOT NULL REFERENCES categories (id),\n" +
        "  created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_products PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_products_price CHECK (price >= 0)\n" +
        ");\n" +
        "COMMENT ON TABLE  products IS 'Product catalogue entries.';\n" +
        "COMMENT ON COLUMN products.legacy_code IS 'Legacy ERP code; retained for backward compatibility only.';\n\n" +

        "CREATE INDEX ix_products_category ON products (category_id);\n\n" +

        "CREATE TABLE inventory (\n" +
        "  id         UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  warehouse  VARCHAR(50) NOT NULL,\n" +
        "  qty        INTEGER     NOT NULL DEFAULT 0,\n" +
        "  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_inventory PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_inventory_product_warehouse UNIQUE (product_id, warehouse),\n" +
        "  CONSTRAINT ck_inventory_qty CHECK (qty >= 0)\n" +
        ");\n" +
        "COMMENT ON TABLE  inventory IS 'Per-warehouse stock levels.';\n" +
        "COMMENT ON COLUMN inventory.qty IS 'Current on-hand quantity; never negative.';\n\n" +

        "CREATE TABLE orders (\n" +
        "  id               UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  user_id          UUID          NOT NULL REFERENCES users (id),\n" +
        "  status           VARCHAR(20)   NOT NULL DEFAULT 'pending',\n" +
        "  total            NUMERIC(14,2) NOT NULL DEFAULT 0,\n" +
        "  notes            TEXT          NULL,\n" +
        "  created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_orders PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_orders_status CHECK (status IN ('pending','confirmed','shipped','delivered','cancelled')),\n" +
        "  CONSTRAINT ck_orders_total  CHECK (total >= 0)\n" +
        ");\n" +
        "COMMENT ON TABLE  orders IS 'Customer purchase orders.';\n" +
        "COMMENT ON COLUMN orders.status IS 'Lifecycle state: pending → confirmed → shipped → delivered.';\n\n" +

        "CREATE INDEX ix_orders_user   ON orders (user_id);\n" +
        "CREATE INDEX ix_orders_status ON orders (status);\n\n" +

        "CREATE TABLE order_items (\n" +
        "  id         UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  order_id   UUID          NOT NULL REFERENCES orders (id) ON DELETE CASCADE,\n" +
        "  product_id UUID          NOT NULL REFERENCES products (id),\n" +
        "  quantity   INTEGER       NOT NULL,\n" +
        "  unit_price NUMERIC(12,2) NOT NULL,\n" +
        "  CONSTRAINT pk_order_items PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_order_items_qty   CHECK (quantity > 0),\n" +
        "  CONSTRAINT ck_order_items_price CHECK (unit_price >= 0)\n" +
        ");\n" +
        "COMMENT ON TABLE order_items IS 'Line items belonging to an order.';\n\n" +

        string.Join("\n", Enumerable.Range(1, 25).Select(i =>
            $"-- Schema note {i}: all v1 tables comply with the platform naming conventions (snake_case, UUID PKs, timestamptz audit columns)."));

    private static readonly string SchemaV2 =
        "-- ============================================================\n" +
        "-- Database Schema – Version 2.0\n" +
        "-- Released: 2023-07-01\n" +
        "-- Engine: PostgreSQL 15\n" +
        "-- Changes from v1:\n" +
        "--   ADDED   reviews table (new feature, non-breaking)\n" +
        "--   ADDED   products.tags column (optional, non-breaking)\n" +
        "--   ADDED   orders.shipping_address column (nullable, non-breaking)\n" +
        "--   ADDED   ix_products_name index (non-breaking)\n" +
        "-- All changes are additive; no existing columns or tables removed.\n" +
        "-- ============================================================\n\n" +

        "CREATE TABLE users (\n" +
        "  id            UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  email         VARCHAR(320)  NOT NULL,\n" +
        "  name          VARCHAR(200)  NOT NULL,\n" +
        "  phone         VARCHAR(30)   NULL,\n" +
        "  role          VARCHAR(20)   NOT NULL DEFAULT 'viewer',\n" +
        "  password_hash VARCHAR(128)  NOT NULL,\n" +
        "  is_active     BOOLEAN       NOT NULL DEFAULT TRUE,\n" +
        "  created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_users PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_users_email UNIQUE (email),\n" +
        "  CONSTRAINT ck_users_role CHECK (role IN ('admin','editor','viewer'))\n" +
        ");\n\n" +

        "CREATE INDEX ix_users_email ON users (email);\n" +
        "CREATE INDEX ix_users_role  ON users (role);\n\n" +

        "CREATE TABLE categories (\n" +
        "  id         UUID         NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name       VARCHAR(100) NOT NULL,\n" +
        "  parent_id  UUID         NULL REFERENCES categories (id) ON DELETE SET NULL,\n" +
        "  sort_order INTEGER      NOT NULL DEFAULT 0,\n" +
        "  created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_categories PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_categories_name UNIQUE (name)\n" +
        ");\n\n" +

        "CREATE TABLE products (\n" +
        "  id          UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name        VARCHAR(200)  NOT NULL,\n" +
        "  description TEXT          NULL,\n" +
        "  price       NUMERIC(12,2) NOT NULL,\n" +
        "  legacy_code VARCHAR(50)   NULL,\n" +
        "  tags        TEXT[]        NULL,\n" +
        "  category_id UUID          NOT NULL REFERENCES categories (id),\n" +
        "  created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_products PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_products_price CHECK (price >= 0)\n" +
        ");\n" +
        "COMMENT ON COLUMN products.tags IS 'Free-form search tags; array of lowercase strings.';\n\n" +

        "CREATE INDEX ix_products_category ON products (category_id);\n" +
        "CREATE INDEX ix_products_name     ON products (name);\n\n" +

        "CREATE TABLE inventory (\n" +
        "  id         UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  warehouse  VARCHAR(50) NOT NULL,\n" +
        "  qty        INTEGER     NOT NULL DEFAULT 0,\n" +
        "  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_inventory PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_inventory_product_warehouse UNIQUE (product_id, warehouse),\n" +
        "  CONSTRAINT ck_inventory_qty CHECK (qty >= 0)\n" +
        ");\n\n" +

        "CREATE TABLE orders (\n" +
        "  id               UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  user_id          UUID          NOT NULL REFERENCES users (id),\n" +
        "  status           VARCHAR(20)   NOT NULL DEFAULT 'pending',\n" +
        "  total            NUMERIC(14,2) NOT NULL DEFAULT 0,\n" +
        "  notes            TEXT          NULL,\n" +
        "  shipping_address TEXT          NULL,\n" +
        "  created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_orders PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_orders_status CHECK (status IN ('pending','confirmed','shipped','delivered','cancelled')),\n" +
        "  CONSTRAINT ck_orders_total  CHECK (total >= 0)\n" +
        ");\n" +
        "COMMENT ON COLUMN orders.shipping_address IS 'Denormalised shipping address snapshot at order time.';\n\n" +

        "CREATE INDEX ix_orders_user   ON orders (user_id);\n" +
        "CREATE INDEX ix_orders_status ON orders (status);\n\n" +

        "CREATE TABLE order_items (\n" +
        "  id         UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  order_id   UUID          NOT NULL REFERENCES orders (id) ON DELETE CASCADE,\n" +
        "  product_id UUID          NOT NULL REFERENCES products (id),\n" +
        "  quantity   INTEGER       NOT NULL,\n" +
        "  unit_price NUMERIC(12,2) NOT NULL,\n" +
        "  CONSTRAINT pk_order_items PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_order_items_qty   CHECK (quantity > 0),\n" +
        "  CONSTRAINT ck_order_items_price CHECK (unit_price >= 0)\n" +
        ");\n\n" +

        "CREATE TABLE reviews (\n" +
        "  id          UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id  UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  user_id     UUID        NOT NULL REFERENCES users (id),\n" +
        "  rating      SMALLINT    NOT NULL,\n" +
        "  body        TEXT        NULL,\n" +
        "  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_reviews PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_reviews_rating CHECK (rating BETWEEN 1 AND 5)\n" +
        ");\n" +
        "COMMENT ON TABLE reviews IS 'User-submitted product reviews and ratings.';\n\n" +

        "CREATE INDEX ix_reviews_product ON reviews (product_id);\n" +
        "CREATE INDEX ix_reviews_user    ON reviews (user_id);\n\n" +

        string.Join("\n", Enumerable.Range(1, 25).Select(i =>
            $"-- Schema note {i}: v2 is fully backward-compatible with v1; all additions are nullable or have defaults."));

    private static readonly string SchemaV3 =
        "-- ============================================================\n" +
        "-- Database Schema – Version 3.0\n" +
        "-- Released: 2024-01-15\n" +
        "-- Engine: PostgreSQL 15\n" +
        "-- Changes from v2:\n" +
        "--   BREAKING  users.phone renamed to users.phone_number\n" +
        "--   BREAKING  categories.sort_order column DROPPED\n" +
        "--   ADDED     audit_log table (new feature, non-breaking)\n" +
        "--   ADDED     users.locale column (nullable, non-breaking)\n" +
        "-- Migration notes: run RENAME COLUMN and DROP COLUMN in a maintenance window.\n" +
        "-- ============================================================\n\n" +

        "CREATE TABLE users (\n" +
        "  id            UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  email         VARCHAR(320)  NOT NULL,\n" +
        "  name          VARCHAR(200)  NOT NULL,\n" +
        "  phone_number  VARCHAR(30)   NULL,\n" +
        "  role          VARCHAR(20)   NOT NULL DEFAULT 'viewer',\n" +
        "  password_hash VARCHAR(128)  NOT NULL,\n" +
        "  is_active     BOOLEAN       NOT NULL DEFAULT TRUE,\n" +
        "  locale        VARCHAR(10)   NULL DEFAULT 'en',\n" +
        "  created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_users PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_users_email UNIQUE (email),\n" +
        "  CONSTRAINT ck_users_role CHECK (role IN ('admin','editor','viewer'))\n" +
        ");\n" +
        "COMMENT ON COLUMN users.phone_number IS 'Optional contact phone; renamed from phone in v3. E.164 format required.';\n" +
        "COMMENT ON COLUMN users.locale IS 'BCP 47 locale tag for UI rendering, e.g. en, fr, de.';\n\n" +

        "CREATE INDEX ix_users_email ON users (email);\n" +
        "CREATE INDEX ix_users_role  ON users (role);\n\n" +

        "CREATE TABLE categories (\n" +
        "  id         UUID         NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name       VARCHAR(100) NOT NULL,\n" +
        "  parent_id  UUID         NULL REFERENCES categories (id) ON DELETE SET NULL,\n" +
        "  created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_categories PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_categories_name UNIQUE (name)\n" +
        ");\n" +
        "-- NOTE: sort_order column was removed in v3. UI teams must manage display order client-side.\n\n" +

        "CREATE TABLE products (\n" +
        "  id          UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name        VARCHAR(200)  NOT NULL,\n" +
        "  description TEXT          NULL,\n" +
        "  price       NUMERIC(12,2) NOT NULL,\n" +
        "  legacy_code VARCHAR(50)   NULL,\n" +
        "  tags        TEXT[]        NULL,\n" +
        "  category_id UUID          NOT NULL REFERENCES categories (id),\n" +
        "  created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_products PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_products_price CHECK (price >= 0)\n" +
        ");\n\n" +

        "CREATE INDEX ix_products_category ON products (category_id);\n" +
        "CREATE INDEX ix_products_name     ON products (name);\n\n" +

        "CREATE TABLE inventory (\n" +
        "  id         UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  warehouse  VARCHAR(50) NOT NULL,\n" +
        "  qty        INTEGER     NOT NULL DEFAULT 0,\n" +
        "  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_inventory PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_inventory_product_warehouse UNIQUE (product_id, warehouse),\n" +
        "  CONSTRAINT ck_inventory_qty CHECK (qty >= 0)\n" +
        ");\n\n" +

        "CREATE TABLE orders (\n" +
        "  id               UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  user_id          UUID          NOT NULL REFERENCES users (id),\n" +
        "  status           VARCHAR(20)   NOT NULL DEFAULT 'pending',\n" +
        "  total            NUMERIC(14,2) NOT NULL DEFAULT 0,\n" +
        "  notes            TEXT          NULL,\n" +
        "  shipping_address TEXT          NULL,\n" +
        "  created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_orders PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_orders_status CHECK (status IN ('pending','confirmed','shipped','delivered','cancelled')),\n" +
        "  CONSTRAINT ck_orders_total  CHECK (total >= 0)\n" +
        ");\n\n" +

        "CREATE INDEX ix_orders_user   ON orders (user_id);\n" +
        "CREATE INDEX ix_orders_status ON orders (status);\n\n" +

        "CREATE TABLE order_items (\n" +
        "  id         UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  order_id   UUID          NOT NULL REFERENCES orders (id) ON DELETE CASCADE,\n" +
        "  product_id UUID          NOT NULL REFERENCES products (id),\n" +
        "  quantity   INTEGER       NOT NULL,\n" +
        "  unit_price NUMERIC(12,2) NOT NULL,\n" +
        "  CONSTRAINT pk_order_items PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_order_items_qty   CHECK (quantity > 0),\n" +
        "  CONSTRAINT ck_order_items_price CHECK (unit_price >= 0)\n" +
        ");\n\n" +

        "CREATE TABLE reviews (\n" +
        "  id          UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id  UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  user_id     UUID        NOT NULL REFERENCES users (id),\n" +
        "  rating      SMALLINT    NOT NULL,\n" +
        "  body        TEXT        NULL,\n" +
        "  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_reviews PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_reviews_rating CHECK (rating BETWEEN 1 AND 5)\n" +
        ");\n\n" +

        "CREATE INDEX ix_reviews_product ON reviews (product_id);\n\n" +

        "CREATE TABLE audit_log (\n" +
        "  id          BIGSERIAL    NOT NULL,\n" +
        "  table_name  VARCHAR(100) NOT NULL,\n" +
        "  record_id   UUID         NOT NULL,\n" +
        "  action      VARCHAR(10)  NOT NULL,\n" +
        "  changed_by  UUID         NULL REFERENCES users (id) ON DELETE SET NULL,\n" +
        "  changed_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  payload     JSONB        NULL,\n" +
        "  CONSTRAINT pk_audit_log PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_audit_log_action CHECK (action IN ('INSERT','UPDATE','DELETE'))\n" +
        ");\n" +
        "COMMENT ON TABLE audit_log IS 'Append-only row-level change log populated by triggers.';\n\n" +

        "CREATE INDEX ix_audit_log_table_record ON audit_log (table_name, record_id);\n" +
        "CREATE INDEX ix_audit_log_changed_at   ON audit_log (changed_at);\n\n" +

        string.Join("\n", Enumerable.Range(1, 25).Select(i =>
            $"-- Schema note {i}: v3 introduces two breaking changes; coordinate rename and drop with all consumers before deploying."));

    private static readonly string SchemaV4 =
        "-- ============================================================\n" +
        "-- Database Schema – Version 4.0\n" +
        "-- Released: 2024-06-01\n" +
        "-- Engine: PostgreSQL 15\n" +
        "-- Changes from v3:\n" +
        "--   BREAKING  orders.status type changed from VARCHAR(20) to order_status enum\n" +
        "--   BREAKING  inventory.qty renamed to inventory.quantity\n" +
        "--   ADDED     products.weight column (nullable, non-breaking)\n" +
        "--   ADDED     order_status enum type\n" +
        "--   ADDED     ix_inventory_warehouse index\n" +
        "-- Migration notes: create enum, migrate data, alter column, rename column.\n" +
        "-- ============================================================\n\n" +

        "CREATE TYPE order_status AS ENUM ('pending','confirmed','shipped','delivered','cancelled');\n" +
        "COMMENT ON TYPE order_status IS 'Lifecycle states for customer orders; replaces VARCHAR status column.';\n\n" +

        "CREATE TABLE users (\n" +
        "  id            UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  email         VARCHAR(320)  NOT NULL,\n" +
        "  name          VARCHAR(200)  NOT NULL,\n" +
        "  phone_number  VARCHAR(30)   NULL,\n" +
        "  role          VARCHAR(20)   NOT NULL DEFAULT 'viewer',\n" +
        "  password_hash VARCHAR(128)  NOT NULL,\n" +
        "  is_active     BOOLEAN       NOT NULL DEFAULT TRUE,\n" +
        "  locale        VARCHAR(10)   NULL DEFAULT 'en',\n" +
        "  created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_users PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_users_email UNIQUE (email),\n" +
        "  CONSTRAINT ck_users_role CHECK (role IN ('admin','editor','viewer'))\n" +
        ");\n\n" +

        "CREATE INDEX ix_users_email ON users (email);\n" +
        "CREATE INDEX ix_users_role  ON users (role);\n\n" +

        "CREATE TABLE categories (\n" +
        "  id         UUID         NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name       VARCHAR(100) NOT NULL,\n" +
        "  parent_id  UUID         NULL REFERENCES categories (id) ON DELETE SET NULL,\n" +
        "  created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_categories PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_categories_name UNIQUE (name)\n" +
        ");\n\n" +

        "CREATE TABLE products (\n" +
        "  id          UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name        VARCHAR(200)  NOT NULL,\n" +
        "  description TEXT          NULL,\n" +
        "  price       NUMERIC(12,2) NOT NULL,\n" +
        "  legacy_code VARCHAR(50)   NULL,\n" +
        "  tags        TEXT[]        NULL,\n" +
        "  weight      NUMERIC(8,3)  NULL,\n" +
        "  category_id UUID          NOT NULL REFERENCES categories (id),\n" +
        "  created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_products PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_products_price  CHECK (price >= 0),\n" +
        "  CONSTRAINT ck_products_weight CHECK (weight IS NULL OR weight > 0)\n" +
        ");\n" +
        "COMMENT ON COLUMN products.weight IS 'Shipping weight in kilograms; null means weight not yet catalogued.';\n\n" +

        "CREATE INDEX ix_products_category ON products (category_id);\n" +
        "CREATE INDEX ix_products_name     ON products (name);\n\n" +

        "CREATE TABLE inventory (\n" +
        "  id         UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  warehouse  VARCHAR(50) NOT NULL,\n" +
        "  quantity   INTEGER     NOT NULL DEFAULT 0,\n" +
        "  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_inventory PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_inventory_product_warehouse UNIQUE (product_id, warehouse),\n" +
        "  CONSTRAINT ck_inventory_quantity CHECK (quantity >= 0)\n" +
        ");\n" +
        "COMMENT ON COLUMN inventory.quantity IS 'Current on-hand quantity; renamed from qty in v4.';\n\n" +

        "CREATE INDEX ix_inventory_warehouse ON inventory (warehouse);\n\n" +

        "CREATE TABLE orders (\n" +
        "  id               UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  user_id          UUID          NOT NULL REFERENCES users (id),\n" +
        "  status           order_status  NOT NULL DEFAULT 'pending',\n" +
        "  total            NUMERIC(14,2) NOT NULL DEFAULT 0,\n" +
        "  notes            TEXT          NULL,\n" +
        "  shipping_address TEXT          NULL,\n" +
        "  created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_orders PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_orders_total CHECK (total >= 0)\n" +
        ");\n" +
        "COMMENT ON COLUMN orders.status IS 'Uses order_status enum type since v4; was VARCHAR(20) in v3.';\n\n" +

        "CREATE INDEX ix_orders_user   ON orders (user_id);\n" +
        "CREATE INDEX ix_orders_status ON orders (status);\n\n" +

        "CREATE TABLE order_items (\n" +
        "  id         UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  order_id   UUID          NOT NULL REFERENCES orders (id) ON DELETE CASCADE,\n" +
        "  product_id UUID          NOT NULL REFERENCES products (id),\n" +
        "  quantity   INTEGER       NOT NULL,\n" +
        "  unit_price NUMERIC(12,2) NOT NULL,\n" +
        "  CONSTRAINT pk_order_items PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_order_items_qty   CHECK (quantity > 0),\n" +
        "  CONSTRAINT ck_order_items_price CHECK (unit_price >= 0)\n" +
        ");\n\n" +

        "CREATE TABLE reviews (\n" +
        "  id          UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id  UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  user_id     UUID        NOT NULL REFERENCES users (id),\n" +
        "  rating      SMALLINT    NOT NULL,\n" +
        "  body        TEXT        NULL,\n" +
        "  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_reviews PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_reviews_rating CHECK (rating BETWEEN 1 AND 5)\n" +
        ");\n\n" +

        "CREATE TABLE audit_log (\n" +
        "  id          BIGSERIAL    NOT NULL,\n" +
        "  table_name  VARCHAR(100) NOT NULL,\n" +
        "  record_id   UUID         NOT NULL,\n" +
        "  action      VARCHAR(10)  NOT NULL,\n" +
        "  changed_by  UUID         NULL REFERENCES users (id) ON DELETE SET NULL,\n" +
        "  changed_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  payload     JSONB        NULL,\n" +
        "  CONSTRAINT pk_audit_log PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_audit_log_action CHECK (action IN ('INSERT','UPDATE','DELETE'))\n" +
        ");\n\n" +

        "CREATE INDEX ix_audit_log_table_record ON audit_log (table_name, record_id);\n" +
        "CREATE INDEX ix_audit_log_changed_at   ON audit_log (changed_at);\n\n" +

        string.Join("\n", Enumerable.Range(1, 25).Select(i =>
            $"-- Schema note {i}: v4 enum migration must use a transaction; ALTER COLUMN TYPE requires USING cast expression."));

    private static readonly string SchemaV5 =
        "-- ============================================================\n" +
        "-- Database Schema – Version 5.0\n" +
        "-- Released: 2024-11-01\n" +
        "-- Engine: PostgreSQL 15\n" +
        "-- Changes from v4:\n" +
        "--   BREAKING  products.legacy_code column DROPPED\n" +
        "--   BREAKING  orders.shipping_address split into shipping_street, shipping_city, shipping_country\n" +
        "--   ADDED     users.mfa_enabled column (NOT NULL with default, non-breaking)\n" +
        "--   ADDED     ix_users_mfa index\n" +
        "--   ADDED     ix_orders_shipping_country index\n" +
        "-- Migration notes: data migration required to populate new shipping columns before drop.\n" +
        "-- ============================================================\n\n" +

        "CREATE TYPE order_status AS ENUM ('pending','confirmed','shipped','delivered','cancelled');\n\n" +

        "CREATE TABLE users (\n" +
        "  id            UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  email         VARCHAR(320)  NOT NULL,\n" +
        "  name          VARCHAR(200)  NOT NULL,\n" +
        "  phone_number  VARCHAR(30)   NULL,\n" +
        "  role          VARCHAR(20)   NOT NULL DEFAULT 'viewer',\n" +
        "  password_hash VARCHAR(128)  NOT NULL,\n" +
        "  is_active     BOOLEAN       NOT NULL DEFAULT TRUE,\n" +
        "  locale        VARCHAR(10)   NULL DEFAULT 'en',\n" +
        "  mfa_enabled   BOOLEAN       NOT NULL DEFAULT FALSE,\n" +
        "  created_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at    TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_users PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_users_email UNIQUE (email),\n" +
        "  CONSTRAINT ck_users_role CHECK (role IN ('admin','editor','viewer'))\n" +
        ");\n" +
        "COMMENT ON COLUMN users.mfa_enabled IS 'Whether the user has enrolled in multi-factor authentication.';\n\n" +

        "CREATE INDEX ix_users_email ON users (email);\n" +
        "CREATE INDEX ix_users_role  ON users (role);\n" +
        "CREATE INDEX ix_users_mfa   ON users (mfa_enabled) WHERE mfa_enabled = TRUE;\n\n" +

        "CREATE TABLE categories (\n" +
        "  id         UUID         NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name       VARCHAR(100) NOT NULL,\n" +
        "  parent_id  UUID         NULL REFERENCES categories (id) ON DELETE SET NULL,\n" +
        "  created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_categories PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_categories_name UNIQUE (name)\n" +
        ");\n\n" +

        "CREATE TABLE products (\n" +
        "  id          UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  name        VARCHAR(200)  NOT NULL,\n" +
        "  description TEXT          NULL,\n" +
        "  price       NUMERIC(12,2) NOT NULL,\n" +
        "  tags        TEXT[]        NULL,\n" +
        "  weight      NUMERIC(8,3)  NULL,\n" +
        "  category_id UUID          NOT NULL REFERENCES categories (id),\n" +
        "  created_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_products PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_products_price  CHECK (price >= 0),\n" +
        "  CONSTRAINT ck_products_weight CHECK (weight IS NULL OR weight > 0)\n" +
        ");\n" +
        "-- NOTE: legacy_code column was dropped in v5. Remove from all client SELECT lists and INSERT statements.\n\n" +

        "CREATE INDEX ix_products_category ON products (category_id);\n" +
        "CREATE INDEX ix_products_name     ON products (name);\n\n" +

        "CREATE TABLE inventory (\n" +
        "  id         UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  warehouse  VARCHAR(50) NOT NULL,\n" +
        "  quantity   INTEGER     NOT NULL DEFAULT 0,\n" +
        "  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_inventory PRIMARY KEY (id),\n" +
        "  CONSTRAINT uq_inventory_product_warehouse UNIQUE (product_id, warehouse),\n" +
        "  CONSTRAINT ck_inventory_quantity CHECK (quantity >= 0)\n" +
        ");\n\n" +

        "CREATE INDEX ix_inventory_warehouse ON inventory (warehouse);\n\n" +

        "CREATE TABLE orders (\n" +
        "  id                UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  user_id           UUID          NOT NULL REFERENCES users (id),\n" +
        "  status            order_status  NOT NULL DEFAULT 'pending',\n" +
        "  total             NUMERIC(14,2) NOT NULL DEFAULT 0,\n" +
        "  notes             TEXT          NULL,\n" +
        "  shipping_street   VARCHAR(200)  NULL,\n" +
        "  shipping_city     VARCHAR(100)  NULL,\n" +
        "  shipping_country  VARCHAR(100)  NULL,\n" +
        "  created_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  updated_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_orders PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_orders_total CHECK (total >= 0)\n" +
        ");\n" +
        "COMMENT ON COLUMN orders.shipping_street  IS 'Street line of shipping address; replaces shipping_address (v5).';\n" +
        "COMMENT ON COLUMN orders.shipping_city    IS 'City of shipping address; replaces shipping_address (v5).';\n" +
        "COMMENT ON COLUMN orders.shipping_country IS 'ISO country code of shipping address; replaces shipping_address (v5).';\n\n" +

        "CREATE INDEX ix_orders_user             ON orders (user_id);\n" +
        "CREATE INDEX ix_orders_status           ON orders (status);\n" +
        "CREATE INDEX ix_orders_shipping_country ON orders (shipping_country);\n\n" +

        "CREATE TABLE order_items (\n" +
        "  id         UUID          NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  order_id   UUID          NOT NULL REFERENCES orders (id) ON DELETE CASCADE,\n" +
        "  product_id UUID          NOT NULL REFERENCES products (id),\n" +
        "  quantity   INTEGER       NOT NULL,\n" +
        "  unit_price NUMERIC(12,2) NOT NULL,\n" +
        "  CONSTRAINT pk_order_items PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_order_items_qty   CHECK (quantity > 0),\n" +
        "  CONSTRAINT ck_order_items_price CHECK (unit_price >= 0)\n" +
        ");\n\n" +

        "CREATE TABLE reviews (\n" +
        "  id          UUID        NOT NULL DEFAULT gen_random_uuid(),\n" +
        "  product_id  UUID        NOT NULL REFERENCES products (id) ON DELETE CASCADE,\n" +
        "  user_id     UUID        NOT NULL REFERENCES users (id),\n" +
        "  rating      SMALLINT    NOT NULL,\n" +
        "  body        TEXT        NULL,\n" +
        "  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),\n" +
        "  CONSTRAINT pk_reviews PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_reviews_rating CHECK (rating BETWEEN 1 AND 5)\n" +
        ");\n\n" +

        "CREATE TABLE audit_log (\n" +
        "  id          BIGSERIAL    NOT NULL,\n" +
        "  table_name  VARCHAR(100) NOT NULL,\n" +
        "  record_id   UUID         NOT NULL,\n" +
        "  action      VARCHAR(10)  NOT NULL,\n" +
        "  changed_by  UUID         NULL REFERENCES users (id) ON DELETE SET NULL,\n" +
        "  changed_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),\n" +
        "  payload     JSONB        NULL,\n" +
        "  CONSTRAINT pk_audit_log PRIMARY KEY (id),\n" +
        "  CONSTRAINT ck_audit_log_action CHECK (action IN ('INSERT','UPDATE','DELETE'))\n" +
        ");\n\n" +

        "CREATE INDEX ix_audit_log_table_record ON audit_log (table_name, record_id);\n" +
        "CREATE INDEX ix_audit_log_changed_at   ON audit_log (changed_at);\n\n" +

        string.Join("\n", Enumerable.Range(1, 25).Select(i =>
            $"-- Schema note {i}: v5 shipping split requires a data migration script; deploy in two phases to avoid downtime."));

    // -------------------------------------------------------------------------
    // ORM entity snapshot files
    // -------------------------------------------------------------------------

    private static readonly string OrmV1 =
        "# ORM Entity Snapshot – Version 1.0\n" +
        "# Framework: Entity Framework Core 8\n" +
        "# Generated: 2023-01-25\n" +
        "# Status: Fully in sync with schema-v1.sql\n\n" +

        "ENTITY: User\n" +
        "  Table:   users\n" +
        "  Columns:\n" +
        "    Id           -> id            (Guid,       required)\n" +
        "    Email        -> email         (string,     required, max 320)\n" +
        "    Name         -> name          (string,     required, max 200)\n" +
        "    Phone        -> phone         (string?,    max 30)\n" +
        "    Role         -> role          (string,     required, default: viewer)\n" +
        "    PasswordHash -> password_hash (string,     required, max 128)\n" +
        "    IsActive     -> is_active     (bool,       required, default: true)\n" +
        "    CreatedAt    -> created_at    (DateTimeOffset, required)\n" +
        "    UpdatedAt    -> updated_at    (DateTimeOffset, required)\n\n" +

        "ENTITY: Category\n" +
        "  Table:   categories\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    Name      -> name       (string,  required, max 100)\n" +
        "    ParentId  -> parent_id  (Guid?,   FK: Category.Id)\n" +
        "    SortOrder -> sort_order (int,     required, default: 0)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Product\n" +
        "  Table:   products\n" +
        "  Columns:\n" +
        "    Id          -> id           (Guid,    required)\n" +
        "    Name        -> name         (string,  required, max 200)\n" +
        "    Description -> description  (string?, text)\n" +
        "    Price       -> price        (decimal, required, precision 12,2)\n" +
        "    LegacyCode  -> legacy_code  (string?, max 50)\n" +
        "    CategoryId  -> category_id  (Guid,    required, FK: Category.Id)\n" +
        "    CreatedAt   -> created_at   (DateTimeOffset, required)\n" +
        "    UpdatedAt   -> updated_at   (DateTimeOffset, required)\n\n" +

        "ENTITY: Inventory\n" +
        "  Table:   inventory\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Warehouse -> warehouse  (string,  required, max 50)\n" +
        "    Qty       -> qty        (int,     required, default: 0)\n" +
        "    UpdatedAt -> updated_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Order\n" +
        "  Table:   orders\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    UserId    -> user_id    (Guid,    required, FK: User.Id)\n" +
        "    Status    -> status     (string,  required, default: pending)\n" +
        "    Total     -> total      (decimal, required, precision 14,2)\n" +
        "    Notes     -> notes      (string?, text)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n" +
        "    UpdatedAt -> updated_at (DateTimeOffset, required)\n\n" +

        "ENTITY: OrderItem\n" +
        "  Table:   order_items\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    OrderId   -> order_id   (Guid,    required, FK: Order.Id)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Quantity  -> quantity   (int,     required, min: 1)\n" +
        "    UnitPrice -> unit_price (decimal, required, precision 12,2)\n\n" +

        string.Join("\n", Enumerable.Range(1, 15).Select(i =>
            $"# ORM note {i}: v1 entities use value object pattern for money fields; Price and UnitPrice map to Money record."));

    private static readonly string OrmV2 =
        "# ORM Entity Snapshot – Version 2.0\n" +
        "# Framework: Entity Framework Core 8\n" +
        "# Generated: 2023-07-10\n" +
        "# Status: Fully in sync with schema-v2.sql\n\n" +

        "ENTITY: User\n" +
        "  Table:   users\n" +
        "  Columns:\n" +
        "    Id           -> id            (Guid,       required)\n" +
        "    Email        -> email         (string,     required, max 320)\n" +
        "    Name         -> name          (string,     required, max 200)\n" +
        "    Phone        -> phone         (string?,    max 30)\n" +
        "    Role         -> role          (string,     required, default: viewer)\n" +
        "    PasswordHash -> password_hash (string,     required, max 128)\n" +
        "    IsActive     -> is_active     (bool,       required, default: true)\n" +
        "    CreatedAt    -> created_at    (DateTimeOffset, required)\n" +
        "    UpdatedAt    -> updated_at    (DateTimeOffset, required)\n\n" +

        "ENTITY: Category\n" +
        "  Table:   categories\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    Name      -> name       (string,  required, max 100)\n" +
        "    ParentId  -> parent_id  (Guid?,   FK: Category.Id)\n" +
        "    SortOrder -> sort_order (int,     required, default: 0)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Product\n" +
        "  Table:   products\n" +
        "  Columns:\n" +
        "    Id          -> id           (Guid,    required)\n" +
        "    Name        -> name         (string,  required, max 200)\n" +
        "    Description -> description  (string?, text)\n" +
        "    Price       -> price        (decimal, required, precision 12,2)\n" +
        "    LegacyCode  -> legacy_code  (string?, max 50)\n" +
        "    Tags        -> tags         (string[]?, array)\n" +
        "    CategoryId  -> category_id  (Guid,    required, FK: Category.Id)\n" +
        "    CreatedAt   -> created_at   (DateTimeOffset, required)\n" +
        "    UpdatedAt   -> updated_at   (DateTimeOffset, required)\n\n" +

        "ENTITY: Inventory\n" +
        "  Table:   inventory\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Warehouse -> warehouse  (string,  required, max 50)\n" +
        "    Qty       -> qty        (int,     required, default: 0)\n" +
        "    UpdatedAt -> updated_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Order\n" +
        "  Table:   orders\n" +
        "  Columns:\n" +
        "    Id              -> id               (Guid,    required)\n" +
        "    UserId          -> user_id          (Guid,    required, FK: User.Id)\n" +
        "    Status          -> status           (string,  required, default: pending)\n" +
        "    Total           -> total            (decimal, required, precision 14,2)\n" +
        "    Notes           -> notes            (string?, text)\n" +
        "    ShippingAddress -> shipping_address (string?, text)\n" +
        "    CreatedAt       -> created_at       (DateTimeOffset, required)\n" +
        "    UpdatedAt       -> updated_at       (DateTimeOffset, required)\n\n" +

        "ENTITY: OrderItem\n" +
        "  Table:   order_items\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    OrderId   -> order_id   (Guid,    required, FK: Order.Id)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Quantity  -> quantity   (int,     required, min: 1)\n" +
        "    UnitPrice -> unit_price (decimal, required, precision 12,2)\n\n" +

        "ENTITY: Review\n" +
        "  Table:   reviews\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    UserId    -> user_id    (Guid,    required, FK: User.Id)\n" +
        "    Rating    -> rating     (short,   required, range 1–5)\n" +
        "    Body      -> body       (string?, text)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        string.Join("\n", Enumerable.Range(1, 15).Select(i =>
            $"# ORM note {i}: v2 adds Review entity and Tags array; both map cleanly to new schema columns."));

    private static readonly string OrmV3 =
        "# ORM Entity Snapshot – Version 3.0\n" +
        "# Framework: Entity Framework Core 8\n" +
        "# Generated: 2024-01-20\n" +
        "# Status: DRIFT DETECTED – see notes below\n" +
        "# Known issue: User.Phone still maps to 'phone'; schema-v3.sql renamed column to 'phone_number'.\n" +
        "#   This drift will cause runtime mapping failures on User.Phone reads/writes.\n\n" +

        "ENTITY: User\n" +
        "  Table:   users\n" +
        "  Columns:\n" +
        "    Id           -> id            (Guid,       required)\n" +
        "    Email        -> email         (string,     required, max 320)\n" +
        "    Name         -> name          (string,     required, max 200)\n" +
        "    Phone        -> phone         (string?,    max 30)   -- DRIFT: schema column is now phone_number\n" +
        "    Role         -> role          (string,     required, default: viewer)\n" +
        "    PasswordHash -> password_hash (string,     required, max 128)\n" +
        "    IsActive     -> is_active     (bool,       required, default: true)\n" +
        "    Locale       -> locale        (string?,    max 10, default: en)\n" +
        "    CreatedAt    -> created_at    (DateTimeOffset, required)\n" +
        "    UpdatedAt    -> updated_at    (DateTimeOffset, required)\n\n" +

        "ENTITY: Category\n" +
        "  Table:   categories\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    Name      -> name       (string,  required, max 100)\n" +
        "    ParentId  -> parent_id  (Guid?,   FK: Category.Id)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n" +
        "    -- SortOrder removed from entity; matches schema-v3 drop\n\n" +

        "ENTITY: Product\n" +
        "  Table:   products\n" +
        "  Columns:\n" +
        "    Id          -> id           (Guid,    required)\n" +
        "    Name        -> name         (string,  required, max 200)\n" +
        "    Description -> description  (string?, text)\n" +
        "    Price       -> price        (decimal, required, precision 12,2)\n" +
        "    LegacyCode  -> legacy_code  (string?, max 50)\n" +
        "    Tags        -> tags         (string[]?, array)\n" +
        "    CategoryId  -> category_id  (Guid,    required, FK: Category.Id)\n" +
        "    CreatedAt   -> created_at   (DateTimeOffset, required)\n" +
        "    UpdatedAt   -> updated_at   (DateTimeOffset, required)\n\n" +

        "ENTITY: Inventory\n" +
        "  Table:   inventory\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Warehouse -> warehouse  (string,  required, max 50)\n" +
        "    Qty       -> qty        (int,     required, default: 0)\n" +
        "    UpdatedAt -> updated_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Order\n" +
        "  Table:   orders\n" +
        "  Columns:\n" +
        "    Id              -> id               (Guid,    required)\n" +
        "    UserId          -> user_id          (Guid,    required, FK: User.Id)\n" +
        "    Status          -> status           (string,  required, default: pending)\n" +
        "    Total           -> total            (decimal, required, precision 14,2)\n" +
        "    Notes           -> notes            (string?, text)\n" +
        "    ShippingAddress -> shipping_address (string?, text)\n" +
        "    CreatedAt       -> created_at       (DateTimeOffset, required)\n" +
        "    UpdatedAt       -> updated_at       (DateTimeOffset, required)\n\n" +

        "ENTITY: OrderItem\n" +
        "  Table:   order_items\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    OrderId   -> order_id   (Guid,    required, FK: Order.Id)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Quantity  -> quantity   (int,     required, min: 1)\n" +
        "    UnitPrice -> unit_price (decimal, required, precision 12,2)\n\n" +

        "ENTITY: Review\n" +
        "  Table:   reviews\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    UserId    -> user_id    (Guid,    required, FK: User.Id)\n" +
        "    Rating    -> rating     (short,   required, range 1–5)\n" +
        "    Body      -> body       (string?, text)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        "ENTITY: AuditLog\n" +
        "  Table:   audit_log\n" +
        "  Columns:\n" +
        "    Id        -> id          (long,    required)\n" +
        "    TableName -> table_name  (string,  required, max 100)\n" +
        "    RecordId  -> record_id   (Guid,    required)\n" +
        "    Action    -> action      (string,  required)\n" +
        "    ChangedBy -> changed_by  (Guid?,   FK: User.Id)\n" +
        "    ChangedAt -> changed_at  (DateTimeOffset, required)\n" +
        "    Payload   -> payload     (JsonDocument?, jsonb)\n\n" +

        string.Join("\n", Enumerable.Range(1, 15).Select(i =>
            $"# ORM note {i}: v3 ORM was deployed before schema migration ran; Phone drift must be fixed before v3 goes live."));

    private static readonly string OrmV4 =
        "# ORM Entity Snapshot – Version 4.0\n" +
        "# Framework: Entity Framework Core 8\n" +
        "# Generated: 2024-06-08\n" +
        "# Status: DRIFT DETECTED – see notes below\n" +
        "# Known issue: Inventory.Qty still maps to 'qty'; schema-v4.sql renamed column to 'quantity'.\n" +
        "#   This drift causes EF Core query translation failures on Inventory reads.\n\n" +

        "ENTITY: User\n" +
        "  Table:   users\n" +
        "  Columns:\n" +
        "    Id           -> id            (Guid,       required)\n" +
        "    Email        -> email         (string,     required, max 320)\n" +
        "    Name         -> name          (string,     required, max 200)\n" +
        "    PhoneNumber  -> phone_number  (string?,    max 30)\n" +
        "    Role         -> role          (string,     required, default: viewer)\n" +
        "    PasswordHash -> password_hash (string,     required, max 128)\n" +
        "    IsActive     -> is_active     (bool,       required, default: true)\n" +
        "    Locale       -> locale        (string?,    max 10, default: en)\n" +
        "    CreatedAt    -> created_at    (DateTimeOffset, required)\n" +
        "    UpdatedAt    -> updated_at    (DateTimeOffset, required)\n\n" +

        "ENTITY: Category\n" +
        "  Table:   categories\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    Name      -> name       (string,  required, max 100)\n" +
        "    ParentId  -> parent_id  (Guid?,   FK: Category.Id)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Product\n" +
        "  Table:   products\n" +
        "  Columns:\n" +
        "    Id          -> id           (Guid,    required)\n" +
        "    Name        -> name         (string,  required, max 200)\n" +
        "    Description -> description  (string?, text)\n" +
        "    Price       -> price        (decimal, required, precision 12,2)\n" +
        "    LegacyCode  -> legacy_code  (string?, max 50)\n" +
        "    Tags        -> tags         (string[]?, array)\n" +
        "    Weight      -> weight       (decimal?, precision 8,3)\n" +
        "    CategoryId  -> category_id  (Guid,    required, FK: Category.Id)\n" +
        "    CreatedAt   -> created_at   (DateTimeOffset, required)\n" +
        "    UpdatedAt   -> updated_at   (DateTimeOffset, required)\n\n" +

        "ENTITY: Inventory\n" +
        "  Table:   inventory\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Warehouse -> warehouse  (string,  required, max 50)\n" +
        "    Qty       -> qty        (int,     required, default: 0)  -- DRIFT: schema column is now quantity\n" +
        "    UpdatedAt -> updated_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Order\n" +
        "  Table:   orders\n" +
        "  Columns:\n" +
        "    Id              -> id               (Guid,    required)\n" +
        "    UserId          -> user_id          (Guid,    required, FK: User.Id)\n" +
        "    Status          -> status           (OrderStatus, required, default: pending)\n" +
        "    Total           -> total            (decimal, required, precision 14,2)\n" +
        "    Notes           -> notes            (string?, text)\n" +
        "    ShippingAddress -> shipping_address (string?, text)\n" +
        "    CreatedAt       -> created_at       (DateTimeOffset, required)\n" +
        "    UpdatedAt       -> updated_at       (DateTimeOffset, required)\n\n" +

        "ENTITY: OrderItem\n" +
        "  Table:   order_items\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    OrderId   -> order_id   (Guid,    required, FK: Order.Id)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Quantity  -> quantity   (int,     required, min: 1)\n" +
        "    UnitPrice -> unit_price (decimal, required, precision 12,2)\n\n" +

        "ENTITY: Review\n" +
        "  Table:   reviews\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    UserId    -> user_id    (Guid,    required, FK: User.Id)\n" +
        "    Rating    -> rating     (short,   required, range 1–5)\n" +
        "    Body      -> body       (string?, text)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        "ENTITY: AuditLog\n" +
        "  Table:   audit_log\n" +
        "  Columns:\n" +
        "    Id        -> id          (long,    required)\n" +
        "    TableName -> table_name  (string,  required, max 100)\n" +
        "    RecordId  -> record_id   (Guid,    required)\n" +
        "    Action    -> action      (string,  required)\n" +
        "    ChangedBy -> changed_by  (Guid?,   FK: User.Id)\n" +
        "    ChangedAt -> changed_at  (DateTimeOffset, required)\n" +
        "    Payload   -> payload     (JsonDocument?, jsonb)\n\n" +

        "ENUM: OrderStatus\n" +
        "  Values: Pending, Confirmed, Shipped, Delivered, Cancelled\n" +
        "  Maps to: order_status (PostgreSQL enum type)\n\n" +

        string.Join("\n", Enumerable.Range(1, 15).Select(i =>
            $"# ORM note {i}: v4 Inventory drift introduced by parallel migration; EF Core scaffold was not rerun after ALTER COLUMN."));

    private static readonly string OrmV5 =
        "# ORM Entity Snapshot – Version 5.0\n" +
        "# Framework: Entity Framework Core 8\n" +
        "# Generated: 2024-11-10\n" +
        "# Status: Fully in sync with schema-v5.sql\n\n" +

        "ENTITY: User\n" +
        "  Table:   users\n" +
        "  Columns:\n" +
        "    Id           -> id            (Guid,       required)\n" +
        "    Email        -> email         (string,     required, max 320)\n" +
        "    Name         -> name          (string,     required, max 200)\n" +
        "    PhoneNumber  -> phone_number  (string?,    max 30)\n" +
        "    Role         -> role          (string,     required, default: viewer)\n" +
        "    PasswordHash -> password_hash (string,     required, max 128)\n" +
        "    IsActive     -> is_active     (bool,       required, default: true)\n" +
        "    Locale       -> locale        (string?,    max 10, default: en)\n" +
        "    MfaEnabled   -> mfa_enabled   (bool,       required, default: false)\n" +
        "    CreatedAt    -> created_at    (DateTimeOffset, required)\n" +
        "    UpdatedAt    -> updated_at    (DateTimeOffset, required)\n\n" +

        "ENTITY: Category\n" +
        "  Table:   categories\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    Name      -> name       (string,  required, max 100)\n" +
        "    ParentId  -> parent_id  (Guid?,   FK: Category.Id)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Product\n" +
        "  Table:   products\n" +
        "  Columns:\n" +
        "    Id          -> id           (Guid,    required)\n" +
        "    Name        -> name         (string,  required, max 200)\n" +
        "    Description -> description  (string?, text)\n" +
        "    Price       -> price        (decimal, required, precision 12,2)\n" +
        "    Tags        -> tags         (string[]?, array)\n" +
        "    Weight      -> weight       (decimal?, precision 8,3)\n" +
        "    CategoryId  -> category_id  (Guid,    required, FK: Category.Id)\n" +
        "    CreatedAt   -> created_at   (DateTimeOffset, required)\n" +
        "    UpdatedAt   -> updated_at   (DateTimeOffset, required)\n" +
        "    -- LegacyCode removed; matches schema-v5 drop\n\n" +

        "ENTITY: Inventory\n" +
        "  Table:   inventory\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Warehouse -> warehouse  (string,  required, max 50)\n" +
        "    Quantity  -> quantity   (int,     required, default: 0)\n" +
        "    UpdatedAt -> updated_at (DateTimeOffset, required)\n\n" +

        "ENTITY: Order\n" +
        "  Table:   orders\n" +
        "  Columns:\n" +
        "    Id              -> id                (Guid,    required)\n" +
        "    UserId          -> user_id           (Guid,    required, FK: User.Id)\n" +
        "    Status          -> status            (OrderStatus, required, default: pending)\n" +
        "    Total           -> total             (decimal, required, precision 14,2)\n" +
        "    Notes           -> notes             (string?, text)\n" +
        "    ShippingStreet  -> shipping_street   (string?, max 200)\n" +
        "    ShippingCity    -> shipping_city     (string?, max 100)\n" +
        "    ShippingCountry -> shipping_country  (string?, max 100)\n" +
        "    CreatedAt       -> created_at        (DateTimeOffset, required)\n" +
        "    UpdatedAt       -> updated_at        (DateTimeOffset, required)\n\n" +

        "ENTITY: OrderItem\n" +
        "  Table:   order_items\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    OrderId   -> order_id   (Guid,    required, FK: Order.Id)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    Quantity  -> quantity   (int,     required, min: 1)\n" +
        "    UnitPrice -> unit_price (decimal, required, precision 12,2)\n\n" +

        "ENTITY: Review\n" +
        "  Table:   reviews\n" +
        "  Columns:\n" +
        "    Id        -> id         (Guid,    required)\n" +
        "    ProductId -> product_id (Guid,    required, FK: Product.Id)\n" +
        "    UserId    -> user_id    (Guid,    required, FK: User.Id)\n" +
        "    Rating    -> rating     (short,   required, range 1–5)\n" +
        "    Body      -> body       (string?, text)\n" +
        "    CreatedAt -> created_at (DateTimeOffset, required)\n\n" +

        "ENTITY: AuditLog\n" +
        "  Table:   audit_log\n" +
        "  Columns:\n" +
        "    Id        -> id          (long,    required)\n" +
        "    TableName -> table_name  (string,  required, max 100)\n" +
        "    RecordId  -> record_id   (Guid,    required)\n" +
        "    Action    -> action      (string,  required)\n" +
        "    ChangedBy -> changed_by  (Guid?,   FK: User.Id)\n" +
        "    ChangedAt -> changed_at  (DateTimeOffset, required)\n" +
        "    Payload   -> payload     (JsonDocument?, jsonb)\n\n" +

        "ENUM: OrderStatus\n" +
        "  Values: Pending, Confirmed, Shipped, Delivered, Cancelled\n" +
        "  Maps to: order_status (PostgreSQL enum type)\n\n" +

        string.Join("\n", Enumerable.Range(1, 15).Select(i =>
            $"# ORM note {i}: v5 entities were regenerated via EF Core scaffold after both schema migrations completed."));

    // -------------------------------------------------------------------------
    // Policy and registry files
    // -------------------------------------------------------------------------

    private static readonly string MigrationRules =
        "# Database Migration Classification Rules\n" +
        "# Version: 1.0\n" +
        "# Applies to: all TokenGuard-managed schema hops\n" +
        "# Reviewed by: Platform DBA team, 2023-01-01\n\n" +

        "RULE 1 – Column Removal\n" +
        "  Classification: BREAKING\n" +
        "  Condition: A column that existed in version N is absent in version N+1.\n" +
        "  Rationale: Any consumer SELECT-ing, INSERT-ing, or UPDATE-ing that column will receive a DB error.\n" +
        "  Required action: Coordinate with all consumers before deployment; remove column references first.\n\n" +

        "RULE 2 – Column Rename Without Alias\n" +
        "  Classification: BREAKING\n" +
        "  Condition: A column is renamed in version N+1 and no backward-compatible alias view is created.\n" +
        "  Rationale: Existing queries referencing the old name will fail with 'column does not exist'.\n" +
        "  Required action: Create a compatibility view or update all consumers atomically.\n\n" +

        "RULE 3 – Type Change (Widening)\n" +
        "  Classification: NON-BREAKING\n" +
        "  Condition: A column's type changes to a strictly wider type (e.g. VARCHAR(20)→VARCHAR(100), INT→BIGINT).\n" +
        "  Rationale: Existing data still fits; read paths are unaffected.\n" +
        "  Required action: None for consumers; verify CHECK constraints still pass.\n\n" +

        "RULE 4 – Type Change (Narrowing or Structural)\n" +
        "  Classification: BREAKING\n" +
        "  Condition: A column's type changes to a narrower type, or to a structurally different type\n" +
        "             (e.g. VARCHAR→ENUM, NUMERIC→INTEGER, TEXT→VARCHAR with length < existing data max).\n" +
        "  Rationale: Existing data may not fit the new type; ORM mappings and cast logic must change.\n" +
        "  Required action: Data migration script required; update ORM and application cast paths.\n\n" +

        "RULE 5 – Table Removal\n" +
        "  Classification: BREAKING\n" +
        "  Condition: A table that existed in version N is absent in version N+1.\n" +
        "  Rationale: Any consumer querying that table will fail.\n" +
        "  Required action: Migrate consumers to the replacement or remove dependencies before deployment.\n\n" +

        "RULE 6 – New Nullable Column\n" +
        "  Classification: NON-BREAKING\n" +
        "  Condition: A new column is added with NULL as default or explicit DEFAULT value.\n" +
        "  Rationale: Existing INSERT statements omitting the column will still succeed.\n" +
        "  Required action: None required; consumers may opt in to reading the new column.\n\n" +

        "RULE 7 – New Not-Null Column with Default\n" +
        "  Classification: NON-BREAKING\n" +
        "  Condition: A new NOT NULL column is added with a non-null DEFAULT expression.\n" +
        "  Rationale: Existing rows are back-filled; existing INSERT statements use the default.\n" +
        "  Required action: Verify existing data satisfies any associated CHECK constraint.\n\n" +

        "RULE 8 – New Table\n" +
        "  Classification: NON-BREAKING\n" +
        "  Condition: A table is added that did not exist in the previous version.\n" +
        "  Rationale: No existing query paths are affected.\n" +
        "  Required action: None for existing consumers.\n\n" +

        "RULE 9 – Index Addition or Removal\n" +
        "  Classification: NON-BREAKING\n" +
        "  Condition: An index is created or dropped without changing underlying column definitions.\n" +
        "  Rationale: Indexes affect performance only; query semantics are unchanged.\n" +
        "  Required action: Monitor query plans after deployment.\n\n" +

        "RULE 10 – Constraint Tightening\n" +
        "  Classification: BREAKING\n" +
        "  Condition: A CHECK or UNIQUE constraint is added or tightened on an existing column.\n" +
        "  Rationale: Existing data or write paths may violate the new constraint.\n" +
        "  Required action: Audit existing data; update application validation before deployment.\n\n" +

        string.Join("\n", Enumerable.Range(1, 15).Select(i =>
            $"# Policy note {i}: all classification decisions are final per the DBA review board; appeals must go through the schema RFC process."));

    private static readonly string ConsumerRegistry =
        "# Consumer Registry\n" +
        "# Maintained by: Platform Integration team\n" +
        "# Last updated: 2024-10-01\n" +
        "# Format: consumer name, pinned schema version, tables/columns used\n\n" +

        "CONSUMER: ConsumerA\n" +
        "  Pinned schema version: v1\n" +
        "  Tables used:\n" +
        "    users        – id, email, name, phone, role, is_active\n" +
        "    products     – id, name, price, category_id\n" +
        "    orders       – id, user_id, status, total, created_at\n" +
        "    order_items  – order_id, product_id, quantity, unit_price\n" +
        "  Description: Legacy billing system; read-only access.\n\n" +

        "CONSUMER: ConsumerB\n" +
        "  Pinned schema version: v2\n" +
        "  Tables used:\n" +
        "    users        – id, email, name, phone, is_active, created_at\n" +
        "    orders       – id, user_id, status, shipping_address, total\n" +
        "  Description: Customer-facing web portal; reads user profiles and order status.\n\n" +

        "CONSUMER: ConsumerC\n" +
        "  Pinned schema version: v2\n" +
        "  Tables used:\n" +
        "    products     – id, name, price, tags, category_id, description\n" +
        "    reviews      – product_id, rating, body, created_at\n" +
        "    categories   – id, name, parent_id, sort_order\n" +
        "  Description: Product search and recommendation engine.\n\n" +

        "CONSUMER: ConsumerD\n" +
        "  Pinned schema version: v2\n" +
        "  Tables used:\n" +
        "    categories   – id, name, parent_id, sort_order\n" +
        "    products     – id, name, category_id, legacy_code\n" +
        "  Description: CMS integration that renders category navigation trees using sort_order.\n\n" +

        "CONSUMER: ConsumerE\n" +
        "  Pinned schema version: v3\n" +
        "  Tables used:\n" +
        "    inventory    – product_id, warehouse, qty\n" +
        "    products     – id, name, price, legacy_code\n" +
        "  Description: Warehouse management system; reads inventory levels and product codes.\n\n" +

        "CONSUMER: ConsumerF\n" +
        "  Pinned schema version: v4\n" +
        "  Tables used:\n" +
        "    orders       – id, user_id, status, shipping_address, total, created_at\n" +
        "    order_items  – order_id, product_id, quantity, unit_price\n" +
        "    users        – id, name, email\n" +
        "  Description: Logistics and shipping label generation service.\n\n" +

        string.Join("\n", Enumerable.Range(1, 10).Select(i =>
            $"# Registry note {i}: consumers must register schema version upgrades with the Platform Integration team before deployment."));
}
