## 🚀 Release v0.4 – GxPAuditTrailReview

**Release Date:** 2026-04-26

The concept of scanning audit trails using both deterministic, configurable rules and an AI-supported engine has proven successful. Audit trail scans can now be performed for each system, and every scan can be saved for future reference.

Scan results can now be reviewed and assessed. Support for audit trails from major software vendors has also been added, and it is now possible to upload audit trails in CSV, XML, and JSON formats.

---

### ✨ New Features

- Dashboard changes
	- Overview of scans to review and unreviewed findings
	- File preview to inspect the uploaded content
- Systems
	- Ability to assign a system type to each system
	- Ability to configure various system types
	- Ability to run a scan from the system level
- Scans
	- Added file type to the system overview
	- Ability to connect systems to scans coming from the Integration Hub
- Scan management
	- Scan results, AI analysis, review details, and signature information are placed in separate tabs
	- It is possible to close and sign a scan when all findings have been processed
	- It is possible to reopen a scan if the user has Key User or Admin roles
	- Signatures of audit trail reports are shown in the Reviews and Signed tabs
- Rule-based engine
	- Rule configuration has been redefined
	- Engine settings related to rule configuration have been added for more detailed setup
 - The audit trail engine complies with OATS (Open Audit Trail Standard)
- Logs
	- Added user login logs
	- Added audit trail logs
- Integration Hub
 - The first version of the Integration Hub Worker Service has been added
	- Ability to discover files on FTP, scan them, and add them to the GxPAuditTrailReview database
	- The Integration Hub uses the same engine code as GxPAuditTrailReview
- User management
	- It is possible to reset the password for a new or existing user by sending an email
	- Introduction of three roles: Admin, Super User, and User
- Deployment
	- The application seeds basic settings when starting with a new database

---

### 🔧 Improvements

- Refactored the codebase and separated it into dedicated projects  
- Updated all libraries to the latest versions
- UI design changes
   - Only the first word is capitalized in the menu

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
