**Command Implementation Source Paths:**
- Pull Request List: ``pkg/cmd/pr/list/list.go``
- Issue List: ``pkg/cmd/issue/list/list.go``

**Default Result Allowance:**
Both commands default to fetching **30** items. Configured via `--limit` / `-L` (`cmd.Flags().IntVarP(&opts.LimitResults, "limit", "L", 30, ...)`) in ``pkg/cmd/pr/list/list.go``:105 and ``pkg/cmd/issue/list/list.go``:104. Validation ensures values `< 1` fail fast with a flag error (``list.go``:85-87, :81-83).

**Search-Result Ceiling:**
The GitHub GraphQL Search API imposes a hard ceiling of **1,000** results. Both commands proactively mark their result payloads when a user requests more (`SearchCapped: limit > 1000`) in ``pkg/cmd/pr/list/http.go``:69 and ``pkg/cmd/issue/list/http.go``:160. A runtime warning ("capped at 1000 results maximum") is printed to stderr upon completion if the cap was hit (``list.go``:197 and :203).

**Result-Struct Capped Flag:**
The boolean field identifying cap saturation is ``SearchCapped`` defined inside ``PullRequestAndTotalCount`` (``api/queries_pr.go``:16) and ``IssuesAndTotalCount`` (``api/queries_issue.go``:20).

**Execution Tracing:**
* **Path Divergence:** ``pkg/cmd/pr/list/http.go``:17 evaluates `shouldUseSearch(filters)`. This predicate returns true if any advanced filter is applied (`Draft`, `Author`, `Assignee`, explicit `Search` string, or non-empty `Labels`). True routes execution to `searchPullRequests`/`searchIssues`, which constructs a GraphQL query targeting the global `search` endpoint (`query PullRequestSearch` / `query IssueSearch`). False routes execution to `prShared.NewLister(...).List(...)`, using repository-scoped cursor-based pagination.
* **Page-Size Reduction:** Initial fetches clamp to `min(limit, 100)` to respect GitHub's API page maximum (``http.go``:69, :150). Before subsequent requests, the client calculates remaining quota and shrinks the next page size dynamically via `min(perPage, limit-len(results))` (``http.go``:98, :187). This prevents overshooting the target `limit` when the API returns fewer nodes than requested due to filtering or rate limits.
* **Stopping Conditions:** The `loop:` block terminates via two guarded exits: (1) `break loop` fires immediately when `len(res.PullRequests) == limit` or `len(ic.Issues) == limit` after appending, ensuring strict adherence to the user-requested cap (``http.go``:91-92, :178-179); (2) natural exit occurs when `resp.Search.PageInfo.HasNextPage` is `false`, indicating exhaustion (``http.go``:100, :183).
* **Exported Fields:** When `opts.Exporter != nil` (activated via `--json` flags), the run function short-circuits to `opts.Exporter.Write(opts.IO, listResult.PullRequests/Issues)` (``list.go``:192-193, :198-199). This bypasses terminal header generation, colorization, and the OS pager, serializing the raw slice directly to stdout.
* **State Filters:** PR listings accept `open`, `closed`, `merged`, or `all` via `--state` (``list.go``:106). Issue listings accept `open`, `closed`, or `all` (``list.go``:103). These enum variants are injected into the list options struct and translated to API queries during traversal.

```markdown
default-list-limit: 30
search-result-ceiling: 1000
```