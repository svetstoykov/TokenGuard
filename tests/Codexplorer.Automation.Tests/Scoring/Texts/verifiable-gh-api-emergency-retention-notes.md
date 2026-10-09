# API Command Pagination Evidence Report

## REST Page Size

**rest-page-size: 100**

When pagination is enabled (`--paginate`) and the request path is not GraphQL, `pkg/cmd/api/api.go` line 334 inserts `per_page=100` into the request query parameters via `addPerPage(requestPath, 100, params)`. If the user has already supplied `per_page` as a field value (via `-F per_page=N`) or in the query string of the request path, `addPerPage` skips adding it to avoid duplicates (see `pagination.go` lines 95-109).

Evidence: `api_test.go` tests at lines 692-765 verify this default page size of 100 is applied to paginated REST requests.

---

## REST Next Page URL Extraction

**Function:** `findNextPage`  
**Source file:** `pkg/cmd/api/pagination.go` (lines 38-65)

The function extracts the next REST page URL by parsing the `Link` header from the HTTP response. It looks for a link relation of type `"next"` within the multi-value Link header. The URL is extracted from angle-bracket notation `<URL>; rel="next"`.

```go
// In api.go line 394:
requestPath, hasNextPage = findNextPage(resp)
```

If no Link header exists or no `rel="next"` link relation is found, the function returns an empty string and `false`, causing the pagination loop in `apiRun` to terminate.

**Test coverage:** `pagination_test.go` lines 17-56 (`Test_findNextPage`) covers three cases:
- No Link header → returns ("", false)
- Link header without "next" rel → returns ("", false)
- Link header with `rel="next"` → returns the correct URL and true

---

## GraphQL End Cursor Extraction

**Function:** `findEndCursor`  
**Source file:** `pkg/cmd/api/pagination.go` (lines 71-91)

The function reads the JSON response body and checks whether the top-level object contains a `pageInfo` key. If present, it extracts the `endCursor` value from that nested object — but only if `hasNextPage` is `true`. If `hasNextPage` is `false` or `pageInfo` does not exist, the function returns an empty string.

Key detail: the function uses a two-step decode. First it decodes into a generic `map[string]interface{}`, then re-decodes the `pageInfo` value into another map to extract `endCursor` and `hasNextPage`. This prevents confusion with top-level fields named `endCursor` or `hasNextPage` that might exist elsewhere in the JSON (see test case "unrelated fields" in `pagination_test.go` lines 40-46 which verifies that top-level `endCursor` is ignored).

**Test coverage:** `pagination_test.go` lines 58-95 (`Test_findEndCursor`) covers five cases:
- Blank JSON `{}` → returns ""
- Top-level `hasNextPage`/`endCursor` outside `pageInfo` → returns ""
- Valid `pageInfo` with `hasNextPage: true` → returns the endCursor value
- Multiple `pageInfo` blocks → returns the first one's cursor
- `hasNextPage: false` → returns "" even though `endCursor` is present

### Cursor Update Loop Logic

In `api.go` lines 418-422, when pagination is active and the request is GraphQL:

```go
if isGraphQL {
    hasNextPage = endCursor != ""
    if hasNextPage {
        params["endCursor"] = endCursor
    }
}
```

On each iteration after processing the response:
1. If `findEndCursor` returned a non-empty string, `hasNextPage` stays `true` and the loop continues.
2. The returned `endCursor` value is set into the `params` map under the key `"endCursor"`.
3. On the next iteration, `addQuery` appends `&endCursor=<value>` to the GraphQL request path (or serialises it into the POST body).
4. For POST requests to `/graphql` (the common case), the request body is serialized as JSON. The `params` map becomes the root-level JSON object sent as the request body, so `endCursor` appears in the `variables` section of the GraphQL query payload.

**Test coverage:** `api_test.go` lines 1008-1070 (`Test_apiRun_paginationGraphQL_slurp`) and the subsequent assertion at lines 1057-1061 verify:
- Two responses are produced (first page with `hasNextPage: true`, second page with `hasNextPage: false`)
- The second request's variables contain `endCursor: "PAGE1_END"` confirming the cursor was propagated between pages

---

## Explicit Page Sizes

Explicit page sizes can be supplied in two ways:

1. **As a raw field value:** `-F per_page=50`. The `parseFields` function processes this into the `params` map. When `addPerPage` is called in `api.go` line 334, it checks if `per_page` already exists in `params` (line 95 of `pagination.go`). If it does, it returns early without appending anything.

2. **Embedded in the request path query string:** e.g., passing `issues?per_page=10` as the path argument. The `addPerPage` function parses the query portion of the path and if `per_page` is already present, it returns the path unchanged.

**Test evidence:**
- `api_test.go` lines 707-765 (`Test_apiRun_pagination_perPage`): passes `-F per_page=50 --paginate` and verifies both pages use `per_page=50` in their URLs.
- `api_test.go` lines 776-836 (`Test_apiRun_pagination_pathPerPage`): passes `issues?per_page=50` as the path and verifies `per_page=50` is used throughout.
- `api_test.go` lines 847-909 (`Test_apiRun_pagination_graphQL_noPerPages`): confirms `per_page` is never added to GraphQL requests regardless of pagination mode.
- `pagination_test.go` lines 123-169 (`Test_addPerPage`): unit tests for `addPerPage` confirm that explicit `per_page` in params or query string is preserved without duplication.

---

## Method Changes When Fields/Input Are Supplied

**Location:** `api.go` lines 317-319

```go
if !opts.RequestMethodPassed && (len(params) > 0 || opts.RequestInputFile != "") {
    method = "POST"
}
```

When the user does not explicitly pass a method flag (`-X/--method`) and either provides fields (`-F`) or an input file (`--input`), the command auto-switches to `POST` regardless of what the user wrote for `--method`.

This is tested in:
- `api_test.go` lines 1246-1278 (`Test_apiRun_inputFile`): verifies that passing `RequestInputFile` changes the HTTP method to `POST` even without explicit method override.

---

## Invalid Combinations

Three invalid combinations are caught by validation logic in `api.go`:

### 1. Paginate + non-GET + non-GraphQL REST
**Location:** `api.go` lines 236-238
```go
if opts.Paginate && !strings.EqualFold(opts.RequestMethod, "GET") && opts.RequestPath != "graphql" {
    return cmdutil.FlagErrorf("the `--paginate` option is not supported for non-GET requests")
}
```

Non-GET methods (PUT, PATCH, DELETE, etc.) cannot be paginated over REST endpoints. The exception is `graphql` because GraphQL pagination works differently (cursor-based on the same endpoint).

### 2. Paginate + Input File
**Location:** `api.go` lines 240-244
```go
if err := cmdutil.MutuallyExclusive(
    "the `--paginate` option is not supported with `--input`",
    opts.Paginate,
    opts.RequestInputFile != "",
); err != nil {
    return err
}
```

Cannot combine streaming paginated requests with a request body from a file. The rationale is that paginated requests reuse the same parameters across pages, but a file input might represent varying payloads.

### 3. Slurp + jq Template
**Location:** `api.go` lines 246-252
```go
if err := cmdutil.MutuallyExclusive(
    "the `--slurp` option is not supported with `--jq` or `--template`",
    opts.Slurp,
    opts.FilterOutput != "",
    opts.Template != "",
); err != nil {
    return err
}
```

Cannot slurp multiple pages into a JSON array while simultaneously applying a filter expression or template, because slurp changes the output format entirely (wrapping all pages in an array).

---

## Edge Cases Verified Against Tests

### HEAD requests returning non-2xx
`api_test.go` lines 1280-1307: A HEAD request that receives a 422 status triggers `cmdutil.SilentError`, not a fatal error. This allows HEAD calls for existence checks.

### DELETE requests omitting request body
`api_test.go` lines 1259-1281: DELETE requests with no fields send no body (`gotRequest.Body` is nil). This follows Go's http package convention — a nil Body signals "no body" to the transport.

### Cache respects TTL
`api_test.go` lines 1309-1350: When `CacheTTL` is set, identical requests within the TTL window hit the cache and produce zero HTTP server requests (only one `requestCount++` despite two `apiRun` calls). Output is empty because cached responses are not written to stdout.

### Placeholder substitution in paths
`api_test.go` lines 1352-1449: Both `:` syntax (e.g., `:owner/:repo`) and `{}` syntax (e.g., `{owner}/{repo}`) work for base repo substitution. Branch placeholders (`:branch` / `{branch}`) resolve via a callback function. When git is in detached HEAD state, branch resolution fails with an error rather than silently skipping.

---

## Summary of Key Findings

| Item | Value/Detail | Source |
|------|-------------|--------|
| REST default page size | 100 | `api.go:334`, `pagination_test.go:135-142` |
| REST next page extractor | `findNextPage()` | `pagination.go:38-65` |
| GraphQL end cursor extractor | `findEndCursor()` | `pagination.go:71-91` |
| Cursor update mechanism | `params["endCursor"] = endCursor` | `api.go:418-422` |
| Explicit per_page bypass | Check in `addPerPage` | `pagination.go:95-96, 103-104` |
| Auto-method change to POST | When fields or input provided | `api.go:317-319` |
| Paginate + non-REST restriction | Only GET allowed | `api.go:236-238` |
| Paginate + input mutual exclusivity | Blocked by `MutuallyExclusive` | `api.go:240-244` |
| Slurp + jq/template mutual exclusivity | Blocked by `MutuallyExclusive` | `api.go:246-252` |
