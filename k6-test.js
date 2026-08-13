import http from "k6/http";

export const options = {
  iterations: 10000,
  vus: 10000,
};

export default function () {
  http.get("http://localhost:5071/ETE6G5M/blank");
}
