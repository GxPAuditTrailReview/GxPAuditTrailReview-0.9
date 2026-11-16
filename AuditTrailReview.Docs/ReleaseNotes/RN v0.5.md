## 🚀 Release v0.5 – GxPAuditTrailReview

**Release Date:** 2026-06-31

The concept of scanning audit trails using both deterministic, configurable rules and an AI-supported engine has proven successful. Audit trail scans can now be performed for each system, and every scan can be saved for future reference.

Scan results can now be reviewed and assessed. Support for audit trails from major software vendors has also been added, and it is now possible to upload audit trails in CSV, XML, and JSON formats.

---

### ✨ New Features

- Console application
	- A multiplatform console application has been added to process and upload audit trail files through the Integration Hub API
- AuditTrailReview.IntegrationHub.API
	- API for integrating external systems with the GxPAuditTrailReview platform
- Testing
	- Adding unit and integration tests for the new features and components


---

### 🔧 Improvements

- Refactored the codebase and separated it into dedicated projects in IntegrationHub 
- Updated all libraries to the latest versions
- Cybersecurity improvements, including dependency updates and code refactoring to enhance security and maintainability

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
