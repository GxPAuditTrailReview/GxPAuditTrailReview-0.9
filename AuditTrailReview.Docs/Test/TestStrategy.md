# Test Strategy

## Purpose and scope
This document describes how GxPAuditTrailReview is tested: the automated layers (unit tests,
Selenium and Playwright UI tests) and the manual, ad hoc large-scale performance testing that sits alongside them.

It also records the performance test results that have driven real product decision.

---

## 1. Testing layers

| Layer | Project | What it covers |
|---|---|---|
| Unit tests | `AuditTrailReview.UnitTest` | Correctness of individual components in isolation.
| UI / Selenium tests | `AuditTrailReview.UISeleniumTest` | End-to-end browser-driven scenarios.
| UI / Playwright tests | `AuditTrailReview.UIPlaywrightTest` | Smoke and menu-crawl scenarios (TC-UI-001 to TC-UI-008) against an already running environment (`ATR_PLAYWRIGHT_*` settings). Run on demand by the manual Azure DevOps pipeline `pipelines/pipeline-ui-tests-external-env.yml`; not part of CI. See `CICD/Azure Pipelines.md`. |
| Manual performance / load testing | *(no dedicated project; run ad hoc against the pipeline)* | Large-scale runs (tens of thousands to millions of records) to measure actual throughput.


---