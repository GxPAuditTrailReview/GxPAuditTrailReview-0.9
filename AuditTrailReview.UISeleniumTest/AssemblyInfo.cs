// This project runs every test through a real Chrome/ChromeDriver instance against a single,
// shared AuditTrailReview.WebApp process that WebAppFixture starts once for the whole test run
// (see WebAppFixture.cs's [AssemblyInitialize]) -- there is one running app and one underlying
// (Local)DB behind every test, not one per test.
//
// Correction (Path to v1.0.md's Thirty-eighth follow-up): this attribute was originally added
// on the assumption that the unified `MSTest` package runs test CLASSES in parallel by default.
// That assumption was wrong -- per Microsoft's own docs (MSTEST0001), MSTest runs sequentially
// unless an assembly opts in with [assembly: Parallelize], which this project never had. So
// adding [assembly: DoNotParallelize] here did not change how the suite ran; it was a no-op, and
// the WebDriverException / 60-second-timeout / "Driver service did not exit within 10 seconds.
// Forcing termination" symptom it was meant to fix (seen on TC-002, TC-008, and TC-009, none of
// which touch anything specific to the new tests) kept recurring afterward for exactly that
// reason. The attribute is kept -- it's still correct and it's the explicit, analyzer-recommended
// choice (MSTEST0001 wants every assembly to declare one way or the other) -- but it should not
// be read as the fix for that symptom. The actual investigation moved to WebAppFixture.cs's
// AssemblyInitialize, which now fails fast if a stale WebApp process from a previous, uncleanly-
// stopped run is still bound to Config.BaseUrl when a new run starts.
[assembly: DoNotParallelize]
