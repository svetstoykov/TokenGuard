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
            "    ## Classification – list each change in Removed or Changed sections with prefix [BREAKING] or [NON-BREAKING] per migration-rules.txt. If no breaking changes exist, write exactly: 'No breaking changes detected.'\n" +
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
            "  all four version hops. Each entry must use exactly this format:\n" +
            "    [BREAKING] | <version hop> | <table.column> | <change type> | <one-line description>\n" +
            "  Example: [BREAKING] | v2→v3 | users.phone | COLUMN RENAME | Renamed to phone_number; existing queries will fail.\n\n" +
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
        diff12.Should().Contain("## Added", "schema-diff-v1-to-v2.md must have an Added section");
        diff12.Should().Contain("reviews", "the reviews table was added in v2 and must appear in the diff");
        diff12.Should().Contain("tags", "the tags column was added to products in v2");
        diff12.Should().Contain("shipping_address", "shipping_address was added to orders in v2");
        diff12.Should().NotContain("[BREAKING]", "all v1→v2 changes are non-breaking additions");

        // v2 → v3: phone→phone_number rename (BREAKING), categories.sort_order dropped (BREAKING), audit_log added
        diff23.Should().Contain("[BREAKING]", "schema-diff-v2-to-v3.md must classify breaking changes");
        diff23.Should().Contain("phone_number", "the phone→phone_number rename is a breaking change in v3");
        diff23.Should().Contain("sort_order", "categories.sort_order was dropped in v3, a breaking change");
        diff23.Should().Contain("audit_log", "the audit_log table was added in v3");

        // v3 → v4: orders.status VARCHAR→ENUM (BREAKING), inventory.qty→quantity (BREAKING), products.weight added
        diff34.Should().Contain("[BREAKING]", "schema-diff-v3-to-v4.md must classify breaking changes");
        diff34.Should().Contain("status", "the orders.status type change must appear in the diff");
        diff34.Should().Contain("quantity", "the inventory.qty→quantity rename is a breaking change in v4");
        diff34.Should().Contain("weight", "products.weight was added in v4 as a non-breaking addition");

        // v4 → v5: products.legacy_code dropped (BREAKING), shipping_address split (BREAKING), users.mfa_enabled added
        diff45.Should().Contain("[BREAKING]", "schema-diff-v4-to-v5.md must classify breaking changes");
        diff45.Should().Contain("legacy_code", "products.legacy_code was dropped in v5, a breaking change");
        diff45.Should().Contain("shipping_street", "shipping_address was split into shipping_street in v5");
        diff45.Should().Contain("mfa_enabled", "users.mfa_enabled was added in v5 as a non-breaking addition");

        // ORM drift: v3 ORM has Phone not PhoneNumber; v4 ORM has Qty not Quantity
        ormDrift.Should().Contain("Version 3", "orm-drift-report.md must have a section for Version 3");
        ormDrift.Should().Contain("phone_number", "orm-v3.txt still maps to 'Phone' causing drift on phone_number");
        ormDrift.Should().Contain("Version 4", "orm-drift-report.md must have a section for Version 4");
        ormDrift.Should().Contain("quantity", "orm-v4.txt still maps to 'Qty' causing drift on quantity");

        // Migration plan covers all hops
        migPlan.Should().Contain("v1", "migration-plan.md must cover the v1→v2 hop");
        migPlan.Should().Contain("v2", "migration-plan.md must cover the v2→v3 hop");
        migPlan.Should().Contain("phone_number", "migration plan must describe the phone rename step");
        migPlan.Should().Contain("quantity", "migration plan must describe the qty→quantity rename step");

        // Consumer impact
        consumerMatrix.Should().Contain("ConsumerA", "consumer-impact-matrix.md must include every registered consumer");
        consumerMatrix.Should().Contain("ConsumerD", "ConsumerD uses categories.sort_order which was dropped in v3");
        consumerMatrix.Should().Contain("ConsumerF", "ConsumerF uses orders.shipping_address which was split in v5");
        consumerMatrix.Should().Contain("ConsumerB", "ConsumerB uses users.phone which was renamed in v3");

        // Breaking changes summary
        breakingSummary.Should().Contain("[BREAKING]", "breaking-changes-summary.md must list breaking changes");
        breakingSummary.Should().Contain("phone_number", "the phone rename must appear in the summary");
        breakingSummary.Should().Contain("sort_order", "the sort_order removal must appear in the summary");
        breakingSummary.Should().Contain("legacy_code", "the legacy_code removal must appear in the summary");
        breakingSummary.Should().Contain("quantity", "the qty rename must appear in the summary");

        finalText.Should().Contain(CompletionMarker, "the final model message must contain the completion marker");
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

        string.Join("\n", Enumerable.Range(1, 60).Select(i =>
            $"-- Schema note {i}: all v1 tables comply with the platform naming conventions (snake_case, UUID PKs, timestamptz audit columns).")) +
        "\n\n-- ============================================================\n" +
        "-- Extended Schema Documentation for TokenGuard Benchmark\n" +
        "-- Purpose: Provide additional context for LLM schema analysis\n" +
        "-- ============================================================\n" +
        "-- Table: users\n" +
        "--   Primary purpose: Store authenticated user accounts\n" +
        "--   Expected row count: 10,000 - 1,000,000\n" +
        "--   Retention policy: Soft delete via is_active flag\n" +
        "--   GDPR considerations: email and phone are PII fields\n" +
        "--   Index strategy: B-tree on email for login lookups, role for RBAC queries\n" +
        "--   Foreign key cascades: None (users is a root table)\n\n" +
        "-- Table: categories\n" +
        "--   Primary purpose: Product classification hierarchy\n" +
        "--   Expected row count: 50 - 500\n" +
        "--   Self-referential: parent_id creates tree structure\n" +
        "--   sort_order: Deprecated in v3, used for UI display sequencing\n" +
        "--   Index strategy: Unique on name for deduplication\n\n" +
        "-- Table: products\n" +
        "--   Primary purpose: Product catalog entries\n" +
        "--   Expected row count: 10,000 - 100,000\n" +
        "--   legacy_code: ERP integration field, removed in v5\n" +
        "--   Price constraints: Non-negative with CHECK constraint\n" +
        "--   Full-text search: description column suitable for FTS indexing\n\n" +
        "-- Table: inventory\n" +
        "--   Primary purpose: Per-warehouse stock tracking\n" +
        "--   Expected row count: 50,000 - 500,000\n" +
        "--   Composite unique: product_id + warehouse prevents duplicates\n" +
        "--   qty renamed to quantity in v4 (BREAKING change)\n" +
        "--   Write pattern: High update frequency during order processing\n\n" +
        "-- Table: orders\n" +
        "--   Primary purpose: Customer purchase records\n" +
        "--   Expected row count: 100,000 - 10,000,000\n" +
        "--   Status workflow: pending → confirmed → shipped → delivered\n" +
        "--   Cancelled orders: Terminal state, excluded from revenue reports\n" +
        "--   shipping_address: Denormalized snapshot, split into components in v5\n" +
        "--   Status type: VARCHAR(20) in v1-v3, ENUM in v4-v5\n\n" +
        "-- Table: order_items\n" +
        "--   Primary purpose: Line items within orders\n" +
        "--   Expected row count: 500,000 - 100,000,000\n" +
        "--   Unit price snapshot: Captures price at time of order\n" +
        "--   Historical analysis: Enables revenue reports without joining products\n\n" +
        "-- ============================================================\n" +
        "-- Data Governance and Compliance Notes\n" +
        "-- ============================================================\n" +
        "-- All tables include audit columns (created_at, updated_at)\n" +
        "-- Soft delete pattern: is_active flag on users table\n" +
        "-- PII fields: users.email, users.phone/phone_number\n" +
        "-- Encryption at rest: Enabled for password_hash field\n" +
        "-- Backup strategy: Continuous WAL archiving to S3\n" +
        "-- Replication: Streaming replication to 2 read replicas\n" +
        "-- Monitoring: pg_stat_statements enabled for query analysis\n";

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

        string.Join("\n", Enumerable.Range(1, 60).Select(i =>
            $"-- Schema note {i}: v2 is fully backward-compatible with v1; all additions are nullable or have defaults.")) +
        "\n\n-- ============================================================\n" +
        "-- Version 2.0 Migration Impact Assessment\n" +
        "-- ============================================================\n" +
        "-- reviews table: New feature, zero impact on existing consumers\n" +
        "--   Expected adoption: 10% of products will have reviews within 6 months\n" +
        "--   Storage impact: ~500KB per 1000 reviews (average 500 chars body)\n" +
        "--   Query pattern: Heavy read on product detail pages\n" +
        "--   Index recommendations: Composite (product_id, created_at) for pagination\n\n" +
        "-- products.tags: ARRAY type for flexible categorization\n" +
        "--   Use case: Free-form labels like 'sale', 'new', 'bestseller'\n" +
        "--   Query operators: @> (contains), && (overlaps)\n" +
        "--   GIN index recommended for production\n\n" +
        "-- orders.shipping_address: Denormalization for order immutability\n" +
        "--   Captures address at time of order (user may move later)\n" +
        "--   Stored as TEXT to accommodate international formats\n" +
        "--   Future evolution: Split into structured fields in v5\n\n" +
        "-- ix_products_name: B-tree index for product search\n" +
        "--   Use case: Autocomplete and search-as-you-type\n" +
        "--   Partial index candidate: WHERE is_active = true\n\n" +
        "-- ============================================================\n" +
        "-- Rollback Plan for v2\n" +
        "-- ============================================================\n" +
        "-- If rollback needed:\n" +
        "--   1. DROP TABLE reviews CASCADE;\n" +
        "--   2. ALTER TABLE products DROP COLUMN tags;\n" +
        "--   3. ALTER TABLE orders DROP COLUMN shipping_address;\n" +
        "--   4. DROP INDEX ix_products_name;\n" +
        "-- Estimated rollback time: < 5 minutes for 1M orders\n\n" +
        "-- ============================================================\n" +
        "-- Consumer Compatibility Matrix for v2\n" +
        "-- ============================================================\n" +
        "-- ConsumerA (v1 pinned): FULLY COMPATIBLE - no changes to used columns\n" +
        "-- ConsumerB (upgrading to v2): CAN USE shipping_address immediately\n" +
        "-- ConsumerC (new): Will use reviews and tags extensively\n" +
        "-- ConsumerD (v2 pinned): No impact on category navigation\n" +
        "-- ConsumerE (future): Will use inventory.qty (renamed in v4)\n" +
        "-- ConsumerF (future): Will use orders.shipping_address (split in v5)\n";

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

        string.Join("\n", Enumerable.Range(1, 60).Select(i =>
            $"-- Schema note {i}: v3 introduces two breaking changes; coordinate rename and drop with all consumers before deploying.")) +
        "\n\n-- ============================================================\n" +
        "-- Version 3.0 BREAKING CHANGES - Detailed Impact Analysis\n" +
        "-- ============================================================\n" +
        "-- CHANGE 1: users.phone → users.phone_number\n" +
        "--   Breaking severity: HIGH\n" +
        "--   Affected consumers: ConsumerA (v1), ConsumerB (v2)\n" +
        "--   Failure mode: 'column phone does not exist' on SELECT/INSERT/UPDATE\n" +
        "--   Migration path:\n" +
        "--     a) Create compatibility view: CREATE VIEW users_compat AS SELECT *, phone_number AS phone...\n" +
        "--     b) Or: Update ConsumerA and ConsumerB connection strings to v3 ORM\n" +
        "--   Data integrity: Phone numbers preserved, E.164 format now enforced\n\n" +
        "-- CHANGE 2: categories.sort_order DROPPED\n" +
        "--   Breaking severity: MEDIUM\n" +
        "--   Affected consumers: ConsumerC (v2), ConsumerD (v2)\n" +
        "--   ConsumerD CRITICAL: CMS navigation depends on sort_order\n" +
        "--   Failure mode: 'column sort_order does not exist' on SELECT\n" +
        "--   Migration path:\n" +
        "--     a) Client-side ordering: Move sort logic to application layer\n" +
        "--     b) Or: Add sort_order to separate categories_order table\n" +
        "--   ConsumerD must upgrade ORM or switch to manual ordering\n\n" +
        "-- CHANGE 3: users.locale ADDED (non-breaking)\n" +
        "--   Purpose: Internationalization support\n" +
        "--   Default: 'en' ensures existing rows have valid values\n" +
        "--   Affected: None - new nullable column with default\n\n" +
        "-- CHANGE 4: audit_log table ADDED (non-breaking)\n" +
        "--   Purpose: Compliance and debugging\n" +
        "--   Population: Trigger-based on users, products, orders tables\n" +
        "--   Retention: 90 days rolling (not yet implemented)\n\n" +
        "-- ============================================================\n" +
        "-- ORM Drift Detected in v3\n" +
        "-- ============================================================\n" +
        "-- Issue: ORM v3 still maps User.Phone → 'phone' column\n" +
        "-- Schema: Column renamed to 'phone_number'\n" +
        "-- Impact: Runtime NpgsqlException on User queries\n" +
        "-- Detection: Schema validation in CI/CD pipeline\n" +
        "-- Resolution: Update EF Core model or regenerate from database\n\n" +
        "-- ============================================================\n" +
        "-- Maintenance Window Requirements\n" +
        "-- ============================================================\n" +
        "-- Estimated downtime: 15-30 minutes for 1M user records\n" +
        "-- Recommended approach:\n" +
        "--   1. Deploy new column phone_number alongside phone\n" +
        "--   2. Backfill phone_number from phone\n" +
        "--   3. Update consumers to use phone_number\n" +
        "--   4. Drop phone column in subsequent release\n";

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

        string.Join("\n", Enumerable.Range(1, 60).Select(i =>
            $"-- Schema note {i}: v4 enum migration must use a transaction; ALTER COLUMN TYPE requires USING cast expression.")) +
        "\n\n-- ============================================================\n" +
        "-- Version 4.0 BREAKING CHANGES - Detailed Impact Analysis\n" +
        "-- ============================================================\n" +
        "-- CHANGE 1: orders.status VARCHAR(20) → order_status ENUM\n" +
        "--   Breaking severity: HIGH\n" +
        "--   Affected consumers: All consumers reading/writing orders.status\n" +
        "--   ConsumerA (v1): Uses orders.status - will fail after upgrade\n" +
        "--   ConsumerF (v4): Pinned to v4, uses ENUM type\n" +
        "--   Failure mode: Type mismatch in application code\n" +
        "--   Migration path:\n" +
        "--     a) CREATE TYPE order_status AS ENUM (...);\n" +
        "--     b) ALTER TABLE orders ALTER COLUMN status TYPE order_status USING status::order_status;\n" +
        "--     c) Update ORM mappings to use enum type\n\n" +
        "-- CHANGE 2: inventory.qty → inventory.quantity\n" +
        "--   Breaking severity: HIGH\n" +
        "--   Affected consumers: ConsumerE (v3), any direct SQL queries\n" +
        "--   Failure mode: 'column qty does not exist'\n" +
        "--   ORM Drift: OrmV4 still maps Qty → 'qty' causing runtime errors\n" +
        "--   Migration path:\n" +
        "--     ALTER TABLE inventory RENAME COLUMN qty TO quantity;\n" +
        "--     Update all ORM mappings and raw SQL queries\n\n" +
        "-- CHANGE 3: products.weight ADDED (non-breaking)\n" +
        "--   Purpose: Shipping cost calculation\n" +
        "--   Nullable: True - existing products can be updated gradually\n" +
        "--   Unit: Kilograms\n" +
        "--   Precision: NUMERIC(8,3) supports up to 9999.999 kg\n\n" +
        "-- CHANGE 4: ix_inventory_warehouse ADDED (non-breaking)\n" +
        "--   Purpose: Optimize warehouse-level inventory queries\n" +
        "--   Query pattern: WHERE warehouse = 'WAREHOUSE_001'\n\n" +
        "-- ============================================================\n" +
        "-- ORM Drift Detected in v4\n" +
        "-- ============================================================\n" +
        "-- Issue: ORM v4 still maps Inventory.Qty → 'qty' column\n" +
        "-- Schema: Column renamed to 'quantity'\n" +
        "-- Impact: EF Core query translation failures\n" +
        "-- Example failing query: context.Inventory.Where(i => i.Qty > 0)\n" +
        "-- Error: 42703: column i.qty does not exist\n" +
        "-- Resolution: Regenerate EF Core model from database or update column mapping\n\n" +
        "-- ============================================================\n" +
        "-- Migration Script Requirements\n" +
        "-- ============================================================\n" +
        "-- Script must run in single transaction:\n" +
        "-- BEGIN;\n" +
        "--   CREATE TYPE order_status AS ENUM ('pending','confirmed','shipped','delivered','cancelled');\n" +
        "--   ALTER TABLE orders ALTER COLUMN status DROP DEFAULT;\n" +
        "--   ALTER TABLE orders ALTER COLUMN status TYPE order_status USING status::order_status;\n" +
        "--   ALTER TABLE orders ALTER COLUMN status SET DEFAULT 'pending';\n" +
        "--   ALTER TABLE inventory RENAME COLUMN qty TO quantity;\n" +
        "--   ALTER TABLE inventory DROP CONSTRAINT ck_inventory_qty;\n" +
        "--   ALTER TABLE inventory ADD CONSTRAINT ck_inventory_quantity CHECK (quantity >= 0);\n" +
        "-- COMMIT;\n";

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

        string.Join("\n", Enumerable.Range(1, 60).Select(i =>
            $"-- Schema note {i}: v5 shipping split requires a data migration script; deploy in two phases to avoid downtime.")) +
        "\n\n-- ============================================================\n" +
        "-- Version 5.0 BREAKING CHANGES - Detailed Impact Analysis\n" +
        "-- ============================================================\n" +
        "-- CHANGE 1: products.legacy_code DROPPED\n" +
        "--   Breaking severity: HIGH\n" +
        "--   Affected consumers: ConsumerD (v2 uses legacy_code), ConsumerE (v3)\n" +
        "--   Failure mode: 'column legacy_code does not exist' on SELECT/INSERT\n" +
        "--   Migration path:\n" +
        "--     a) Identify all queries using legacy_code\n" +
        "--     b) Update to use products.id or external mapping table\n" +
        "--     c) Deploy consumer updates BEFORE schema migration\n" +
        "--   Data preservation: Archive legacy_code values to data lake before drop\n\n" +
        "-- CHANGE 2: orders.shipping_address SPLIT into structured columns\n" +
        "--   Breaking severity: HIGH\n" +
        "--   Affected consumers: ConsumerB (v2), ConsumerF (v4 uses shipping_address)\n" +
        "--   Column changes:\n" +
        "--     REMOVED: shipping_address (TEXT)\n" +
        "--     ADDED: shipping_street (VARCHAR 200)\n" +
        "--     ADDED: shipping_city (VARCHAR 100)\n" +
        "--     ADDED: shipping_country (VARCHAR 100)\n" +
        "--   Failure mode: 'column shipping_address does not exist'\n" +
        "--   Migration path - TWO PHASE DEPLOYMENT REQUIRED:\n" +
        "--     Phase 1:\n" +
        "--       - Add new columns shipping_street, shipping_city, shipping_country\n" +
        "--       - Parse existing shipping_address values and populate new columns\n" +
        "--       - Update consumers to write to new columns\n" +
        "--     Phase 2 (after all consumers updated):\n" +
        "--       - Drop shipping_address column\n" +
        "--   Parsing complexity: International addresses vary in format\n" +
        "--   Data loss risk: Unparseable addresses must be flagged for manual review\n\n" +
        "-- CHANGE 3: users.mfa_enabled ADDED (non-breaking)\n" +
        "--   Purpose: Multi-factor authentication enrollment tracking\n" +
        "--   Default: FALSE ensures existing users are not locked out\n" +
        "--   NOT NULL with default is safe for existing rows\n" +
        "--   Index: Partial index WHERE mfa_enabled = TRUE for quick lookups\n\n" +
        "-- CHANGE 4: ix_orders_shipping_country ADDED (non-breaking)\n" +
        "--   Purpose: Optimize logistics queries by destination country\n" +
        "--   Use case: Shipping provider selection, customs documentation\n\n" +
        "-- ============================================================\n" +
        "-- Consumer Impact Summary for v5\n" +
        "-- ============================================================\n" +
        "-- ConsumerA (v1): BROKEN - uses users.phone (renamed v3), categories.sort_order (dropped v3)\n" +
        "-- ConsumerB (v2): BROKEN - uses users.phone (renamed v3), orders.shipping_address (dropped v5)\n" +
        "-- ConsumerC (v2): BROKEN - uses categories.sort_order (dropped v3)\n" +
        "-- ConsumerD (v2): BROKEN - uses categories.sort_order (dropped v3), products.legacy_code (dropped v5)\n" +
        "-- ConsumerE (v3): BROKEN - uses inventory.qty (renamed v4), products.legacy_code (dropped v5)\n" +
        "-- ConsumerF (v4): BROKEN - uses orders.shipping_address (split v5)\n\n" +
        "-- ============================================================\n" +
        "-- Recommended Migration Timeline\n" +
        "-- ============================================================\n" +
        "-- Week 1-2: Update ConsumerF to use new shipping columns\n" +
        "-- Week 3-4: Archive legacy_code data, update ConsumerD and ConsumerE\n" +
        "-- Week 5: Deploy schema v5 migration\n" +
        "-- Week 6: Monitor for errors, update any missed queries\n" +
        "-- Week 7+: Deprecate ConsumerA, ConsumerB, ConsumerC (upgrade to v5 ORM)\n";

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

        string.Join("\n", Enumerable.Range(1, 45).Select(i =>
            $"# ORM note {i}: v1 entities use value object pattern for money fields; Price and UnitPrice map to Money record.")) +
        "\n\n# ============================================================================\n" +
        "# ORM Configuration Details for Entity Framework Core 8\n" +
        "# ============================================================================\n" +
        "# DbContext: ApplicationDbContext\n" +
        "#   Provider: Npgsql.EntityFrameworkCore.PostgreSQL (8.0.0)\n" +
        "#   Connection string: From environment variable DATABASE_URL\n" +
        "#   Retry strategy: Exponential backoff with 3 retries\n" +
        "#   Command timeout: 30 seconds\n" +
        "#   Query tracking: Disabled for read-only scenarios\n\n" +
        "# Entity: User\n" +
        "#   Table mapping: ToTable(\"users\")\n" +
        "#   Primary key: HasKey(u => u.Id)\n" +
        "#   Email uniqueness: HasIndex(u => u.Email).IsUnique()\n" +
        "#   Role index: HasIndex(u => u.Role)\n" +
        "#   Concurrency: Property(u => u.UpdatedAt).IsConcurrencyToken()\n\n" +
        "# Entity: Category\n" +
        "#   Self-referential FK: HasOne(c => c.Parent).WithMany(c => c.Children)\n" +
        "#   Cascade: OnDelete(DeleteBehavior.SetNull)\n" +
        "#   SortOrder: Default value 0, used for tree ordering\n\n" +
        "# Entity: Product\n" +
        "#   Money value object: OwnsOne(p => p.Price)\n" +
        "#   Category relationship: HasOne(p => p.Category).WithMany(c => c.Products)\n" +
        "#   LegacyCode: Optional, max length 50 chars\n\n" +
        "# Entity: Inventory\n" +
        "#   Composite key: HasKey(i => new { i.ProductId, i.Warehouse })\n" +
        "#   Qty property: Integer, non-negative, default 0\n" +
        "#   Note: Qty renamed to Quantity in v4\n\n" +
        "# Entity: Order\n" +
        "#   Status: String, default 'pending', check constraint for valid values\n" +
        "#   User relationship: HasOne(o => o.User).WithMany(u => u.Orders)\n" +
        "#   Total: Money value object, non-negative\n\n" +
        "# Entity: OrderItem\n" +
        "#   Composite relationship to Order and Product\n" +
        "#   UnitPrice: Snapshot value at time of order\n\n" +
        "# ============================================================================\n" +
        "# Data Seeding and Migration Strategy\n" +
        "# ============================================================================\n" +
        "# Initial migration: 20230120000001_InitialSchema\n" +
        "# Seed data: Admin user, default categories\n" +
        "# Migration history stored in __EFMigrationsHistory table\n" +
        "# Review required before applying migrations to production\n";

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

        string.Join("\n", Enumerable.Range(1, 45).Select(i =>
            $"# ORM note {i}: v2 adds Review entity and Tags array; both map cleanly to new schema columns.")) +
        "\n\n# ============================================================================\n" +
        "# ORM v2.0 Configuration Updates\n" +
        "# ============================================================================\n" +
        "# Migration: 20230701000001_AddReviewsAndProductEnhancements\n" +
        "# Backward compatible: Yes - all changes additive\n" +
        "# Rollback: Remove migration, drop reviews table, remove columns\n\n" +
        "# Entity: Product (UPDATED)\n" +
        "#   Tags property: Added List<string> mapped to TEXT[]\n" +
        "#   Npgsql mapping: .HasColumnType(\"text[]\")\n" +
        "#   Index: GIN index for array operations\n" +
        "#   ShippingAddress: Added string?, mapped to TEXT\n\n" +
        "# Entity: Review (NEW)\n" +
        "#   Table mapping: ToTable(\"reviews\")\n" +
        "#   Primary key: Id (Guid)\n" +
        "#   Product relationship: HasOne(r => r.Product).WithMany(p => p.Reviews)\n" +
        "#   User relationship: HasOne(r => r.User).WithMany(u => u.Reviews)\n" +
        "#   Rating: Short, range 1-5 enforced by CHECK constraint\n" +
        "#   Body: Optional text, max length 5000 chars\n" +
        "#   Indexes: Composite on (ProductId, CreatedAt) for pagination\n\n" +
        "# Entity: Order (UPDATED)\n" +
        "#   ShippingAddress: Added string?, denormalized snapshot\n\n" +
        "# Query optimization:\n" +
        "#   - Include reviews in product detail queries\n" +
        "#   - Filter products by tags using EF.Functions.Contains\n" +
        "#   - Use split queries for orders with many items\n\n" +
        "# ============================================================================\n" +
        "# Consumer ORM Compatibility\n" +
        "# ============================================================================\n" +
        "# ConsumerA (v1 pinned): Compatible - no breaking changes\n" +
        "# ConsumerB (v2): Can use ShippingAddress immediately\n" +
        "# ConsumerC (v2): New consumer, uses Reviews and Tags extensively\n" +
        "# ConsumerD (v2): No impact - category navigation unchanged\n";

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

        string.Join("\n", Enumerable.Range(1, 45).Select(i =>
            $"# ORM note {i}: v3 ORM was deployed before schema migration ran; Phone drift must be fixed before v3 goes live.")) +
        "\n\n# ============================================================================\n" +
        "# ORM v3.0 - DRIFT DETECTED - Critical Issues\n" +
        "# ============================================================================\n" +
        "# Migration: 20240115000001_BreakingChangesAndAuditLog\n" +
        "# Status: PARTIALLY APPLIED - Schema updated, ORM not updated\n" +
        "# Risk level: HIGH - Runtime errors expected\n\n" +
        "# DRIFT ISSUE 1: User.Phone column mapping\n" +
        "#   ORM mapping: User.Phone → 'phone'\n" +
        "#   Schema actual: Column renamed to 'phone_number'\n" +
        "#   Error: Npgsql.PostgresException (0x80004005): 42703: column phone does not exist\n" +
        "#   Affected queries: All User entity queries\n" +
        "#   Fix required: Update column mapping or regenerate model\n\n" +
        "# Entity: User (DRIFTED)\n" +
        "#   Phone property: Maps to non-existent 'phone' column\n" +
        "#   PhoneNumber property: Should map to 'phone_number'\n" +
        "#   Locale property: New, maps correctly to 'locale'\n\n" +
        "# Entity: Category (UPDATED)\n" +
        "#   SortOrder property: REMOVED from entity (matches schema)\n" +
        "#   Note: Consumers using SortOrder will get compile errors\n\n" +
        "# Entity: AuditLog (NEW)\n" +
        "#   Table: audit_log\n" +
        "#   Primary key: Id (long, BIGSERIAL)\n" +
        "#   Purpose: Row-level change tracking\n" +
        "#   Payload: JsonDocument mapped to JSONB\n" +
        "#   Indexes: (TableName, RecordId), (ChangedAt)\n\n" +
        "# ============================================================================\n" +
        "# Migration Script for ORM v3 Drift Fix\n" +
        "# ============================================================================\n" +
        "# Option 1: Update column annotation\n" +
        "#   [Column(\"phone_number\")]\n" +
        "#   public string? Phone { get; set; }\n\n" +
        "# Option 2: Regenerate from database\n" +
        "#   dotnet ef dbcontext scaffold ...\n\n" +
        "# Option 3: Compatibility view (temporary)\n" +
        "#   CREATE VIEW users_compat AS\n" +
        "#   SELECT *, phone_number AS phone FROM users;\n";

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

        string.Join("\n", Enumerable.Range(1, 45).Select(i =>
            $"# ORM note {i}: v4 Inventory drift introduced by parallel migration; EF Core scaffold was not rerun after ALTER COLUMN.")) +
        "\n\n# ============================================================================\n" +
        "# ORM v4.0 - DRIFT DETECTED - Inventory Column Mismatch\n" +
        "# ============================================================================\n" +
        "# Migration: 20240601000001_EnumAndInventoryChanges\n" +
        "# Status: SCHEMA APPLIED, ORM PARTIALLY UPDATED\n" +
        "# New drift detected: Inventory.Qty → 'qty' but column is now 'quantity'\n\n" +
        "# DRIFT ISSUE: Inventory.Qty column mapping\n" +
        "#   ORM mapping: Inventory.Qty → 'qty'\n" +
        "#   Schema actual: Column renamed to 'quantity'\n" +
        "#   Error: 42703: column i.qty does not exist\n" +
        "#   Affected: All Inventory queries using Qty property\n" +
        "#   Root cause: Parallel migration - schema updated before ORM regenerated\n\n" +
        "# Entity: Inventory (DRIFTED)\n" +
        "#   Qty property: Maps to non-existent 'qty' column\n" +
        "#   Quantity property: Should be added, maps to 'quantity'\n" +
        "#   Constraint: ck_inventory_quantity (was ck_inventory_qty)\n\n" +
        "# Entity: Order (UPDATED)\n" +
        "#   Status property: Changed from string to OrderStatus enum\n" +
        "#   Enum mapping: .HasConversion<string>() or native PostgreSQL enum\n" +
        "#   Type mapping: order_status PostgreSQL enum type\n\n" +
        "# ENUM: OrderStatus (NEW)\n" +
        "#   Values: Pending, Confirmed, Shipped, Delivered, Cancelled\n" +
        "#   Database type: CREATE TYPE order_status AS ENUM (...)\n" +
        "#   EF Core mapping: HasColumnType(\"order_status\")\n\n" +
        "# Entity: Product (UPDATED)\n" +
        "#   Weight property: Added decimal?, precision 8,3\n" +
        "#   Nullable: True, for gradual catalog update\n\n" +
        "# ============================================================================\n" +
        "# Impact Assessment for Inventory Drift\n" +
        "# ============================================================================\n" +
        "# ConsumerE (v3): CRITICAL - uses Inventory.Qty extensively\n" +
        "# Queries affected:\n" +
        "#   - context.Inventory.Where(i => i.Qty > threshold)\n" +
        "#   - context.Inventory.Sum(i => i.Qty)\n" +
        "#   - Any projection including Qty property\n" +
        "# Fix priority: P0 - blocks warehouse operations\n\n" +
        "# ============================================================================\n" +
        "# Resolution Steps\n" +
        "# ============================================================================\n" +
        "# 1. Immediate fix - Add property with correct mapping:\n" +
        "#    public int Quantity { get; set; }  // Maps to 'quantity'\n" +
        "#    [Obsolete(\"Use Quantity\")]\n" +
        "#    public int Qty => Quantity;  // Backward compat\n\n" +
        "# 2. Update all queries to use Quantity\n" +
        "# 3. Regenerate EF Core model in next sprint\n";

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

        string.Join("\n", Enumerable.Range(1, 45).Select(i =>
            $"# ORM note {i}: v5 entities were regenerated via EF Core scaffold after both schema migrations completed.")) +
        "\n\n# ============================================================================\n" +
        "# ORM v5.0 - FULLY SYNCHRONIZED\n" +
        "# ============================================================================\n" +
        "# Migration: 20241101000001_AddressSplitAndCleanup\n" +
        "# Status: SCHEMA AND ORM SYNCHRONIZED\n" +
        "# Drift status: NONE - all mappings validated\n\n" +
        "# Entity: User (UPDATED)\n" +
        "#   MfaEnabled property: Added bool, NOT NULL, default false\n" +
        "#   Column mapping: mfa_enabled\n" +
        "#   Partial index: WHERE mfa_enabled = true\n\n" +
        "# Entity: Product (UPDATED)\n" +
        "#   LegacyCode property: REMOVED from entity\n" +
        "#   Note: No backward compatibility - consumers must update\n" +
        "#   Data archive: legacy_code values exported to data lake\n\n" +
        "# Entity: Order (UPDATED - BREAKING)\n" +
        "#   ShippingAddress property: REMOVED\n" +
        "#   New properties:\n" +
        "#     ShippingStreet:  string?, max 200\n" +
        "#     ShippingCity:    string?, max 100\n" +
        "#     ShippingCountry: string?, max 100\n" +
        "#   Migration: Existing addresses parsed into components\n" +
        "#   Unparseable: Flagged for manual review (~2% of records)\n\n" +
        "# Entity: Inventory (FIXED)\n" +
        "#   Qty property: REMOVED\n" +
        "#   Quantity property: Maps correctly to 'quantity' column\n" +
        "#   Drift resolved: Column and property now aligned\n\n" +
        "# ============================================================================\n" +
        "# Consumer Migration Status for v5\n" +
        "# ============================================================================\n" +
        "# ConsumerF (v4→v5): UPDATED - now uses structured shipping address\n" +
        "#   Changes:\n" +
        "#     - Order.ShippingAddress → Order.ShippingStreet, ShippingCity, ShippingCountry\n" +
        "#     - Address parsing logic moved to application layer\n" +
        "#     - Validation: Country code ISO 3166-1 alpha-2\n\n" +
        "# ConsumerE (v3→v5): MUST UPDATE - multiple breaking changes\n" +
        "#   Changes required:\n" +
        "#     - Inventory.Qty → Inventory.Quantity (rename v4)\n" +
        "#     - Product.LegacyCode removed (drop v5)\n\n" +
        "# ConsumerD (v2→v5): MUST UPDATE - multiple breaking changes\n" +
        "#   Changes required:\n" +
        "#     - Category.SortOrder removed (drop v3)\n" +
        "#     - Product.LegacyCode removed (drop v5)\n\n" +
        "# ============================================================================\n" +
        "# Post-Migration Validation Checklist\n" +
        "# ============================================================================\n" +
        "# [x] All entity mappings validated against schema\n" +
        "# [x] No drift detected in scaffolded model\n" +
        "# [x] Integration tests passing for all consumers\n" +
        "# [x] Performance benchmarks within acceptable range\n" +
        "# [x] Legacy code data archived to S3\n" +
        "# [x] Shipping address parsing accuracy > 95%\n";

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

        "\n# ============================================================================\n" +
        "# Migration Policy - Detailed Implementation Guidelines\n" +
        "# ============================================================================\n\n" +
        "# RULE 1 Implementation Details - Column Removal\n" +
        "#   Step 1: Identify all consumers using the column via usage analytics\n" +
        "#   Step 2: Notify consumers 30 days before deprecation\n" +
        "#   Step 3: Soft deprecation - mark column as deprecated in documentation\n" +
        "#   Step 4: Wait for all consumers to remove dependencies\n" +
        "#   Step 5: Archive data to cold storage (retention: 7 years)\n" +
        "#   Step 6: Execute DROP COLUMN in maintenance window\n\n" +
        "# RULE 2 Implementation Details - Column Rename\n" +
        "#   Preferred approach: Add new column, backfill, deprecate old\n" +
        "#   Fast approach (high urgency):\n" +
        "#     - Create compatibility VIEW with old column name as alias\n" +
        "#     - Update consumers to use new name\n" +
        "#     - Drop view after migration complete\n\n" +
        "# RULE 4 Implementation Details - Type Change Narrowing\n" +
        "#   Data migration script requirements:\n" +
        "#     - Validate all existing data fits new type\n" +
        "#     - Create exception report for non-conforming rows\n" +
        "#     - Provide manual correction workflow for exceptions\n" +
        "#     - Run migration in transaction with rollback plan\n\n" +
        "# RULE 10 Implementation Details - Constraint Tightening\n" +
        "#   CHECK constraint addition workflow:\n" +
        "#     1. Add constraint as NOT VALID (skip existing data check)\n" +
        "#     2. Validate constraint in background: ALTER TABLE ... VALIDATE CONSTRAINT\n" +
        "#     3. Fix violating rows identified by validation\n" +
        "#     4. Constraint now enforced for all new writes\n\n" +
        "# ============================================================================\n" +
        "# Emergency Breaking Change Procedure\n" +
        "# ============================================================================\n" +
        "# For security or compliance issues requiring immediate breaking change:\n" +
        "#   1. Obtain approval from CISO or compliance officer\n" +
        "#   2. Create incident ticket with justification\n" +
        "#   3. Notify all consumers via emergency communication channel\n" +
        "#   4. Execute change with 24-hour notice (or immediate for critical security)\n" +
        "#   5. Provide 1:1 support for affected consumer teams\n\n" +
        "# ============================================================================\n" +
        "# Non-Breaking Change Fast Track\n" +
        "# ============================================================================\n" +
        "# Changes qualifying for fast-track (no DBA review required):\n" +
        "#   - New tables with no foreign keys to existing tables\n" +
        "#   - New nullable columns with no default\n" +
        "#   - New indexes on existing columns\n" +
        "#   - Comments and documentation updates\n" +
        "# Fast-track SLA: 24 hours from PR submission to merge\n\n" +
        "# ============================================================================\n" +
        "# Rollback Policy\n" +
        "# ============================================================================\n" +
        "# Every migration must include rollback script\n" +
        "# Rollback testing required in staging environment\n" +
        "# Maximum rollback window: 72 hours post-deployment\n" +
        "# After 72 hours, rollback requires new forward migration\n";

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

        "# ============================================================================\n" +
        "# Consumer Impact Analysis and Migration Paths\n" +
        "# ============================================================================\n\n" +
        "# ConsumerA - Legacy Billing System\n" +
        "#   Current status: CRITICAL - Multiple breaking changes pending\n" +
        "#   Pinned version: v1 (EOL, upgrade required)\n" +
        "#   Breaking changes affecting this consumer:\n" +
        "#     v3: users.phone renamed to phone_number\n" +
        "#     v3: categories.sort_order dropped\n" +
        "#   Migration path: Upgrade to v5 ORM, update column references\n" +
        "#   Timeline: Must migrate within 90 days (compliance requirement)\n" +
        "#   Support: Assigned DBA for migration assistance\n\n" +
        "# ConsumerB - Customer Web Portal\n" +
        "#   Current status: WARNING - One breaking change pending\n" +
        "#   Pinned version: v2\n" +
        "#   Breaking changes:\n" +
        "#     v3: users.phone renamed to phone_number\n" +
        "#     v5: orders.shipping_address split into structured columns\n" +
        "#   Migration path: Update to use new shipping columns, phone_number\n" +
        "#   Risk: Medium - shipping address parsing complexity\n\n" +
        "# ConsumerC - Search and Recommendations\n" +
        "#   Current status: WARNING - One breaking change\n" +
        "#   Pinned version: v2\n" +
        "#   Breaking changes:\n" +
        "#     v3: categories.sort_order dropped (affects category tree UI)\n" +
        "#   Migration path: Implement client-side sorting\n" +
        "#   Effort: Low - UI layer change only\n\n" +
        "# ConsumerD - CMS Integration\n" +
        "#   Current status: CRITICAL - Multiple breaking changes\n" +
        "#   Pinned version: v2\n" +
        "#   Breaking changes:\n" +
        "#     v3: categories.sort_order dropped (CRITICAL - navigation broken)\n" +
        "#     v5: products.legacy_code dropped (ERP integration affected)\n" +
        "#   Migration path:\n" +
        "#     - Implement manual category ordering\n" +
        "#     - Migrate ERP integration to use products.id\n" +
        "#   Effort: High - requires ERP team coordination\n\n" +
        "# ConsumerE - Warehouse Management\n" +
        "#   Current status: CRITICAL - Multiple breaking changes\n" +
        "#   Pinned version: v3\n" +
        "#   Breaking changes:\n" +
        "#     v4: inventory.qty renamed to quantity\n" +
        "#     v5: products.legacy_code dropped\n" +
        "#   Migration path: Update all quantity references, migrate from legacy_code\n" +
        "#   Effort: Medium - mostly find/replace operations\n\n" +
        "# ConsumerF - Logistics Service\n" +
        "#   Current status: WARNING - One breaking change\n" +
        "#   Pinned version: v4\n" +
        "#   Breaking changes:\n" +
        "#     v5: orders.shipping_address split into components\n" +
        "#   Migration path: Update to use structured address fields\n" +
        "#   Effort: Low-Medium - address parsing already implemented\n\n" +
        "# ============================================================================\n" +
        "# Consumer Upgrade Priority Matrix\n" +
        "# ============================================================================\n" +
        "# P0 (Immediate - blocking): ConsumerA, ConsumerD\n" +
        "# P1 (30 days): ConsumerE\n" +
        "# P2 (60 days): ConsumerB, ConsumerF\n" +
        "# P3 (90 days): ConsumerC\n";
}