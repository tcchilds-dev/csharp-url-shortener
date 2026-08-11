# Tom's Simple URL Shortener in C\#

> [!WARNING] this project is a WIP

This project is a simple URL Shortener API written in C#.

It was made as a simple learning exercise in understanding the basics of ASP.NET
Core backend development, it mirrors and replaces my first portfolio project,
which was a URL-shortener written in TypeScript using Express/Node.

My focus for this was to have some simple functionality, around and through which
I could experiment with a selection of ASP.NET Core packages and capabilities,
whilst demonstrating some basic syntactical proficiency.

The scope is deliberately more focused compared to the original. I wanted it to
be a little more streamlined; a little less scattered. It's still in progress,
and I may 'zhuzh' it up down the line.

This README was written entirely by myself.

## Tech Stack

- C# / .NET 10
- ASP.NET Core
- Entity Framework Core
- SQL Server
- Redis
- Docker Compose
- ASP.NET Core Rate Limiting
- ASP.NET Core Logging
- Background Worker & Job Queue

## Current Features

- **Link Shortening**: Produces clean and compact short links from long URLs.
- **Fast Redirects**: Redirect lookups are cached in Redis, whilst click-count
  updates are processed by a background worker, so they don't block the redirect
  response.
- **Rate Limiting**: A global concurrency limiter provides an upper bound on
  simultaneous requests for stability, and a per-IP fixed-window limiter
  protects link-creation from abuse.

## Planned Features

- **Click Count**: An endpoint that returns the click count for your link,
  so you can see how popular it is.
- **Playground**: A basic frontend where you can play around with the
  functionality and view some server performance stats.

## Installation

### Prerequisites

Before running the project, you'll need the following installed:

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker & Docker Compose](https://www.docker.com/products/docker-desktop/)

Verify the installations:

```bash
dotnet --version
docker --version
docker compose version
```

### Running The Application

Clone the repository:

```bash
git clone https://github.com/tcchilds-dev/csharp-url-shortener
cd csharp-url-shortener/UrlShortener.Api/
```

Start Redis and SQL Server:

```bash
docker compose up -d
```

Run the app:

```bash
dotnet run
```

### Stopping The Application

You can stop the app from the terminal with `Ctrl+C`.

To stop the docker services:

```bash
docker compose down
```

To remove the docker volumes and their data:

```bash
docker compose down -v
```

## Usage

### Endpoints

> [!NOTE] For now, the base URL should be `localhost:5071`

#### `POST <baseURL>/shorten`

Send a full URL and receive a shortened link.

Example Send:

```Bash
curl -X POST http://localhost:5071/shorten \
     -H "Content-Type: application/json" \
     -d '{"url": "https://www.example.com"}'
```

Example Receive:

```Bash
"eXpL123"
```

#### `GET <baseURL>/{code}`

Redirects a short link to the corresponding full address.

If you don't want to test without redirect:

```Bash
curl -L -o /dev/null -s -w \
     'status: %{http_code}\ntotal: %{time_total}s\n' \
      http://localhost:5071/<yourcode>
```

## Decisions & Rationale

_**Why short codes of length 7?**_

It provides a good balance between being compact and minimising chances of
collisions.

I have 62 characters to generate with.

The codes themselves have a unique constraint of course.

Total possible codes: $62^7\approx3.5\times10^{12}$

Birthday-collision scale is roughly $\sqrt{62^7}\approx1.88$ million actual
generated codes before collision likelihood stops being negligible. Add in
retries on top of that and we're chilling.

---

**How do you deal with short-code collisions?**

I use randomly generated codes with retries. Maximum attempts are set to 3.

Out of the three primary methods I know of:

- Random + retries
- Sequential + base62 encoding
- Pre-generated key pool

Random + retries is the simplest, and sufficient for our uses.

---

**What is the rationale behind the rate limiter settings?**

I've got a global concurrency limiter set at 1000 with zero queue.

This caps the number of requests being processed simultaneously. At a
conservative average of 10ms per request, 1000 concurrent requests
would map to a theoretical throughput of roughly 100,000 requests/sec.
This limit is intended as a stability ceiling and may change after load
testing.

I've also got a fixed window per-IP limit on creating links of 10 per minute.
Which may be a little generous. Nobody needs that many links surely. I might
dial that down.

---
