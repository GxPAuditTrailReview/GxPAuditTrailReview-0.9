## 🚀 Release v0.3 – GxPAuditTrailReview

**Release Date:** 2026-03-27

The concept of scanning audit trails using both deterministic, configurable rules and an AI-supported engine has proven successful. Audit trail scans can now be performed per system, and each scan can be saved for future reference.

Scan results can be reviewed and assessed. Support for audit trails from major software vendors has been added, and it is now possible to upload audit trails in CSV, XML, and JSON formats.

---

### ✨ New Features

- Sorting and filtering of tables for easier data searching  
- Direct redirection to the scan results page after a scan  
- Scan results and AI analysis are separated into different tabs  
- Ability to indicate a scan review decision with options such as:
  - Reviewed / False Positive  
  - Investigation Required  
  - Deviation  
  - Add review comments  
- Audit trail scanning now supports CSV, XML, and JSON formats  
- The file upload page has been redesigned
- Rule based review Eengine was extended to 21 options 

---

### 🔧 Improvements

- Refactored the codebase and separated it into dedicated projects  
- Updated all libraries to the latest versions  

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
