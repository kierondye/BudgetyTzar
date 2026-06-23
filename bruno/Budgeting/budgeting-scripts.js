function captureProjectionEventId(bru, res) {
  const eventId = ((typeof res.getHeader) === "function"
    ? res.getHeader("X-BudgetyTzar-Event-Id") || res.getHeader("x-budgetytzar-event-id")
    : getHeaderValue(res.headers, "x-budgetytzar-event-id"));

  bru.setVar("lastProjectionEventId", eventId);
}

function getHeaderValue(headers, name) {
  if (!headers) {
    return undefined;
  }

  const lowerName = name.toLowerCase();
  const headerName = Object.keys(headers).find((key) => key.toLowerCase() === lowerName);
  return headerName ? headers[headerName] : undefined;
}

async function waitForProjectionReady(bru, req) {
  const eventId = bru.getVar("lastProjectionEventId");
  const budgetId = bru.getVar("budgetId");
  const baseUrl = bru.getEnvVar("baseUrl") || bru.getVar("baseUrl");
  if (!eventId) {
    throw new Error("Missing lastProjectionEventId from the previous write response.");
  }
  if (!baseUrl) {
    throw new Error("Missing baseUrl environment variable.");
  }

  const snapshotUrl = bru.interpolate(req.getUrl());
  const snapshotSeparator = snapshotUrl.includes("?") ? "&" : "?";
  req.setUrl(`${snapshotUrl}${snapshotSeparator}waitForEventId=${eventId}`);

  const eventUrl = `${baseUrl.replace(/\/$/, "")}/api/budgets/${budgetId}/projection-events?eventId=${eventId}`;
  const eventResponse = await bru.sendRequest({ method: "GET", url: eventUrl, timeout: 10000 });
  const body = typeof eventResponse.body === "string"
    ? eventResponse.body
    : JSON.stringify(eventResponse.body || eventResponse.data || {});

  if (eventResponse.status === 404) {
    throw new Error(`Projection event ${eventId} is unknown for budget ${budgetId}.`);
  }
  if (eventResponse.status < 200 || eventResponse.status >= 300) {
    throw new Error(`Projection event stream returned HTTP ${eventResponse.status}.`);
  }
  if (!body.includes("event: projection-ready") || !body.includes(eventId)) {
    throw new Error(`Projection event ${eventId} did not produce a projection-ready SSE event.`);
  }
}

function assertSnapshotShape(res, expect) {
  expect(Object.keys(res.body).sort()).to.eql([
    "budgetId",
    "budgetItems",
    "date",
    "totalBalance",
    "totalBudgetedBalance",
    "totalTransactionBalance",
    "unbudgetedBalance"
  ].sort());
  res.body.budgetItems.forEach((item) => {
    expect(Object.keys(item).sort()).to.eql([
      "actualCredit",
      "actualDebit",
      "balance",
      "budgetItemId",
      "name",
      "plannedCredit",
      "plannedDebit"
    ].sort());
  });
}

function findSnapshotItem(res, budgetItemId) {
  return res.body.budgetItems.find((item) => item.budgetItemId === budgetItemId);
}

module.exports = {
  assertSnapshotShape,
  captureProjectionEventId,
  findSnapshotItem,
  waitForProjectionReady
};
