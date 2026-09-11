# Tom's URL Shortener in C

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

Visit the local address: `http://localhost:5071`

To stop the app:

- `Ctrl+C` will stop the app.
- Then stop the services with `docker compose down`.
- `docker compose down -v` also deletes the local database volumes.

## API

### Create A Link

```txt
POST /shorten

Content-Type: application/json
Body: {"url":"https://example.com"}

Returns:

    Success:
        201 Created + JSON string containing seven-character short code

    Error:
        400 Bad Request
        429 Too Many Requests
```

- Only absolute HTTP and HTTPS URLs are accepted.
- There is a character limit of 4096 characters for the URLs.
- Rate limiting:
  - Creation is limited to 10 requests per minute per IP.
  - There is a global concurrency limit of 1000 simultaneous requests.

### Redirect

```txt
GET /{code}

Returns:

    Success:
        302 Found

    Error:
        404 Not Found
```

- 302 is necessary for analytics, 301s get cached by the browser.
- Unknown codes return 404.
- Codes are case-sensitive.

### Playground Diagnostics

`GET /{code}/blank` runs the same lookup, cache operations, and click recording
as the redirect, but returns some stats instead of redirecting to the actual link.

```txt
Cache Hit
Lookup: 0.21ms
Handler Completed In: 0.22ms
```

- The times shown only record operations in the handler. Network latency is not
  included.
- The lookup time includes a Redis lookup, and in the case of a miss, a
  SQL lookup.
- These requests do increment the click counter for the link.

### Click Counts

```txt
GET /{code}/clicks

Returns:

    Success:
        200 OK + click count

    Error:
        404 Not Found
```

- Unknown codes return 404.
- Click counts update every second when the background service flushes.

## Decisions

- **Short codes**: use a cryptographic random generator with 62 possible characters.
  A case-sensitive unique SQL index enforces uniqueness. Retries up to three times
  on collision.

- **Caching**: Redis caches links for 48 hours and are reentered on cache misses.
  Failed cache reads fall back to SQL.

- **Background service**: click increments are handled by a background service so
  cache hit redirects remain fast. Batched click counts are written to SQL once
  per second.

- **Analytics**: click counts are best effort, they can be lost in situations like
  crashes or forced shutdowns.
