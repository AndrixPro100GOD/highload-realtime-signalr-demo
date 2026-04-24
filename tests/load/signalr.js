import http from "k6/http";
import ws from "k6/ws";
import { check, sleep } from "k6";

const baseUrl = (__ENV.BASE_URL || "http://localhost:8080").replace(/\/$/, "");
const hubPath = __ENV.HUB_PATH || "/hubs/realtime";
const groupName = __ENV.GROUP_NAME || "benchmark";
const sendIntervalMs = Number(__ENV.SEND_INTERVAL_MS || 1000);
const payloadSize = Number(__ENV.PAYLOAD_BYTES || 128);
const stages = [
  { duration: __ENV.RAMP_UP || "30s", target: Number(__ENV.VUS || 200) },
  { duration: __ENV.STEADY || "60s", target: Number(__ENV.VUS || 200) },
  { duration: __ENV.RAMP_DOWN || "10s", target: 0 },
];

export const options = {
  stages,
  thresholds: {
    http_req_failed: ["rate<0.01"],
  },
};

function recordSeparator(payload) {
  return `${payload}\u001e`;
}

function buildPayload(sequence) {
  const body = "x".repeat(Math.max(payloadSize, 16));
  return {
    senderId: `k6-${__VU}`,
    groupName,
    payload: body,
    sequenceNumber: sequence,
    sentAtUtc: new Date().toISOString(),
  };
}

function negotiate() {
  const response = http.post(
    `${baseUrl}${hubPath}/negotiate?negotiateVersion=1`,
    null,
    {
      headers: {
        "Content-Type": "application/json",
      },
    }
  );

  check(response, {
    "negotiate status is 200": (res) => res.status === 200,
  });

  const payload = response.json();
  return payload.connectionToken || payload.connectionId;
}

export default function () {
  const token = negotiate();
  const wsUrl = baseUrl.replace("http://", "ws://").replace("https://", "wss://");
  const url = `${wsUrl}${hubPath}?id=${encodeURIComponent(token)}`;

  const response = ws.connect(url, {}, function (socket) {
    let sequence = 0;

    socket.on("open", () => {
      socket.send(recordSeparator(JSON.stringify({ protocol: "json", version: 1 })));
      socket.send(recordSeparator(JSON.stringify({
        type: 1,
        target: "JoinGroup",
        arguments: [groupName],
      })));
    });

    socket.on("message", () => {
      // k6-скрипт в этом демо фиксирует факт жизнеспособного round-trip,
      // а точную SignalR latency удобнее мерить NBomber-сценарием.
    });

    socket.setInterval(() => {
      sequence += 1;
      socket.send(recordSeparator(JSON.stringify({
        type: 1,
        target: sequence % 4 === 0 ? "QueueGroupMessage" : "SendToGroup",
        arguments: [buildPayload(sequence)],
      })));
    }, sendIntervalMs);

    socket.setTimeout(() => {
      socket.close();
    }, sendIntervalMs * 10);
  });

  check(response, {
    "ws upgrade status is 101": (res) => res && res.status === 101,
  });

  sleep(1);
}
