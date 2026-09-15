import http from "k6/http";
import { check } from "k6";
import { SharedArray } from "k6/data";
import exec from "k6/execution";
import { Counter } from "k6/metrics";
import { textSummary } from "https://jslib.k6.io/k6-summary/0.0.2/index.js";

const baseUrl = (__ENV.BASE_URL || "http://localhost:5071").replace(/\/$/, "");
const scenario = __ENV.SCENARIO || "hot";
if (!["hot", "mixed", "distinct"].includes(scenario)) {
  throw new Error("SCENARIO must be hot, mixed, or distinct");
}

let metadata;
const codes = new SharedArray("test links", () => {
  const data = JSON.parse(open(__ENV.DATA_FILE || "./load-test-data.json"));
  metadata = { runId: data.runId, targetUrl: data.targetUrl };
  return data.codes;
});
if (!metadata) {
  const data = JSON.parse(open(__ENV.DATA_FILE || "./load-test-data.json"));
  metadata = { runId: data.runId, targetUrl: data.targetUrl };
}
if (codes.length < 1010) throw new Error("Run the data utility to prepare at least 1010 links");

function positiveInteger(name, fallback) {
  const value = Number(__ENV[name] || fallback);
  if (!Number.isSafeInteger(value) || value <= 0)
    throw new Error(`${name} must be a positive integer`);
  return value;
}
const preAllocatedVUs = positiveInteger("PREALLOCATED_VUS", 20);
// Allocate the default pool before the run instead of creating extra VUs under load.
const maxVUs = positiveInteger("MAX_VUS", preAllocatedVUs);
if (maxVUs < preAllocatedVUs) throw new Error("MAX_VUS must be at least PREALLOCATED_VUS");
const successfulRedirects = new Counter("successful_redirects");

export const options = {
  scenarios: {
    [scenario]: {
      executor: "constant-arrival-rate",
      duration: __ENV.DURATION || "30s",
      rate: positiveInteger("RATE", 100),
      timeUnit: "1s",
      preAllocatedVUs,
      maxVUs,
    },
  },
  thresholds: {
    checks: ["rate==1"],
    successful_redirects: ["count>0"],
    "http_req_failed{endpoint:redirect}": ["rate<0.01"],
    "http_req_duration{endpoint:redirect}": [`p(95)<${positiveInteger("P95_MS", 100)}`],
    dropped_iterations: ["count==0"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
};

export default function () {
  let index = 0;
  if (scenario === "mixed") {
    index =
      Math.random() < 0.8 ? Math.floor(Math.random() * 10) : 10 + Math.floor(Math.random() * 1000);
  } else if (scenario === "distinct") {
    index = exec.scenario.iterationInTest % codes.length;
  }
  const response = http.get(`${baseUrl}/${codes[index]}`, {
    redirects: 0,
    tags: { endpoint: "redirect", name: "GET /{code}" },
  });
  const success = check(response, {
    "returns 302": (result) => result.status === 302,
    "returns the expected destination": (result) => result.headers.Location === metadata.targetUrl,
  });
  successfulRedirects.add(success ? 1 : 0);
}

export function handleSummary(data) {
  const report = {
    runId: metadata.runId,
    scenario,
    successfulRedirects: data.metrics.successful_redirects?.values.count || 0,
    metrics: data.metrics,
  };
  return {
    [__ENV.SUMMARY_FILE || "k6-summary.json"]: JSON.stringify(report, null, 2),
    stdout:
      textSummary(data, {
        indent: "  ",
        enableColors: !data.options.noColor && data.state.isStdOutTTY,
      }) + "\n\nRun the data utility's verify command to check persisted clicks.\n",
  };
}
