## 🚀 Release v0.6 – GxPAuditTrailReview

**Release Date:** 2026-08-31

The concept of scanning audit trails using both deterministic, configurable rules and an AI-supported engine has proven successful. Audit trail scans can now be performed for each system, and every scan can be saved for future reference.

Scan results can now be reviewed and assessed. Support for audit trails from major software vendors has also been added, and it is now possible to upload audit trails in CSV, XML, and JSON formats.

---

### ✨ New Features

- Adding paging to log and scan pages for better performance and usability
- Decouple the AI analysis call from the core scan-save transaction
- Persist the Worker Service's "already-processed" file tracking
- Enforce close-and-sign server-side.
- Add rate limiting to the Integration Hub API, which today has no limit beyond a shared static key

---

### 🔧 Improvements

- removing prompt for diffrent class
- API keys moved to configutation file for better security and maintainability

---

### 🐞 Bug Fixes

- Fixed bugs identified during development and testing  

---

### ⚠️ Known Issues

- N/A  

---

### 📦 Third-Party Licenses

```bash
dotnet list package --include-transitive
