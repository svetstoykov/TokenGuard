Here are the precise findings grounded entirely in the provided excerpts.

### Factual Summary
| Fact | Value / Symbol | Source Evidence |
|------|----------------|-----------------|
| **Command Implementation Paths** | `pkg/cmd/pr/list/list.go`, `pkg/cmd/issue/list/list.go` | Lines 105 (`PR`), 104 (`Issue`) |
| **Default Result Allowance** | `30` | `IntVarP(&opts.LimitResults, "limit", "L", 30, ...)` in both files |
| **Search-Result Ceiling** | `100` | `min(limit, 100)` initializes per-page GraphQL requests in both `http.go` helpers |
| **Capped Field in Result Struct** | `SearchCapped` | `api.PullRequestAndTotalCount.SearchCapped` & `api.IssuesAndTotalCount.SearchCapped` |

---

### Detailed Trace & Analysis

#### 1. Command Entry & State Filters
Both commands validate inputs early in `list.go` and expose identical structural flags (`--author`, `--app`, `--label`, `--search`, etc.).
* **PR (`pkg/cmd/pr/list/list.go`)**: State enum allows `["open", "closed", "merged", "all"]`. Default is `"open"`. Explicitly blocks mutually exclusive `--author` + `--app` flags.
* **Issue (`pkg/cmd/issue/list/list.go`)**: State enum allows `["open", "closed", "all"]`. Default is `"open"`. Lacks a dedicated `"merged"` state since merged is a superset/alias often handled differently in issue workflows.

#### 2. Routing: Ordinary List vs Search Path
The HTTP helpers implement a decision gate based on filter complexity:
* **PR Path (`pkg/cmd/pr/list/http.go`)**: `shouldUseSearch()` evaluates `filters.Draft != nil || filters.Author != "" || filters.Assignee != "" || filters.Search != "" || len(filters.Labels) > 0`.
  * *False*: Delegates to the shared lister: `prShared.NewLister(httpClient).List(...)`, which uses standard object-scoped GraphQL queries scoped to the repository.
  * *True*: Invokes `searchPullRequests()`, firing the unified `search(query:, type: ISSUE)` GraphQL endpoint.
* **Issue Path (`pkg/cmd/issue/list/http.go`)**: Immediately routes to `searchIssues()`. Includes a prerequisite capability check (`repository.hasIssuesEnabled`) to fail fast if the target repo disabled its issue tracker.

#### 3. Pagination, Page-Size Reduction & Stopping Conditions
Both search implementations paginate through the `search` endpoint using cursor-based fetching (`after`/`endCursor`).
* **Ceiling Enforcement**: Regardless of the user's `--limit`, no single HTTP request exceeds `100` nodes: `pageLimit := min(limit, 100)` (PR `l.66`) and `perPage := min(limit, 100)` (Issue `l.150`).
* **Final-Page Reduction**: To avoid returning empty tail pages, the allocator shrinks the payload size once results accumulate:
  * PR `l.98`: `pageLimit = min(pageLimit, limit - len(res.PullRequests))`
  * Issue `l.187`: `variables["perPage"] = min(perPage, limit - len(ic.Issues))`
* **Termination Gates**:
  1. **Budget Hit**: `if len(items) == limit { break loop }`. The client stops fetching once it hits the exact user-requested count.
  2. **Exhaustion**: `if !PageInfo.hasNextPage { break }`. Stops when the API signals no more pages exist.
* **Deduplication**: PR search maintains `var check = make(map[int]struct{})` to skip duplicate PR numbers returned across pages. Issue search does not apply explicit deduplication in the provided excerpt.

#### 4. Result Structs & JSON Exports
* **PR (`api/queries_pr.go`)**: `PullRequestAndTotalCount` exports `TotalCount`, `PullRequests`, and `SearchCapped`. `SearchCapped` is set to `true` when `limit > 1000`, signaling to downstream UI/renderers that the total count is provisional. JSON export hooks into `api.PullRequestFields`.
* **Issue (`api/queries_issue.go`)**: `IssuesAndTotalCount` exports `Issues`, `TotalCount`, and `SearchCapped`. Identical ceiling-logic flagging. JSON export hooks into `api.IssueFields`.

---

### Proposed `evidence.md` Content

```markdown
# Evidence: CLI Issue & PR Listing Defaults, Limits, and Pagination

## Source Paths
- PR list command: `pkg/cmd/pr/list/list.go`
- Issue list command: `pkg/cmd/issue/list/list.go`

## Numeric Facts
default-list-limit: 30
search-result-ceiling: 100

## Capped Field
Result struct field marking search as capped: `SearchCapped`
- PR: `api/queries_pr.go` (`PullRequestAndTotalCount.SearchCapped`)
- Issue: `api/queries_issue.go` (`IssuesAndTotalCount.SearchCapped`)
Set to `true` when `limit > 1000`.

## Path Tracing & Pagination Logic
### Command Entry & Filter Routing
Both commands parse flags in their respective `list.go` files. `--limit/-L` defaults to `30`.
- PR `list.go` l.106: State enum is `["open", "closed", "merged", "all"]`, defaulting to `"open"`.
- Issue `list.go` l.103: State enum is `["open", "closed", "all"]`, defaulting to `"open"`.

HTTP helpers decide between the ordinary GraphQL object queries and the unified `search` query:
- PR HTTP helper (`pkg/cmd/pr/list/http.go` `l.12`): `shouldUseSearch()` triggers the search path if Draft, Author, Assignee, Search query, or Labels are specified. Otherwise, it falls back to `prShared.NewLister().List()`.
- Issue HTTP helper (`pkg/cmd/issue/list/http.go`): Uses the `search(type: $type, ...)` query, preceded by a repository capability check (`hasIssuesEnabled`).

### Page-Size Reduction & Final Request
Both search implementations cap individual GraphQL requests to **100** items regardless of the user-defined `--limit`:
- PR `http.go` l.66: `pageLimit := min(limit, 100)`
- Issue `http.go` l.150: `perPage := min(limit, 100)`
To prevent over-fetching on the final network round-trip, page sizes shrink dynamically based on how many results have already been accumulated:
- PR `http.go` l.98: `pageLimit = min(pageLimit, limit - len(res.PullRequests))`
- Issue `http.go` l.187: `variables["perPage"] = min(perPage, limit - len(ic.Issues))`

### Stopping Conditions
Pagination loops terminate when either:
1. The accumulated result length matches the requested `--limit` (`if len(items) == limit { break loop }`).
2. `PageInfo.hasNextPage` evaluates to `false` (API exhaustion).
PR search additionally deduplicates items across pages using a `map[int]struct{}` keyed by item number before appending to the final slice.

### Exported Fields
- PR Result (`PullRequestAndTotalCount`): `TotalCount`, `PullRequests`, `SearchCapped`. JSON payloads use `api.PullRequestFields`.
- Issue Result (`IssuesAndTotalCount`): `Issues`, `TotalCount`, `SearchCapped`. JSON payloads use `api.IssueFields`.
```