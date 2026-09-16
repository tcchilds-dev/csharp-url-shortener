// This frontend was coded by AI.

import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import "./style.css";

const apiOrigin = "http://localhost:5071";
const shellQuote = (value) => `'${value.replaceAll("'", "'\\''")}'`;

function LinkSection({
  title,
  description,
  label,
  placeholder,
  value,
  onChange,
  path,
  body,
  onSuccess,
  button,
}) {
  const [output, setOutput] = useState(null);
  const [pending, setPending] = useState(false);
  const [submittedCommand, setSubmittedCommand] = useState(null);
  const command =
    body === undefined
      ? `curl -i ${shellQuote(`${apiOrigin}${path}`)}`
      : `curl -i ${shellQuote(`${apiOrigin}${path}`)} \\\n  -H 'Content-Type: application/json' \\\n  -d ${shellQuote(JSON.stringify(body))}`;

  async function submit(event) {
    event.preventDefault();
    if (pending) return;
    setPending(true);
    setOutput(null);
    setSubmittedCommand(command);

    try {
      const response = await fetch(`/api${path}`, {
        method: body === undefined ? "GET" : "POST",
        ...(body === undefined
          ? {}
          : {
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify(body),
            }),
        signal: AbortSignal.timeout(15000),
      });
      const text = await response.text();
      const headers = [...response.headers].map(([key, value]) => `${key}: ${value}`).join("\n");
      setOutput(`HTTP ${response.status} ${response.statusText}\n${headers}\n\n${text}`);
      if (response.ok && onSuccess) onSuccess(text);
    } catch (error) {
      setOutput(
        error.name === "TimeoutError"
          ? "Request timed out. Check that the API and databases are running."
          : "Could not reach the API. Check that it is running on http://localhost:5071.",
      );
    } finally {
      setPending(false);
    }
  }

  const id = title === "Create a Link" ? "create-link" : "use-link";

  return (
    <section aria-labelledby={`${id}-heading`} className="space-y-4">
      <div className="space-y-1">
        <h2 id={`${id}-heading`} className="font-semibold">
          {title}
        </h2>
        <p className="text-base-content/75">{description}</p>
      </div>
      <div className="mockup-code w-full rounded-xl bg-base-200 text-base-content">
        <pre data-prefix="$">
          <code>{command}</code>
        </pre>
        <div className="terminal-response" role="status" aria-live="polite" aria-busy={pending}>
          <pre>
            <code>
              {pending
                ? "Sending request…"
                : ((submittedCommand === command ? output : null) ??
                  "Submit a request to see the response here.")}
            </code>
          </pre>
        </div>
      </div>
      <form onSubmit={submit} className="space-y-2">
        <label htmlFor={id} className="block">
          {label}
        </label>
        <div className="flex flex-col gap-3 sm:flex-row">
          <input
            id={id}
            className="input input-bordered w-full min-w-0 sm:flex-1"
            type="text"
            autoCapitalize="none"
            spellCheck={false}
            autoComplete="off"
            placeholder={placeholder}
            value={value}
            disabled={pending}
            required
            onChange={(event) => {
              onChange(event.target.value);
              setOutput(null);
            }}
          />
          <button
            className="btn btn-neutral sm:min-w-36"
            type="submit"
            disabled={pending || !value.trim()}
          >
            {pending ? "Sending…" : button}
          </button>
        </div>
      </form>
    </section>
  );
}

function App() {
  const [url, setUrl] = useState("");
  const [code, setCode] = useState("");

  return (
    <main className="mx-auto max-w-3xl space-y-10 px-5 py-10 sm:px-8 sm:py-14">
      <header className="border-b border-base-300 pb-5">
        <h1 className="font-semibold">URL Shortener Playground</h1>
      </header>
      <LinkSection
        title="Create a Link"
        description="Turn a URL into a short link."
        label="Destination URL"
        placeholder="https://example.com"
        value={url}
        onChange={setUrl}
        path="/shorten"
        body={{ url }}
        button="Create link"
        onSuccess={(text) => {
          try {
            const createdCode = JSON.parse(text);
            if (typeof createdCode === "string") setCode(createdCode);
          } catch {
            /* Keep the raw response visible if it is not JSON. */
          }
        }}
      />
      <div className="border-t border-base-300" />
      <LinkSection
        title="Use a Link"
        description="Check cache status and lookup timing. Each request records a click."
        label="Short code"
        placeholder="Ab3dE7f"
        value={code}
        onChange={setCode}
        path={`/${encodeURIComponent(code)}/blank`}
        button="Use link"
      />
    </main>
  );
}

createRoot(document.getElementById("root")).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
