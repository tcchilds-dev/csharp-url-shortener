import http from "k6/http";

export const options = {
  scenarios: {
    contacts: {
      executor: "constant-arrival-rate",
      duration: "30s",
      rate: 10000,
      timeUnit: "1s",
      preAllocatedVUs: 10,
      maxVUs: 20,
    },
  },
};

export default function () {
  http.get("http://localhost:5071/ETE6G5M/blank");
}
