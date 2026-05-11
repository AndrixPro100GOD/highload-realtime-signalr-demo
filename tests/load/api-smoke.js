import http from "k6/http";
import { check, sleep } from "k6";

const baseUrl = (__ENV.BASE_URL || "http://localhost:8080").replace(/\/$/, "");

export const options = {
  vus: Number(__ENV.VUS || 50),
  duration: __ENV.DURATION || "30s",
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<250"],
  },
};

export default function () {
  const ping = http.get(`${baseUrl}/api/ping`);
  const info = http.get(`${baseUrl}/api/performance/info`);

  check(ping, {
    "api ping status is 200": (res) => res.status === 200,
  });

  check(info, {
    "api info status is 200": (res) => res.status === 200,
  });

  sleep(1);
}
