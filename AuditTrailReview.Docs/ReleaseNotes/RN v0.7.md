## 🚀 Release v0.7 – GxPAuditTrailReview

**Release Date:** 2026-09-10

The concept of scanning audit trails using both deterministic, configurable rules and an AI-supported engine has proven successful. Audit trail scans can now be performed for each system, and every scan can be saved for future reference.

Scan results can now be reviewed and assessed. Support for audit trails from major software vendors has also been added, and it is now possible to upload audit trails in CSV, XML, and JSON formats.

---

### ✨ New Features

- Set an explicit password policy and account lockout
- Close the role-enforcement gap
- Make the AI-analysis step configurable/optional
- Console app with new API endpoints for configuring options for scanning audit trails
- AI analysis should extend to Integration Hub-ingested scans
- Sending emails after creation of the user, not only manually by admin
- Rule-Based Review Engine - major adjustments, adding ti rules binding regulatory clause and  internationally harmonized guidance
- share the scan link fron scan level
- improve UI and UX across application
- Application settings

---

### 🔧 Improvements

- Close the role-enforcement gap
- Moving Unit tests to write locations
- Add automated test coverage for the WebApp Razor Page handlers.
- Remove unused package references

---

### 🐞 Bug Fixes

- Fixed bugs identified during development and testing  


---

### ⚠️ Known Issues

- N/A  

---

### 📦 Third-Party Licenses

This project is licensed under the Business Source License 1.1 (see `LICENSE` at the repo
root). A full list of third-party NuGet packages and their licenses has not yet been generated
for this release -- run the command below from the repo root (with a working `dotnet` toolchain)
against each project, or `dotnet restore` at the solution level first, and record the result
here before or shortly after publishing:

```bash
dotnet list package --include-transitive
```
