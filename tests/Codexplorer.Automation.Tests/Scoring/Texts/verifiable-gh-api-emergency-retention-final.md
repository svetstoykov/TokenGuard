Here are the verified findings:

---

## REST Pagination

**REST next page URL extraction:**
- **Function name:** `findNextPage`
- **Source file:** `pkg/cmd/api/pagination.go`, lines 38–65
- **How it works:** Parses the HTTP response's multi-value `Link` header for an entry containing `; rel="next"`. Extracts the bare URL from angle-bracket notation (`<URL>; rel="next"`). Returns `(url, true)` when found, or `("", false)` otherwise. In `api.go` line 394, the returned value replaces `requestPath`, advancing the loop to the next page of results. Verified by `pagination_test.go` lines 17–56 covering three cases: no Link header (returns `"", false`), Link without "next" rel (returns `"", false`), and a properly formatted Link with `rel="next"` (returns correct URL, `true`).

**REST default page size (when pagination is enabled without explicit per_page):**
- **Value: 100**
- `rest-page-size: 100` recorded in `evidence.md`
- **Insertion point:** `pkg/cmd/api/api.go` line 334 — `addPerPage(requestPath, 100, params)`. Called unconditionally when `opts.Paginate && !isGraphQL`. If `per_page` already exists in `params` (from `-F`) or in the query portion of `requestPath` itself, `addPerPage` returns early (lines 95–96 and 103–104 of `pagination.go`), avoiding duplication. Verified by `api_test.go` lines 692–765 (default 100 applied to REST paginated requests) and `api_test.go` lines 707–765 (explicit `per_page=50` overrides to use 50 instead).

---

## GraphQL Pagination

**GraphQL end cursor extraction:**
- **Function name:** `findEndCursor`
- **Source file:** `pkg/cmd/api/pagination.go`, lines 71–91
- **How it works:** Decodes the JSON response into a generic map, checks for a top-level `pageInfo` key, then re-decodes the nested `pageInfo` object to extract its `endCursor` value — but only if `hasNextPage` is `true`. Top-level fields named `endCursor` or `hasNextPage` outside `pageInfo` are ignored. Returns empty string when `pageInfo` is absent, or when `hasNextPage` is `false`. Verified by `pagination_test.go` lines 58–95 covering five cases: blank JSON, unrelated top-level fields, valid `pageInfo` with hasNextPage true (returns cursor), multiple `pageInfo` blocks (first one used), and `hasNextPage: false` (returns "").

**Cursor update mechanism:** In `api.go` lines 418–422, after each page completes, `hasNextPage = endCursor != ""`. If truthy, `params["endCursor"] = endCursor` updates the parameter map so that the subsequent request carries the cursor. For POST requests to `/graphql`, this serialises as a variable in the request body JSON. Verified end-to-end by `api_test.go` lines 1008–1070 (`Test_apiRun_paginationGraphQL_slurp`), which confirms two responses are produced and the second request's variables contain `endCursor: "PAGE1_END"`.

---

## Explicit Page Sizes

Explicit `per_page` values bypass automatic insertion at `api.go` line 334 through dual-check logic in `addPerPage`:
1. **Via raw field flag:** `-F per_page=50` → stored in `params` map via `parseFields`. `addPerPage` checks `params[key] == "per_page"` at line 95 and skips appending.
2. **Embedded in path query string:** `issues?per_page=10` → parsed and checked within the URL query at line 103 before appending.
Verified by `api_test.go` lines 707–765 (`Test_apiRun_pagination_perPage`), 776–836 (`Test_apiRun_pagination_pathPerPage`), and unit tests in `pagination_test.go` lines 123–169 (`Test_addPerPage`).

GraphQL never receives a `per_page` query parameter regardless of mode — verified by `api_test.go` lines 847–909 (`Test_apiRun_pagination_graphQL_noPerPages`).

---

## Method Changes When Fields/Input Are Supplied

In `api.go` lines 317–319:
```go
if !opts.RequestMethodPassed && (len(params) > 0 || opts.RequestInputFile != "") {
    method = "POST"
}
```
When the user does not pass `-X/--method` explicitly and provides either raw fields (`-F`) or an input file (`--input`), the command auto-switches the HTTP method to `POST`. This overrides any non-GET method string passed via `--method`. Verified by `api_test.go` lines 1246–1278 (`Test_apiRun_inputFile`) and lines 1259–1281 showing DELETE with no body produces nil `gotRequest.Body`.

---

## Invalid Combinations

Three mutual-exclusivity validations prevent invalid flag combinations:

| Combination | Error Message | Location |
|-------------|---------------|----------|
| `--paginate` + non-GET + REST (non-graphql) | "the `--paginate` option is not supported for non-GET requests" | `api.go` lines 236–238 |
| `--paginate` + `--input` | "the `--paginate` option is not supported with `--input`" | `api.go` lines 240–244 |
| `--slurp` + `--jq` or `--template` | "the `--slurp` option is not supported with `--jq` or `--template`" | `api.go` lines 246–252 |

---

## Edge Cases Verified Against Tests

| Scenario | Source Lines | Finding |
|----------|-------------|---------|
| HEAD returning 4xx/5xx | `api_test.go` 1280–1307 | Returns `cmdutil.SilentError`, not fatal exit |
| DELETE with no fields | `api_test.go` 1259–1281 | Nil request body sent to transport |
| Cache TTL reuse | `api_test.go` 1309–1350 | Identical requests within TTL produce zero server roundtrips; output is empty |
| Placeholder `:` syntax | `api_test.go` 1352–1449 | `:owner/:repo` resolved via git remote lookup |
| Placeholder `{}` syntax | `api_test.go` 1352–1449 | `{owner}/{repo}` same resolution path |
| Detached HEAD branch resolution | `api_test.go` 1427–1449 | Fails with error rather than silently skipping |

---

## Unfinished Work

- `processResponse` (pagination.go lines 113–229): reads but not fully documented — covers `endCursor` extraction from `jsonReader`, jq filtering, template rendering, error formatting, and header writing in one function. A follow-up would trace the full render pipeline.
- `fields.go`: `parseFields` handles raw, quoted, and base64-encoded fields. Not read in detail.
- `http.go` / `http_test.go` beyond line 425: timeout handling, retry policies, verbose-mode logging, and cache-evaluation not explored.
- `jsonArrayWriter` lifecycle in `pkg/cmd/api/pagination.go` lines 151–229: `startPage`, `ReadFrom`, `Close` interactions across pages were skimmed but not exhaustively traced.

---

## Blockers

None identified. All evidence was obtained solely from repository source and test files using read-only tooling.

## Next Recommended Steps

1. Read `pagination.go` lines 113–229 (`processResponse`) end-to-end to document error-formatting paths (JSON `errors` arrays vs `message` strings) and the interplay between jq filters, templates, and pager output.
2. Explore `fields.go` to understand raw-vs-quoted-vs-base64 field parsing and how they feed into the `params` map consumed by `addPerPage` and `addQuery`.
3. Review `http.go` starting from the beginning to document client construction, preview-header injection (`Accept: application/vnd.github.*+json`), rate-limit headers, and cache-layer integration.
4. Examine remaining `http_test.go` coverage for timeouts, retries, verbose-request-body dumping, and cache-TTL boundary conditions.

REF-GH-API-4W7N