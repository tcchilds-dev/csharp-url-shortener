import http from "k6/http";
import { check, fail } from "k6";

const baseUrl = (__ENV.BASE_URL || "http://localhost:5071").replace(/\/$/, "");
const targetUrl = "https://example.com/";

export const options = {
  scenarios: {
    redirects: {
      executor: "constant-arrival-rate",
      duration: __ENV.DURATION || "30s",
      rate: Number(__ENV.RATE || 100),
      timeUnit: "1s",
      preAllocatedVUs: Number(__ENV.PREALLOCATED_VUS || 20),
      maxVUs: Number(__ENV.MAX_VUS || 100),
    },
  },
  thresholds: {
    checks: ["rate==1"],
    "http_req_failed{endpoint:redirect}": ["rate<0.01"],
    "http_req_duration{endpoint:redirect}": [`p(95)<${__ENV.P95_MS || 100}`],
    dropped_iterations: ["count==0"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
};

export function setup() {
  const response = http.post(`${baseUrl}/shorten`, JSON.stringify({ url: targetUrl }), {
    headers: { "Content-Type": "application/json" },
    redirects: 0,
  });

  if (response.status !== 201) {
    fail(`Could not create test link: HTTP ${response.status} ${response.body}`);
  }

  return { code: response.json() };
}

export default function (data) {
  const response = http.get(`${baseUrl}/${data.code}`, {
    redirects: 0,
    tags: { endpoint: "redirect" },
  });

  check(response, {
    "returns 302": (result) => result.status === 302,
    "returns the expected destination": (result) => result.headers.Location === targetUrl,
  });
}
