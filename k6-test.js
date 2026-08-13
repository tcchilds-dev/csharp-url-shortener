import http from "k6/http";

export const options = {
  scenarios: {
    contacts: {
      executor: "constant-arrival-rate",
      duration: "30s",
      rate: 100000,
      timeUnit: "1s",
      preAllocatedVUs: 100,
      maxVUs: 300,
    },
  },
};

export default function () {
  http.get("http://localhost:5071/ETE6G5M/blank");
}
