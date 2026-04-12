using FluentAssertions;

namespace TokenGuard.Benchmark.AgentWorkflow.Tasks;

/// <summary>
/// Defines a multi-version API contract audit scenario that forces the model to diff five consecutive API spec
/// versions, classify breaking vs. non-breaking changes per a deprecation policy, and produce per-hop migration
/// guides with a client impact analysis cross-referenced against a registered client registry.
/// </summary>
internal static class ApiContractAuditTask
{
    private const string CompletionMarker = "API_CONTRACT_AUDIT_COMPLETE";

    /// <summary>
    /// Creates the API contract audit task definition consumed by the shared E2E loop.
    /// </summary>
    public static AgentLoopTaskDefinition Create() => new(
        name: "ApiContractAudit",
        conversationName: "e2e-api-contract-audit",
        systemPrompt:
            "You are an API governance analyst running inside a TokenGuard E2E test. " +
            "Your job is to compare five API specification versions, classify every change according to the " +
            "provided deprecation policy, and produce a structured set of migration and impact artefacts. " +
            "You MUST use the provided tools for every file operation — do not invent endpoints, field names, " +
            "or client details without reading the actual source files first. " +
            "Work through every version pair methodically: read both specs, diff them, then write the output. " +
            "When all artefacts are complete, respond with exactly three bullet points. " +
            $"The final bullet must be '{CompletionMarker}'.",
        userMessage:
            "Task: perform a full API contract audit across five API specification versions.\n" +
            "Step 1 – list all files in the workspace, then read every file before producing any output:\n" +
            "  api-v1.txt, api-v2.txt, api-v3.txt, api-v4.txt, api-v5.txt, deprecation-policy.txt, client-registry.txt.\n" +
            "Step 2 – for each consecutive version pair, create a diff report using the exact filenames below.\n" +
            "  Each diff report must contain three sections:\n" +
            "    ## Added – new endpoints or fields not present in the previous version.\n" +
            "    ## Removed – endpoints or fields that existed in the previous version but are gone.\n" +
            "    ## Changed – endpoints or fields that exist in both versions but behave differently.\n" +
            "  For every entry in Removed or Changed, classify it as BREAKING or NON-BREAKING per deprecation-policy.txt.\n" +
            "  Filenames: v1-to-v2-diff.md, v2-to-v3-diff.md, v3-to-v4-diff.md, v4-to-v5-diff.md.\n" +
            "Step 3 – create 'migration-guide.md' with one section per version hop (v1→v2, v2→v3, v3→v4, v4→v5).\n" +
            "  Each section must list concrete steps a consumer must take to upgrade from the prior version.\n" +
            "  Sections with no breaking changes must still appear and state 'No breaking changes; upgrade freely.'\n" +
            "Step 4 – create 'client-impact-report.md' with one section per client from client-registry.txt.\n" +
            "  Each section must list every breaking change from Steps 2–3 that affects that client's pinned version " +
            "  and the specific endpoints or fields they use.\n" +
            "  Clients unaffected by any breaking change must still appear with the note 'No impact detected.'\n" +
            "Step 5 – create 'breaking-changes-summary.md' — a chronological list of every BREAKING change across " +
            "  all four version hops, with the version hop, the affected endpoint or field, and the change description.\n" +
            "Step 6 – read back each of the six output files to confirm they contain the expected content.\n" +
            "Do not claim completion until all six output files exist and are verified.",
        completionMarker: CompletionMarker,
        seedWorkspaceAsync: SeedAsync,
        assertOutcomeAsync: AssertAsync);

    /// <summary>
    /// Seeds the workspace with five verbose API specification files, a deprecation policy, and a client registry.
    /// </summary>
    private static async Task SeedAsync(string dir)
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "api-v1.txt"), ApiV1);
        await File.WriteAllTextAsync(Path.Combine(dir, "api-v2.txt"), ApiV2);
        await File.WriteAllTextAsync(Path.Combine(dir, "api-v3.txt"), ApiV3);
        await File.WriteAllTextAsync(Path.Combine(dir, "api-v4.txt"), ApiV4);
        await File.WriteAllTextAsync(Path.Combine(dir, "api-v5.txt"), ApiV5);
        await File.WriteAllTextAsync(Path.Combine(dir, "deprecation-policy.txt"), DeprecationPolicy);
        await File.WriteAllTextAsync(Path.Combine(dir, "client-registry.txt"), ClientRegistry);
    }

    /// <summary>
    /// Verifies that all six output artefacts were produced and contain the expected structural markers.
    /// </summary>
    private static async Task AssertAsync(string dir, string? finalText)
    {
        var v1v2 = await File.ReadAllTextAsync(Path.Combine(dir, "v1-to-v2-diff.md"));
        var v2v3 = await File.ReadAllTextAsync(Path.Combine(dir, "v2-to-v3-diff.md"));
        var v3v4 = await File.ReadAllTextAsync(Path.Combine(dir, "v3-to-v4-diff.md"));
        var v4v5 = await File.ReadAllTextAsync(Path.Combine(dir, "v4-to-v5-diff.md"));
        var guide = await File.ReadAllTextAsync(Path.Combine(dir, "migration-guide.md"));
        var impact = await File.ReadAllTextAsync(Path.Combine(dir, "client-impact-report.md"));
        var summary = await File.ReadAllTextAsync(Path.Combine(dir, "breaking-changes-summary.md"));

        v1v2.Should().Contain("## Added", because: "v1-to-v2-diff.md must have an Added section");
        v1v2.Should().Contain("## Changed", because: "v1-to-v2-diff.md must have a Changed section");
        v1v2.Should().Contain("/health", because: "GET /health was added in v2 and must appear in the diff");
        v1v2.Should().Contain("/products/search", because: "GET /products/search was added in v2");

        v2v3.Should().Contain("BREAKING", because: "v2-to-v3-diff.md must classify breaking changes");
        v2v3.Should().Contain("unitPrice", because: "the price→unitPrice rename is a breaking change introduced in v3");
        v2v3.Should().Contain("/auth/login", because: "POST /auth/login was added in v3");
        v2v3.Should().Contain("/categories", because: "GET /categories was removed in v3, a breaking change");

        v3v4.Should().Contain("BREAKING", because: "v3-to-v4-diff.md must classify breaking changes");
        v3v4.Should().Contain("permissions", because: "the role→permissions.role restructure is a breaking change in v4");
        v3v4.Should().Contain("sku", because: "the productId→sku rename in order items is a breaking change in v4");
        v3v4.Should().Contain("/catalog/", because: "GET /catalog/{id} was added in v4 as the replacement endpoint");

        v4v5.Should().Contain("BREAKING", because: "v4-to-v5-diff.md must classify breaking changes");
        v4v5.Should().Contain("/products/{id}", because: "GET /products/{id} was removed in v5");
        v4v5.Should().Contain("/auth/login", because: "POST /auth/login was removed in v5");

        guide.Should().Contain("v1", because: "migration-guide.md must cover the v1→v2 hop");
        guide.Should().Contain("v2", because: "migration-guide.md must cover the v2→v3 hop");
        guide.Should().Contain("v3", because: "migration-guide.md must cover the v3→v4 hop");
        guide.Should().Contain("v4", because: "migration-guide.md must cover the v4→v5 hop");
        guide.Should().Contain("unitPrice", because: "the guide must mention the breaking price field rename");

        impact.Should().Contain("ClientA", because: "client-impact-report.md must include every registered client");
        impact.Should().Contain("ClientD", because: "ClientD uses /categories which was removed in v3");
        impact.Should().Contain("ClientG", because: "ClientG uses PUT /users/{id} which changed to PATCH in v3");
        impact.Should().Contain("ClientE", because: "ClientE uses /products/{id} which was removed in v5");

        summary.Should().Contain("BREAKING", because: "breaking-changes-summary.md must list breaking changes");
        summary.Should().Contain("unitPrice", because: "the price rename must appear in the summary");
        summary.Should().Contain("/categories", because: "the /categories removal must appear in the summary");
        summary.Should().Contain("permissions", because: "the role restructure must appear in the summary");
    }

    private static readonly string ApiV1 =
        "# API Specification – Version 1.0\n" +
        "# Released: 2023-01-15\n" +
        "# Stability: stable\n" +
        "# Base URL: https://api.example.com/v1\n" +
        "# Auth: none required on most endpoints (see individual entries)\n" +
        "# Format: JSON request and response bodies throughout\n" +
        "#\n" +
        "# -------------------------------------------------------\n\n" +

        "ENDPOINT: GET /users\n" +
        "  Description: Returns a paginated list of all registered users.\n" +
        "  Query Parameters:\n" +
        "    page    (integer, optional, default: 1)   – Page index, 1-based.\n" +
        "    limit   (integer, optional, default: 20)  – Results per page, max 100.\n" +
        "  Request Body: none\n" +
        "  Response Body (200):\n" +
        "    { \"users\": [ { \"id\": string, \"name\": string, \"email\": string, \"role\": string } ], " +
        "\"total\": integer, \"page\": integer, \"limit\": integer }\n" +
        "  Notes: role is one of: admin, editor, viewer.\n\n" +

        "ENDPOINT: GET /users/{id}\n" +
        "  Description: Returns a single user by their unique identifier.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the user.\n" +
        "  Request Body: none\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, \"role\": string, \"createdAt\": string }\n" +
        "  Error Responses: 404 if user not found.\n\n" +

        "ENDPOINT: POST /users\n" +
        "  Description: Creates a new user account.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"email\": string (required), \"role\": string (optional, default: viewer) }\n" +
        "  Response Body (201):\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, \"role\": string, \"createdAt\": string }\n" +
        "  Error Responses: 400 if validation fails, 409 if email already registered.\n\n" +

        "ENDPOINT: PUT /users/{id}\n" +
        "  Description: Replaces a user record entirely. All fields must be provided.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the user.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"email\": string (required), \"role\": string (required) }\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, \"role\": string, \"updatedAt\": string }\n" +
        "  Error Responses: 400 if validation fails, 404 if user not found.\n\n" +

        "ENDPOINT: DELETE /users/{id}\n" +
        "  Description: Permanently deletes a user account.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the user.\n" +
        "  Request Body: none\n" +
        "  Response Body (204): empty\n" +
        "  Error Responses: 404 if user not found.\n\n" +

        "ENDPOINT: GET /products\n" +
        "  Description: Returns a paginated list of products.\n" +
        "  Query Parameters:\n" +
        "    page       (integer, optional, default: 1)  – Page index.\n" +
        "    limit      (integer, optional, default: 20) – Results per page.\n" +
        "    categoryId (string, optional)               – Filter by category UUID.\n" +
        "  Request Body: none\n" +
        "  Response Body (200):\n" +
        "    { \"products\": [ { \"id\": string, \"name\": string, \"price\": number, " +
        "\"categoryId\": string, \"stock\": integer } ], \"total\": integer }\n\n" +

        "ENDPOINT: GET /products/{id}\n" +
        "  Description: Returns a single product by UUID.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the product.\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"price\": number, \"categoryId\": string, " +
        "\"stock\": integer, \"description\": string, \"createdAt\": string }\n" +
        "  Error Responses: 404 if product not found.\n\n" +

        "ENDPOINT: POST /products\n" +
        "  Description: Creates a new product listing.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"price\": number (required), " +
        "\"categoryId\": string (required), \"stock\": integer (optional, default: 0), " +
        "\"description\": string (optional) }\n" +
        "  Response Body (201):\n" +
        "    { \"id\": string, \"name\": string, \"price\": number, \"categoryId\": string, \"stock\": integer }\n" +
        "  Error Responses: 400 if validation fails.\n\n" +

        "ENDPOINT: PUT /products/{id}\n" +
        "  Description: Replaces a product record entirely.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the product.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"price\": number (required), " +
        "\"categoryId\": string (required), \"stock\": integer (required) }\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"price\": number, \"categoryId\": string, \"stock\": integer }\n\n" +

        "ENDPOINT: DELETE /products/{id}\n" +
        "  Description: Removes a product listing permanently.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the product.\n" +
        "  Response Body (204): empty\n" +
        "  Error Responses: 404 if product not found.\n\n" +

        "ENDPOINT: GET /orders\n" +
        "  Description: Returns a paginated list of orders, optionally filtered.\n" +
        "  Query Parameters:\n" +
        "    userId (string, optional) – Filter to orders placed by a specific user.\n" +
        "    status (string, optional) – Filter by order status: pending, confirmed, shipped, delivered, cancelled.\n" +
        "    page   (integer, optional, default: 1)\n" +
        "    limit  (integer, optional, default: 20)\n" +
        "  Response Body (200):\n" +
        "    { \"orders\": [ { \"id\": string, \"userId\": string, \"status\": string, " +
        "\"total\": number, \"createdAt\": string } ], \"total\": integer }\n\n" +

        "ENDPOINT: GET /orders/{id}\n" +
        "  Description: Returns the full detail of a single order.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the order.\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"userId\": string, \"status\": string, \"total\": number, " +
        "\"items\": [ { \"productId\": string, \"quantity\": integer, \"unitPrice\": number } ], " +
        "\"createdAt\": string, \"updatedAt\": string }\n" +
        "  Error Responses: 404 if order not found.\n\n" +

        "ENDPOINT: POST /orders\n" +
        "  Description: Places a new order on behalf of a user.\n" +
        "  Request Body:\n" +
        "    { \"userId\": string (required), " +
        "\"items\": [ { \"productId\": string (required), \"quantity\": integer (required, min: 1) } ] (required, min 1 item) }\n" +
        "  Response Body (201):\n" +
        "    { \"id\": string, \"userId\": string, \"status\": \"pending\", \"total\": number, \"createdAt\": string }\n" +
        "  Error Responses: 400 if items are empty or quantities invalid, 404 if a productId does not exist.\n\n" +

        "ENDPOINT: PATCH /orders/{id}/status\n" +
        "  Description: Updates the status of an existing order.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the order.\n" +
        "  Request Body:\n" +
        "    { \"status\": string (required) } – Must be one of: pending, confirmed, shipped, delivered, cancelled.\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"status\": string, \"updatedAt\": string }\n" +
        "  Error Responses: 400 if status transition is invalid, 404 if order not found.\n\n" +

        "ENDPOINT: GET /categories\n" +
        "  Description: Returns the full list of product categories.\n" +
        "  Query Parameters: none\n" +
        "  Request Body: none\n" +
        "  Response Body (200):\n" +
        "    { \"categories\": [ { \"id\": string, \"name\": string, \"parentId\": string|null } ] }\n" +
        "  Notes: Categories form a two-level hierarchy; parentId is null for top-level categories.\n\n" +

        string.Join("\n", Enumerable.Range(1, 20).Select(i =>
            $"# Spec note {i}: all v1 endpoints follow REST conventions and return RFC 7807 problem details on error."));

    private static readonly string ApiV2 =
        "# API Specification – Version 2.0\n" +
        "# Released: 2023-06-01\n" +
        "# Stability: stable\n" +
        "# Base URL: https://api.example.com/v2\n" +
        "# Changes from v1: three new endpoints, two endpoint enhancements (all non-breaking).\n" +
        "# Auth: none required on most endpoints (see individual entries)\n" +
        "#\n" +
        "# -------------------------------------------------------\n\n" +

        "ENDPOINT: GET /users\n" +
        "  Description: Returns a paginated list of all registered users.\n" +
        "  Query Parameters:\n" +
        "    page    (integer, optional, default: 1)\n" +
        "    limit   (integer, optional, default: 20, max: 100)\n" +
        "  Response Body (200):\n" +
        "    { \"users\": [ { \"id\": string, \"name\": string, \"email\": string, \"role\": string } ], " +
        "\"total\": integer, \"page\": integer, \"limit\": integer }\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: GET /users/{id}\n" +
        "  Description: Returns a single user by UUID.\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, \"role\": string, \"createdAt\": string }\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: POST /users\n" +
        "  Description: Creates a new user account.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"email\": string (required), \"role\": string (optional, default: viewer) }\n" +
        "  Response Body (201):\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, \"role\": string, \"createdAt\": string }\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: PUT /users/{id}\n" +
        "  Description: Replaces a user record entirely.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"email\": string (required), \"role\": string (required) }\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, \"role\": string, \"updatedAt\": string }\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: DELETE /users/{id}\n" +
        "  Description: Permanently deletes a user account.\n" +
        "  Response Body (204): empty\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: GET /users/{id}/orders\n" +
        "  Description: NEW IN V2. Returns all orders associated with a specific user.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – UUID of the user.\n" +
        "  Query Parameters:\n" +
        "    status (string, optional) – Filter by order status.\n" +
        "    page   (integer, optional, default: 1)\n" +
        "    limit  (integer, optional, default: 20)\n" +
        "  Response Body (200):\n" +
        "    { \"orders\": [ { \"id\": string, \"status\": string, \"total\": number, \"createdAt\": string } ], " +
        "\"total\": integer }\n\n" +

        "ENDPOINT: GET /products\n" +
        "  Description: Returns a paginated list of products. ENHANCED IN V2: added sort and direction parameters.\n" +
        "  Query Parameters:\n" +
        "    page       (integer, optional, default: 1)\n" +
        "    limit      (integer, optional, default: 20)\n" +
        "    categoryId (string, optional)\n" +
        "    sort       (string, optional, default: name) – Sort field: name, price, stock, createdAt.\n" +
        "    direction  (string, optional, default: asc)  – Sort direction: asc or desc.\n" +
        "  Response Body (200):\n" +
        "    { \"products\": [ { \"id\": string, \"name\": string, \"price\": number, " +
        "\"categoryId\": string, \"stock\": integer } ], \"total\": integer }\n" +
        "  Notes: sort and direction are additive; all v1 queries remain valid.\n\n" +

        "ENDPOINT: GET /products/search\n" +
        "  Description: NEW IN V2. Full-text search across product names and descriptions.\n" +
        "  Query Parameters:\n" +
        "    q          (string, required)   – Search query string.\n" +
        "    categoryId (string, optional)   – Restrict search to a category.\n" +
        "    minPrice   (number, optional)   – Minimum price inclusive.\n" +
        "    maxPrice   (number, optional)   – Maximum price inclusive.\n" +
        "    page       (integer, optional, default: 1)\n" +
        "    limit      (integer, optional, default: 20)\n" +
        "  Response Body (200):\n" +
        "    { \"results\": [ { \"id\": string, \"name\": string, \"price\": number, \"score\": number } ], " +
        "\"total\": integer, \"query\": string }\n\n" +

        "ENDPOINT: GET /products/{id}\n" +
        "  Description: Returns a single product by UUID.\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"price\": number, \"categoryId\": string, " +
        "\"stock\": integer, \"description\": string, \"createdAt\": string }\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: POST /products\n" +
        "  Description: Creates a new product listing.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"price\": number (required), " +
        "\"categoryId\": string (required), \"stock\": integer (optional), \"description\": string (optional) }\n" +
        "  Response Body (201):\n" +
        "    { \"id\": string, \"name\": string, \"price\": number, \"categoryId\": string, \"stock\": integer }\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: PUT /products/{id}\n" +
        "  Description: Replaces a product record entirely.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"price\": number (required), " +
        "\"categoryId\": string (required), \"stock\": integer (required) }\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: DELETE /products/{id}\n" +
        "  Description: Removes a product listing.\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: GET /orders\n" +
        "  Description: Returns a paginated list of orders.\n" +
        "  Query Parameters:\n" +
        "    userId, status, page, limit – all unchanged from v1.\n" +
        "  Response Body (200): unchanged from v1.\n\n" +

        "ENDPOINT: GET /orders/{id}\n" +
        "  Description: Returns full order detail.\n" +
        "  Response Body (200): unchanged from v1.\n\n" +

        "ENDPOINT: POST /orders\n" +
        "  Description: Places a new order. ENHANCED IN V2: response now includes estimatedDelivery.\n" +
        "  Request Body:\n" +
        "    { \"userId\": string (required), " +
        "\"items\": [ { \"productId\": string (required), \"quantity\": integer (required) } ] (required) }\n" +
        "  Response Body (201):\n" +
        "    { \"id\": string, \"userId\": string, \"status\": \"pending\", \"total\": number, " +
        "\"createdAt\": string, \"estimatedDelivery\": string }\n" +
        "  Notes: estimatedDelivery is an ISO-8601 date. Additive change; v1 clients are unaffected.\n\n" +

        "ENDPOINT: PATCH /orders/{id}/status\n" +
        "  Description: Updates the status of an order.\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: GET /categories\n" +
        "  Description: Returns the full list of product categories.\n" +
        "  Notes: Unchanged from v1.\n\n" +

        "ENDPOINT: GET /health\n" +
        "  Description: NEW IN V2. Returns service health status for monitoring infrastructure.\n" +
        "  Request Body: none\n" +
        "  Response Body (200):\n" +
        "    { \"status\": \"ok\" | \"degraded\" | \"unhealthy\", \"version\": string, \"uptime\": integer }\n" +
        "  Notes: Always returns 200 HTTP status; use the body 'status' field to determine health.\n\n" +

        string.Join("\n", Enumerable.Range(1, 20).Select(i =>
            $"# Spec note {i}: v2 is fully backward compatible with v1; no migration required for existing consumers."));

    private static readonly string ApiV3 =
        "# API Specification – Version 3.0\n" +
        "# Released: 2024-01-10\n" +
        "# Stability: stable\n" +
        "# Base URL: https://api.example.com/v3\n" +
        "# BREAKING CHANGES IN V3 – read migration guide before upgrading.\n" +
        "# Changes from v2: auth endpoints added, PUT /users replaced, price field renamed,\n" +
        "#                  GET /categories removed, order filtering extended.\n" +
        "#\n" +
        "# -------------------------------------------------------\n\n" +

        "ENDPOINT: POST /auth/login\n" +
        "  Description: NEW IN V3. Authenticates a user and returns a short-lived access token.\n" +
        "  Request Body:\n" +
        "    { \"email\": string (required), \"password\": string (required) }\n" +
        "  Response Body (200):\n" +
        "    { \"token\": string, \"expiresAt\": string, \"userId\": string }\n" +
        "  Error Responses: 401 if credentials are invalid.\n\n" +

        "ENDPOINT: POST /auth/refresh\n" +
        "  Description: NEW IN V3. Exchanges a valid token for a fresh one before it expires.\n" +
        "  Headers:\n" +
        "    Authorization: Bearer <token> (required)\n" +
        "  Request Body: none\n" +
        "  Response Body (200):\n" +
        "    { \"token\": string, \"expiresAt\": string }\n" +
        "  Error Responses: 401 if token is expired or invalid.\n\n" +

        "ENDPOINT: GET /users\n" +
        "  Description: Returns a paginated list of users.\n" +
        "  Query Parameters: page, limit – unchanged.\n" +
        "  Response Body (200): unchanged from v2.\n\n" +

        "ENDPOINT: GET /users/{id}\n" +
        "  Description: Returns a single user by UUID.\n" +
        "  Response Body (200): unchanged from v2.\n\n" +

        "ENDPOINT: POST /users\n" +
        "  Description: Creates a new user account. Unchanged from v2.\n\n" +

        "ENDPOINT: PATCH /users/{id}\n" +
        "  Description: BREAKING CHANGE FROM V2. Replaces PUT /users/{id}.\n" +
        "  Reason for change: PATCH semantics are more accurate as callers may omit unchanged fields.\n" +
        "  PUT /users/{id} is removed; clients must switch to PATCH.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (optional), \"email\": string (optional), \"role\": string (optional) }\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, \"role\": string, \"updatedAt\": string }\n" +
        "  Error Responses: 400 if validation fails, 404 if user not found.\n\n" +

        "ENDPOINT: DELETE /users/{id}\n" +
        "  Description: Permanently deletes a user account.\n" +
        "  Headers:\n" +
        "    Authorization: Bearer <token> (required) – BREAKING CHANGE FROM V2: now requires authentication.\n" +
        "  Response Body (204): empty\n" +
        "  Error Responses: 401 if token missing or invalid, 404 if user not found.\n\n" +

        "ENDPOINT: GET /users/{id}/orders\n" +
        "  Description: Returns orders for a user. Unchanged from v2.\n\n" +

        "ENDPOINT: GET /products\n" +
        "  Description: Returns a paginated list of products. Unchanged from v2 (sort, direction retained).\n" +
        "  Response Body (200):\n" +
        "    { \"products\": [ { \"id\": string, \"name\": string, \"unitPrice\": number, " +
        "\"categoryId\": string, \"stock\": integer } ], \"total\": integer }\n" +
        "  Notes: The field 'price' is renamed to 'unitPrice' in all product response bodies. See POST /products.\n\n" +

        "ENDPOINT: GET /products/search\n" +
        "  Description: Full-text product search. Unchanged from v2.\n\n" +

        "ENDPOINT: GET /products/{id}\n" +
        "  Description: Returns a single product by UUID.\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"unitPrice\": number, \"categoryId\": string, " +
        "\"stock\": integer, \"description\": string, \"createdAt\": string }\n" +
        "  Notes: 'price' renamed to 'unitPrice' – BREAKING CHANGE.\n\n" +

        "ENDPOINT: POST /products\n" +
        "  Description: Creates a new product listing.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"unitPrice\": number (required), " +
        "\"categoryId\": string (required), \"stock\": integer (optional), \"description\": string (optional) }\n" +
        "  Notes: 'price' renamed to 'unitPrice' in request body – BREAKING CHANGE. Callers sending 'price' will receive 400.\n" +
        "  Response Body (201):\n" +
        "    { \"id\": string, \"name\": string, \"unitPrice\": number, \"categoryId\": string, \"stock\": integer }\n\n" +

        "ENDPOINT: PUT /products/{id}\n" +
        "  Description: Replaces a product record. Field 'price' renamed to 'unitPrice' – BREAKING CHANGE.\n" +
        "  Request Body:\n" +
        "    { \"name\": string (required), \"unitPrice\": number (required), " +
        "\"categoryId\": string (required), \"stock\": integer (required) }\n\n" +

        "ENDPOINT: DELETE /products/{id}\n" +
        "  Description: Removes a product listing. Unchanged.\n\n" +

        "ENDPOINT: GET /orders\n" +
        "  Description: Returns a paginated list of orders. ENHANCED IN V3: new date range filters.\n" +
        "  Query Parameters:\n" +
        "    userId, status, page, limit – unchanged from v2.\n" +
        "    startDate (string, optional) – ISO-8601 date; filter orders created on or after this date.\n" +
        "    endDate   (string, optional) – ISO-8601 date; filter orders created on or before this date.\n" +
        "  Response Body (200): unchanged from v2.\n" +
        "  Notes: startDate/endDate are additive; existing queries remain valid.\n\n" +

        "ENDPOINT: GET /orders/{id}\n" +
        "  Description: Returns full order detail. Unchanged from v2.\n\n" +

        "ENDPOINT: POST /orders\n" +
        "  Description: Places a new order. Unchanged from v2.\n\n" +

        "ENDPOINT: PATCH /orders/{id}/status\n" +
        "  Description: Updates the status of an order. Unchanged from v2.\n\n" +

        "ENDPOINT: GET /health\n" +
        "  Description: Returns service health. Unchanged from v2.\n\n" +

        "# REMOVED IN V3:\n" +
        "# GET /categories – BREAKING CHANGE. The categories resource has been merged into the product schema.\n" +
        "#   Categories are now embedded in product responses as 'categoryName' and 'categoryPath'.\n" +
        "#   Clients relying on GET /categories must migrate to the product filtering approach.\n\n" +

        string.Join("\n", Enumerable.Range(1, 20).Select(i =>
            $"# Spec note {i}: v3 introduces authentication infrastructure; all write endpoints will require tokens in v4."));

    private static readonly string ApiV4 =
        "# API Specification – Version 4.0\n" +
        "# Released: 2024-07-22\n" +
        "# Stability: stable\n" +
        "# Base URL: https://api.example.com/v4\n" +
        "# BREAKING CHANGES IN V4 – read migration guide before upgrading.\n" +
        "# Changes from v3: user role schema restructured, order item fields renamed,\n" +
        "#                  analytics endpoints added, catalog endpoint introduced,\n" +
        "#                  GET /products/{id} and POST /auth/login marked deprecated.\n" +
        "#\n" +
        "# -------------------------------------------------------\n\n" +

        "ENDPOINT: POST /auth/login\n" +
        "  Description: Authenticates a user. DEPRECATED IN V4 – use POST /auth/token instead.\n" +
        "  Deprecation Notice: This endpoint will be removed in v5. Migrate to POST /auth/token.\n" +
        "  Request Body: { \"email\": string, \"password\": string }\n" +
        "  Response Body (200): { \"token\": string, \"expiresAt\": string, \"userId\": string }\n\n" +

        "ENDPOINT: POST /auth/refresh\n" +
        "  Description: Exchanges a token for a fresh one. Unchanged from v3.\n\n" +

        "ENDPOINT: GET /analytics/summary\n" +
        "  Description: NEW IN V4. Returns aggregated platform metrics.\n" +
        "  Headers:\n" +
        "    Authorization: Bearer <token> (required)\n" +
        "  Query Parameters:\n" +
        "    period (string, optional, default: 7d) – Time window: 1d, 7d, 30d, 90d.\n" +
        "  Response Body (200):\n" +
        "    { \"totalUsers\": integer, \"totalOrders\": integer, \"totalRevenue\": number, " +
        "\"avgOrderValue\": number, \"period\": string }\n\n" +

        "ENDPOINT: GET /analytics/users\n" +
        "  Description: NEW IN V4. Returns user-level activity analytics.\n" +
        "  Headers:\n" +
        "    Authorization: Bearer <token> (required)\n" +
        "  Query Parameters:\n" +
        "    period (string, optional, default: 7d)\n" +
        "    sort   (string, optional, default: orders) – Sort by: orders, revenue, lastActive.\n" +
        "  Response Body (200):\n" +
        "    { \"users\": [ { \"userId\": string, \"orderCount\": integer, \"totalSpend\": number, " +
        "\"lastActiveAt\": string } ], \"total\": integer }\n\n" +

        "ENDPOINT: GET /users\n" +
        "  Description: Returns a paginated list of users.\n" +
        "  Response Body (200) – BREAKING CHANGE FROM V3: 'role' field moved into nested 'permissions' object.\n" +
        "    { \"users\": [ { \"id\": string, \"name\": string, \"email\": string, " +
        "\"permissions\": { \"role\": string, \"scopes\": [ string ] } } ], \"total\": integer }\n" +
        "  Notes: Clients reading user.role must now read user.permissions.role.\n\n" +

        "ENDPOINT: GET /users/{id}\n" +
        "  Description: Returns a single user by UUID.\n" +
        "  Response Body (200) – BREAKING CHANGE FROM V3: same 'role' → 'permissions.role' restructure.\n" +
        "    { \"id\": string, \"name\": string, \"email\": string, " +
        "\"permissions\": { \"role\": string, \"scopes\": [ string ] }, \"createdAt\": string }\n\n" +

        "ENDPOINT: POST /users\n" +
        "  Description: Creates a new user account. Unchanged from v3.\n\n" +

        "ENDPOINT: PATCH /users/{id}\n" +
        "  Description: Partially updates a user record. Unchanged from v3.\n\n" +

        "ENDPOINT: DELETE /users/{id}\n" +
        "  Description: Permanently deletes a user. Requires Authorization header (unchanged from v3).\n\n" +

        "ENDPOINT: GET /users/{id}/orders\n" +
        "  Description: Returns orders for a user. Unchanged from v3.\n\n" +

        "ENDPOINT: GET /products\n" +
        "  Description: Returns a paginated list of products. Unchanged from v3.\n\n" +

        "ENDPOINT: GET /products/search\n" +
        "  Description: Full-text product search. Unchanged from v3.\n\n" +

        "ENDPOINT: GET /products/{id}\n" +
        "  Description: Returns a single product by UUID. DEPRECATED IN V4 – use GET /catalog/{id} instead.\n" +
        "  Deprecation Notice: This endpoint will be removed in v5. Migrate to GET /catalog/{id}.\n" +
        "  Response Body (200): unchanged from v3 (unitPrice field retained).\n\n" +

        "ENDPOINT: GET /catalog/{id}\n" +
        "  Description: NEW IN V4. Canonical replacement for GET /products/{id} with extended metadata.\n" +
        "  Path Parameters:\n" +
        "    id (string, required) – Product UUID.\n" +
        "  Response Body (200):\n" +
        "    { \"id\": string, \"name\": string, \"unitPrice\": number, \"categoryId\": string, " +
        "\"categoryName\": string, \"stock\": integer, \"description\": string, " +
        "\"tags\": [ string ], \"createdAt\": string, \"updatedAt\": string }\n\n" +

        "ENDPOINT: POST /products\n" +
        "  Description: Creates a product. Unchanged from v3.\n\n" +

        "ENDPOINT: PUT /products/{id}\n" +
        "  Description: Replaces a product record. Unchanged from v3.\n\n" +

        "ENDPOINT: DELETE /products/{id}\n" +
        "  Description: Removes a product. Unchanged from v3.\n\n" +

        "ENDPOINT: GET /orders\n" +
        "  Description: Returns paginated orders. Unchanged from v3.\n\n" +

        "ENDPOINT: GET /orders/{id}\n" +
        "  Description: Returns full order detail. Unchanged from v3.\n\n" +

        "ENDPOINT: POST /orders\n" +
        "  Description: Places a new order.\n" +
        "  Request Body – BREAKING CHANGE FROM V3: item fields renamed.\n" +
        "    { \"userId\": string (required), " +
        "\"items\": [ { \"sku\": string (required), \"count\": integer (required, min: 1) } ] (required) }\n" +
        "  Notes: 'productId' renamed to 'sku'; 'quantity' renamed to 'count'. Callers sending old fields receive 400.\n" +
        "  Response Body (201): unchanged from v3.\n\n" +

        "ENDPOINT: PATCH /orders/{id}/status\n" +
        "  Description: Updates order status. Allowed values updated: added 'processing', removed 'pending'.\n" +
        "  Request Body:\n" +
        "    { \"status\": string } – Must be one of: processing, confirmed, shipped, delivered, cancelled.\n" +
        "  Notes: 'pending' is no longer a valid target status. Orders start in 'processing' state from v4 onwards.\n\n" +

        "ENDPOINT: GET /health\n" +
        "  Description: Returns service health. Unchanged from v3.\n\n" +

        string.Join("\n", Enumerable.Range(1, 20).Select(i =>
            $"# Spec note {i}: v4 deprecations are enforced in v5; plan migrations before the v5 release window."));

    private static readonly string ApiV5 =
        "# API Specification – Version 5.0\n" +
        "# Released: 2025-02-03\n" +
        "# Stability: stable\n" +
        "# Base URL: https://api.example.com/v5\n" +
        "# BREAKING CHANGES IN V5 – deprecated endpoints from v4 have been removed.\n" +
        "# Changes from v4: removed GET /products/{id}, removed POST /auth/login,\n" +
        "#                  added POST /auth/token, added GET /catalog,\n" +
        "#                  GET /users now requires Authorization header,\n" +
        "#                  GET /analytics/summary response restructured.\n" +
        "#\n" +
        "# -------------------------------------------------------\n\n" +

        "ENDPOINT: POST /auth/token\n" +
        "  Description: NEW IN V5. Authenticates a user and returns an access token. Replaces POST /auth/login.\n" +
        "  Request Body:\n" +
        "    { \"email\": string (required), \"password\": string (required), " +
        "\"scope\": string (optional, default: read:all) }\n" +
        "  Response Body (200):\n" +
        "    { \"accessToken\": string, \"tokenType\": \"Bearer\", \"expiresIn\": integer, " +
        "\"refreshToken\": string, \"userId\": string }\n" +
        "  Notes: POST /auth/login has been removed. Migrate to this endpoint. " +
        "Response shape differs: 'token' is now 'accessToken', 'expiresAt' replaced by 'expiresIn' (seconds).\n\n" +

        "ENDPOINT: POST /auth/refresh\n" +
        "  Description: Exchanges a refresh token for a new access token. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /users\n" +
        "  Description: Returns a paginated list of users.\n" +
        "  Headers:\n" +
        "    Authorization: Bearer <token> (required) – BREAKING CHANGE FROM V4: authentication now required.\n" +
        "  Query Parameters: page, limit – unchanged.\n" +
        "  Response Body (200): unchanged from v4 (permissions.role structure retained).\n\n" +

        "ENDPOINT: GET /users/{id}\n" +
        "  Description: Returns a single user by UUID. Unchanged from v4.\n\n" +

        "ENDPOINT: POST /users\n" +
        "  Description: Creates a new user account. Unchanged from v4.\n\n" +

        "ENDPOINT: PATCH /users/{id}\n" +
        "  Description: Partially updates a user record. Unchanged from v4.\n\n" +

        "ENDPOINT: DELETE /users/{id}\n" +
        "  Description: Permanently deletes a user. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /users/{id}/orders\n" +
        "  Description: Returns orders for a user. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /analytics/summary\n" +
        "  Description: Returns aggregated platform metrics.\n" +
        "  Headers:\n" +
        "    Authorization: Bearer <token> (required)\n" +
        "  Query Parameters: period – unchanged.\n" +
        "  Response Body (200) – BREAKING CHANGE FROM V4: all fields wrapped under 'data' key.\n" +
        "    { \"data\": { \"totalUsers\": integer, \"totalOrders\": integer, \"totalRevenue\": number, " +
        "\"avgOrderValue\": number }, \"period\": string, \"generatedAt\": string }\n" +
        "  Notes: Clients reading top-level fields (e.g. response.totalUsers) must now read response.data.totalUsers.\n\n" +

        "ENDPOINT: GET /analytics/users\n" +
        "  Description: Returns user-level activity analytics. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /products\n" +
        "  Description: Returns a paginated list of products. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /products/search\n" +
        "  Description: Full-text product search. Unchanged from v4.\n\n" +

        "# REMOVED IN V5:\n" +
        "# GET /products/{id} – BREAKING CHANGE. Was deprecated in v4. Use GET /catalog/{id} instead.\n" +
        "# POST /auth/login  – BREAKING CHANGE. Was deprecated in v4. Use POST /auth/token instead.\n\n" +

        "ENDPOINT: GET /catalog\n" +
        "  Description: NEW IN V5. Lists all catalog items with extended metadata.\n" +
        "  Query Parameters:\n" +
        "    page       (integer, optional, default: 1)\n" +
        "    limit      (integer, optional, default: 20)\n" +
        "    categoryId (string, optional)\n" +
        "    tags       (string, optional) – Comma-separated tag filter.\n" +
        "  Response Body (200):\n" +
        "    { \"items\": [ { \"id\": string, \"name\": string, \"unitPrice\": number, " +
        "\"categoryName\": string, \"tags\": [ string ], \"stock\": integer } ], \"total\": integer }\n\n" +

        "ENDPOINT: GET /catalog/{id}\n" +
        "  Description: Returns a catalog item by UUID. Unchanged from v4.\n\n" +

        "ENDPOINT: POST /products\n" +
        "  Description: Creates a product. Unchanged from v4.\n\n" +

        "ENDPOINT: PUT /products/{id}\n" +
        "  Description: Replaces a product record. Unchanged from v4.\n\n" +

        "ENDPOINT: DELETE /products/{id}\n" +
        "  Description: Removes a product. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /orders\n" +
        "  Description: Returns paginated orders. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /orders/{id}\n" +
        "  Description: Returns full order detail. Unchanged from v4.\n\n" +

        "ENDPOINT: POST /orders\n" +
        "  Description: Places a new order. Unchanged from v4 (sku/count fields retained).\n\n" +

        "ENDPOINT: PATCH /orders/{id}/status\n" +
        "  Description: Updates order status. Unchanged from v4.\n\n" +

        "ENDPOINT: GET /health\n" +
        "  Description: Returns service health. Unchanged from v4.\n\n" +

        string.Join("\n", Enumerable.Range(1, 20).Select(i =>
            $"# Spec note {i}: v5 is the current stable release; no further breaking changes are planned before v6 LTS."));

    private static readonly string DeprecationPolicy =
        "# API Deprecation and Breaking Change Policy\n" +
        "# Effective: 2023-01-01 | Revision: 3\n" +
        "#\n" +
        "# -------------------------------------------------------\n\n" +

        "SECTION 1: CLASSIFICATION OF CHANGES\n\n" +

        "A change is classified as BREAKING if consumers following the documented contract can no longer\n" +
        "send requests or parse responses without modifying their code. Examples:\n\n" +

        "  BREAKING – endpoint removal (the endpoint no longer exists).\n" +
        "  BREAKING – HTTP method change on an existing path (e.g. PUT → PATCH).\n" +
        "  BREAKING – required request field renamed or removed.\n" +
        "  BREAKING – required response field renamed, removed, or restructured.\n" +
        "  BREAKING – new required request header that was not previously required.\n" +
        "  BREAKING – allowed enum value removed from a field that consumers write.\n" +
        "  BREAKING – response body wrapped in an additional nesting layer.\n\n" +

        "A change is classified as NON-BREAKING if existing consumers can continue operating without changes:\n\n" +

        "  NON-BREAKING – new optional query parameter added.\n" +
        "  NON-BREAKING – new optional field added to a request body.\n" +
        "  NON-BREAKING – new field added to a response body (consumers must tolerate unknown fields).\n" +
        "  NON-BREAKING – new endpoint added.\n" +
        "  NON-BREAKING – allowed enum value added to a field that consumers read.\n" +
        "  NON-BREAKING – error response body enriched with additional context.\n\n" +

        "SECTION 2: DEPRECATION LIFECYCLE\n\n" +

        "  Phase 1 – Deprecated: the endpoint or field is marked deprecated in the spec.\n" +
        "             Consumers receive Deprecation response headers. No functional change.\n" +
        "  Phase 2 – Sunset announced: a removal date is set at least one major version ahead.\n" +
        "  Phase 3 – Removed: the endpoint or field is absent from the next major version spec.\n\n" +

        "  Policy guarantee: a deprecated item will not be removed in the same major version it was deprecated.\n" +
        "  Minimum notice: one full major version cycle (approximately six months).\n\n" +

        "SECTION 3: CLIENT IMPACT ASSESSMENT\n\n" +

        "  For each BREAKING change, the governance team must:\n" +
        "    1. Identify all registered clients consuming the affected endpoint or field.\n" +
        "    2. Notify client owners at least 30 days before the change is released.\n" +
        "    3. Provide a concrete migration path in the release migration guide.\n" +
        "    4. Offer a compatibility shim for at least one additional minor version where feasible.\n\n" +

        string.Join("\n", Enumerable.Range(1, 18).Select(i =>
            $"# Policy note {i}: all exceptions to this policy require VP Engineering approval and must be documented."));

    private static readonly string ClientRegistry =
        "# Registered API Consumer Registry\n" +
        "# Maintained by: API Governance Team\n" +
        "# Last updated: 2025-01-15\n" +
        "# Format: ClientId | PinnedVersion | Owner | Endpoints Used | Notes\n" +
        "#\n" +
        "# -------------------------------------------------------\n\n" +

        "CLIENT: ClientA\n" +
        "  Pinned Version: v1\n" +
        "  Owner: mobile-team@example.com\n" +
        "  Endpoints Used:\n" +
        "    GET /users\n" +
        "    GET /products\n" +
        "    POST /orders (reads: id, userId, status, total, createdAt)\n" +
        "  Notes: Legacy mobile client. Has not been updated since initial release.\n\n" +

        "CLIENT: ClientB\n" +
        "  Pinned Version: v2\n" +
        "  Owner: partner-integrations@example.com\n" +
        "  Endpoints Used:\n" +
        "    GET /users/{id}/orders\n" +
        "    GET /products/search\n" +
        "    GET /health\n" +
        "  Notes: Third-party partner integration. Migrated from v1 to v2 in Q3 2023.\n\n" +

        "CLIENT: ClientC\n" +
        "  Pinned Version: v3\n" +
        "  Owner: web-frontend@example.com\n" +
        "  Endpoints Used:\n" +
        "    POST /auth/login (reads: token, expiresAt)\n" +
        "    PATCH /orders/{id}/status\n" +
        "    GET /orders\n" +
        "  Notes: Main web application. Uses auth token for subsequent requests.\n\n" +

        "CLIENT: ClientD\n" +
        "  Pinned Version: v2\n" +
        "  Owner: reporting-team@example.com\n" +
        "  Endpoints Used:\n" +
        "    GET /categories\n" +
        "    GET /products (query: categoryId)\n" +
        "  Notes: Internal reporting dashboard. Relies on category hierarchy for product drill-down.\n\n" +

        "CLIENT: ClientE\n" +
        "  Pinned Version: v4\n" +
        "  Owner: catalog-service@example.com\n" +
        "  Endpoints Used:\n" +
        "    GET /products/{id} (reads: id, name, unitPrice, stock)\n" +
        "    PUT /products/{id}\n" +
        "  Notes: Internal catalog synchronisation service. Uses deprecated GET /products/{id}.\n\n" +

        "CLIENT: ClientF\n" +
        "  Pinned Version: v4\n" +
        "  Owner: analytics-team@example.com\n" +
        "  Endpoints Used:\n" +
        "    GET /analytics/summary (reads: totalUsers, totalOrders, totalRevenue directly from response root)\n" +
        "    GET /analytics/users\n" +
        "  Notes: Internal BI pipeline. Parses top-level fields of the analytics response.\n\n" +

        "CLIENT: ClientG\n" +
        "  Pinned Version: v1\n" +
        "  Owner: erp-integration@example.com\n" +
        "  Endpoints Used:\n" +
        "    PUT /users/{id}\n" +
        "    POST /products (sends: name, price, categoryId, stock)\n" +
        "    GET /users (reads: role field directly from user object)\n" +
        "  Notes: ERP system integration. Has not been updated. Sends 'price' field and uses PUT /users/{id}.\n\n" +

        "CLIENT: ClientH\n" +
        "  Pinned Version: v5\n" +
        "  Owner: new-mobile-team@example.com\n" +
        "  Endpoints Used:\n" +
        "    POST /auth/token\n" +
        "    GET /catalog\n" +
        "    GET /catalog/{id}\n" +
        "  Notes: New mobile client built against v5. Uses current stable endpoints only.\n\n" +

        string.Join("\n", Enumerable.Range(1, 15).Select(i =>
            $"# Registry note {i}: client owners must notify the governance team of any version upgrades within 14 days."));
}
