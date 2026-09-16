# Tom's URL Shortener API in `C#`

A small ASP.NET Core API that takes long URLs, creates short links, redirects
users, and records click counts. This is a C# rewrite of my first portfolio
project, originally built with TypeScript, Express, and PostgreSQL.

The project focuses on core web API functionality, database use, caching,
and background services.

It uses .NET 10, Entity Framework Core, SQL Server and Redis.

## Run Locally

Prerequisites:

- .NET 10 SDK
- Docker with Docker Compose

Clone the repo:

```bash
git clone https://github.com/tcchilds-dev/csharp-url-shortener
cd csharp-url-shortener
```

Start up docker databases:

```bash
docker compose up -d
```

Run the app:

```bash
dotnet run --project UrlShortener.Api --launch-profile http
```

The API runs at [http://localhost:5071](http://localhost:5071).

To stop the app:

- `Ctrl+C` will stop the app.
- Then stop the services with `docker compose down`.
- `docker compose down -v` also deletes the local database volumes.

### Frontend Playground

With Node.js installed and the API running, open a second terminal:

```bash
cd frontend
npm install
npm run dev
```

Open the URL printed by Vite (usually [http://localhost:5173](http://localhost:5173)).
The React playground uses Tailwind CSS and DaisyUI, overkill I know, but it's
familiar. A terminal element simulates the terminal commands and outputs.
“Use a Link” calls `/{code}/blank` to show lookup stats.

## API

### OpenApi Documentation

`localhost:5071/openapi/api.json`

### Dependency-Aware Health Check

```txt
GET /healthz

Description: Returns the health of the application.

Output:
  Healthy   ->  all checks passed successfully.
  Degraded  ->  Redis is unavailable.
  Unhealthy ->  SQL is unavailable.
```

### Create A Link

```txt
POST /shorten

Content-Type: application/json
Body: {"url":"https://example.com"}

Description: Creates and returns a new short code for the supplied URL.

Success:
  200 OK
    Returns the generated code.

Error:
  400 Bad Request
    The supplied URL was invalid.

  429 Too Many Requests
```

Notes:

- Only absolute HTTP and HTTPS URLs are accepted.
- There is a character limit of 4096 characters for the URLs.
- Rate limiting:
  - Creation is limited to 10 requests per minute per IP.
  - There is a global concurrency limit of 1000 simultaneous requests.

### Redirect

```txt
GET /{code}

Description: Redirects the user to the supplied short code's associated URL and
             records a click.

Success:
  302 Found
    User redirected.

Error:
  404 Not Found
    An entry for the supplied code could not be found.
```

Notes:

- 302 is necessary for analytics, 301s get cached by the browser.
- Codes are case-sensitive.

### Redirect Stats

```txt
GET /{code}/blank

Description: Runs the same lookup, cache operations, and click recording as
             the redirect, but returns some performance stats instead of
             redirecting the user.

Success:
  200 OK
    Returns stats for the operations.
    Example:
      Cache Hit
      Lookup: 0.21ms
      Handler Completed In: 0.22ms

Error:
  404 Not Found
```

Notes:

- The times shown only record operations in the handler. Network latency is not
  included.
- The lookup time includes a Redis lookup, and in the case of a miss, a
  SQL lookup.

### Click Counts

```txt
GET /{code}/clicks

Description: Returns the click count for a supplied short link.

Success:
  200 OK
    Returns the click count.

Error:
  404 Not Found
    An entry for the supplied code could not be found.

```

Notes:

- Click counts update every second when the background service flushes.

## Decisions

- **Short codes**: use a cryptographic random generator with 62 possible characters.
  A case-sensitive unique SQL index enforces uniqueness. Retries up to three times
  on collision.

- **Caching**: Redis caches links for 48 hours and are reentered on cache misses.
  Failed cache reads fall back to SQL.

- **Background service**: click increments are handled by a background service so
  cache hit redirects remain fast. Batched click counts are written to SQL once
  per second. Counts are aggregated in memory for up to 10,000 distinct links.
  At capacity, clicks for tracked links are still accepted, clicks for additional
  links are dropped. Failed writes retain their counts, and new clicks continue
  accumulating. Successful writes subtract only the persisted counts, preserving
  clicks received during the flush.

- **Analytics**: click counts are best effort, they can be lost in situations like
  crashes or forced shutdowns.

## Integration Tests

Tests use `xUnit` and test SQL and Redis databases in Docker. The test host
follows ASP.NET Core's `WebApplicationFactory` approach.

To run the tests:

```bash
docker compose -p url-shortener-tests -f compose.test.yaml up -d --wait
dotnet test
```

To stop the docker containers:

```bash
docker compose -p url-shortener-tests -f compose.test.yaml down -v
```

- The first run downloads database images.
- A unique database is created for each API test and deleted afterward.
- Coverage includes:
  - link creation and redirects
  - input validation and length limits
  - case-sensitive SQL uniqueness and cache lookups
  - collision retries
  - rate limiting
  - Redis failure fallback
  - graceful worker shutdown

## Load Testing

> [!NOTE] Please note that whilst the original basic K6 load test was written by
> myself. The current load testing script and tool was written by AI, that
> includes this README section on it. My reasoning for using AI here is that K6
> was intended primarily to be used as a way to interact with my codebase, rather
> than being a significant element of my authored codebase itself.

Prerequisite: Install [Grafana k6](https://grafana.com/docs/k6/latest/set-up/install-k6/).
Run all commands from the repository root, with the Docker databases running.
Start the API in a separate terminal:

```bash
dotnet run --project UrlShortener.Api --configuration Release --launch-profile http
```

### Run a workload

With Bash available, the wrapper prepares links, runs k6, and verifies clicks:

```bash
./load-test.sh hot -e RATE=1000
./load-test.sh mixed -e RATE=1000
./load-test.sh distinct -e RATE=1000
```

The preparation utility inserts 10,000 links directly into SQL on its first run,
warms their Redis entries, and writes `load-test-data.json`. Later preparations
reuse those links and refresh the click-count baseline, they do not reset counts.
This avoids the API's limit of 10 link creations per minute. The API must have
started at least once to apply database migrations.

Choose one workload per run:

```txt
SCENARIO:
hot (default) -> one hot link that every request uses
mixed         -> ~80% across 10 popular links, 20% across 1000 other links
distinct      -> one link per iteration
```

The workload defaults to `hot`; additional arguments are passed to `k6 run`.
Use `./load-test.sh --help` for examples. The wrapper stops if preparation fails,
still verifies clicks if k6 thresholds fail, and exits unsuccessfully if either
k6 or verification fails. The API and databases must already be running.

To run the steps manually instead:

```bash
dotnet run --project tools/LoadTestData -- prepare
k6 run -e SCENARIO=mixed -e RATE=1000 k6-test.js
dotnet run --project tools/LoadTestData -- verify
```

Verification compares the SQL click-count increase with
successful redirects in `k6-summary.json`, polling for up to 30 seconds for the
worker to catch up. It exits unsuccessfully on a mismatch or a summary from a
different preparation. Both the k6 thresholds and this separate verification must
pass. A mismatch can indicate dropped counts, duplicated counts, a backlog that
has not drained, or unrelated traffic to the test links.

Use these links exclusively for one test at a time. Finish verification before
preparing another run; preparation assumes the previous worker backlog has drained.
If verification times out, investigate or retry with a longer timeout before
capturing another baseline:

```bash
dotnet run --project tools/LoadTestData -- verify 60
```

### Settings and Interpretation

- Defaults: 100 iterations/second for 30 seconds, 20 preallocated virtual users,
  with the maximum also set to 20. Each iteration makes one redirect request.
  `MAX_VUS` defaults to `PREALLOCATED_VUS`, so the pool does not grow during a run
  unless explicitly requested. Increase `PREALLOCATED_VUS` to start with a larger
  pool.
- Options: `SCENARIO`, `BASE_URL`, `RATE`, `DURATION`, `PREALLOCATED_VUS`,
  `MAX_VUS`, and `P95_MS`. `SCENARIO` defaults to `hot`.
- Thresholds require all response checks to pass, at least one successful redirect,
  fewer than 1% failed redirect requests, p95 below 100ms, and no dropped iterations.
- The console shows formatted metrics and checks using Grafana's summary helper
  (downloaded by k6 from `jslib.k6.io`); `k6-summary.json` contains all collected
  summary metrics, thresholds, and the preparation identifier.
- All three workloads start with **warm cache entries**. They do not establish
  cold-cache, dependency-outage, or link-creation performance.
