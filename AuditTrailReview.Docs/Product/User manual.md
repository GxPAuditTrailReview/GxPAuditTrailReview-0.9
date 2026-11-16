# User Manual: GxPAuditTrailReview v0.9

## Who this manual is for
This manual explains how to use GxPAuditTrailReview day to day: signing in, managing systems, uploading and reviewing audit trail scans, signing off, and using the administration and configuration screens. It's written for everyone who uses the application, including reviewers, system owners, and administrators, not just developers.

---

## 1. Getting Started

### Signing in
Open the application URL in your browser and sign in with the email and password your administrator gave you. If you're setting up the application for the first time, use the account configured as the `SeedAccount` during installation; it's created automatically with the **Admin** role the first time the app starts.

After signing in you land on the **Dashboard**, which shows:
- Total scans, scans still needing review, total systems, and unreviewed findings.
- Your 5 most recently created scans.
- Quick links to add a system, start a scan, preview a file, and review all scans.

> **Tip:** most list pages (the Dashboard's latest-scans table, **Scans → All Scans**, a scan's **Scan Results** and **Potential Findings** tabs, and the **Logs** pages) let you sort by clicking a column header, filter with a free-text search box, and page through results instead of scrolling one long table.

### User roles
The application has three roles: **Admin**, **Super User**, and **User**.

| Role | Intended for |
|---|---|
| **Admin** | Full platform administration: everything in Admin Panel, plus everything Super User and User can do. |
| **Super User** | Configuring the rule engine and system types, plus everything User can do. |
| **User** | Day-to-day operational work: uploading files, reviewing findings, signing scans, managing systems. |

> **What each role can reach today:** **Admin Panel is Admin-only, full stop** -- user and role administration, Application Settings (General, Email, AI parameters, and the Admin-only security/password/lockout policy), AI Analysis, and Docs chatbot are all reachable by Admin alone; Super User cannot open any of them, from the menu or by URL. **Config Panel's remaining two pages -- Rule-based review engine and System type --** require Admin or Super User, and so do the two **Logs** pages (User login log and Audit trail log); a plain User sees no Logs menu and is sent to the Access denied page if they open a log URL. Most day-to-day work (uploading files, reviewing findings, closing/signing a scan, and creating/editing systems) is deliberately open to any signed-in user, whichever of the three roles they hold -- Super User has no extra review/sign privilege over User there. **Reopening or deleting a scan, and deleting a system, are the exceptions:** all three require Admin or Super User. If your organization needs stricter separation of duties for the remaining operational actions, enforce it through your own SOPs and account provisioning.

### Permissions matrix

The table below lists every action in the application and which roles can perform it, based on the actual access checks in the code (a page with no role restriction is reachable by any signed-in user, regardless of role).

| Action / page | Admin | Super User | User |
|---|:---:|:---:|:---:|
| Sign in; Dashboard; Help; Ask the docs (chat) | Yes | Yes | Yes |
| View notifications | Yes | Yes | Yes |
| **Logs:** User login log; Audit trail log | Yes | Yes | No |
| **Systems:** create, edit, view | Yes | Yes | Yes |
| **Delete a system** | Yes | Yes | No |
| **Scans:** upload, preview, view | Yes | Yes | Yes |
| Review findings; assign a system to a scan | Yes | Yes | Yes |
| Sign & close a scan | Yes | Yes | Yes |
| **Reopen a signed scan** | Yes | Yes | No |
| **Delete a scan** (only available before it's signed) | Yes | Yes | No |
| Download a scan's source file; export results to Excel | Yes | Yes | Yes |
| **Config Panel → Rule-based review engine** (toggle/configure the 19 rules) | Yes | Yes | No |
| **Config Panel → System type** (create, edit, delete, view) | Yes | Yes | No |
| **Admin Panel → Users** (create, edit, delete, view accounts; assign roles) | Yes | No | No |
| **Admin Panel → Roles** (create, edit, delete, view roles) | Yes | No | No |
| **Admin Panel → Application settings** -- General, Email, AI parameters, and Security policy | Yes | No | No |
| **Admin Panel → AI Analysis** (enable/disable AI-assisted review) | Yes | No | No |
| **Admin Panel → Docs chatbot** (reindex the documentation search index) | Yes | No | No |

Note: a **User** account simply won't see the **Reopen review** button on a signed scan, or the delete (🗑) icon next to an unsigned scan or a system in the Systems list -- a direct attempt to call any of these is also rejected by the server. Nothing in the application currently restricts sign-and-close of a scan, or system creation/editing, to a specific role -- if your organization's SOPs call for stricter segregation of duties on those remaining actions, enforce it procedurally (account provisioning, review of the audit trail log) rather than relying on the application to block it. The **Admin panel** menu itself is visible to Admin only -- a Super User sees no Admin Panel menu at all, only the **Config panel** menu with its two remaining items.

### Getting help
The **Help** item in the main menu opens a page with four things:
- **Try it now**: sample audit trail files (CSV, XML and JSON, each with the same 37 example records) built so that every one of the 19 review rules fires at least once. Upload one through **Scans → Upload** to see a full scan and the review workflow without using real data.
- **User manual**: this manual, shown in the application.
- **Video tutorials**: training walkthroughs from both the user and technical point of view, hosted on the [GxPAuditTrailReview YouTube channel](https://www.youtube.com/@GXPAuditTrailReview).
- **Ask the docs**: the chat page described below.

**Ask the docs** is a chat page, reachable from Help, where you can ask a question in plain language (for example "how do I reopen a signed scan?") and get an answer based only on the product's own documentation (Installation Manual, Technical Manual, TechStack overview, this User manual, the Audit-Trail-Rule-Engine reference, and the PRD), with the documents it used listed as sources. If the documentation does not cover your question, it says so. Things to know:
- It needs an AI service key to be configured for the installation, and your question is sent to that AI service.
- An administrator has to build its search index first (**Admin panel → Docs chatbot → Reindex now**, Admin only), and again after the documentation changes. Until then the chat tells you the index has not been built.
- Answers are generated text. Treat them as a convenient pointer to the documentation, not as validated output; check the cited source when it matters.

---

## 2. Managing Systems

A **system** represents the source application whose audit trail you review (e.g. a LIMS, MES, or QMS instance).

### Create a system
1. Go to **Systems → Create** (or use the Dashboard's "Add system" quick link).
2. Enter a system ID/prefix and a system name.
3. Optionally choose a **system type** (see §7 for managing the type list; LIMS, MES, CDS, ERP, QMS, SCADA, DMS, and ELN are available by default).
4. Save.

### View and edit a system
Open a system from the **Systems** list to see its details, its assigned type, and every scan linked to it (most recent first). Use **Edit** to update its name, ID, or type, and **Delete** to remove it -- the delete icon requires an **Admin** or **Super User** account; a **User** account does not see it, and the server rejects the request even if it's called directly. Every create, edit, and delete of a system is written to the internal **Audit Trail Log** (see §11) so changes stay traceable.

### Start a scan from a system
From a system's details page, use the **Scan** action to open the upload page with that system already selected, so the resulting scan is linked to it automatically.

---

## 3. Uploading and Scanning an Audit Trail File

### Supported file types
`.csv`, `.xml`, and `.json`. The file picker only offers these three extensions.

### Run a scan
1. Go to **Scans → Upload** (or start from a system, per §2).
2. Choose your file and submit.
3. The application reads the file, automatically detects the CSV delimiter (comma or semicolon) if applicable, and maps the columns into a common set of fields (timestamp, user, operation, old/new value, reason, etc.) using built-in header-name recognition that covers common vendor terminology.
4. The 19 built-in review rules are evaluated against every row (see §8 for the full list and how to configure them).
5. Results are saved as a new scan. For a file up to about 10 MB this happens while you wait and you're taken to the scan's details page; for a larger file the scan runs in the background (see "Large files" below).
6. If AI analysis is switched on for your installation and an AI-service key is configured, the saved results are also sent for a supplementary AI review in the background (see §10). A missing or invalid key does **not** stop your scan from being created; the AI Analysis tab is simply empty.

### Large files
The maximum upload size is 750 MB. Up to about 10 MB the scan runs while you wait. Larger files are queued and scanned in the background so your browser is not tied up for minutes:
- **Roughly 10 MB to 50 MB:** the web application scans the file in the background. The file is held in memory only, so if the web application restarts while it's still running, that scan is marked **Failed** and you need to upload it again.
- **Above 50 MB (up to the 750 MB upload limit):** the file is stored and scanned by the Integration Hub **Worker Service**, one file at a time. If other large files are ahead of yours, yours waits its turn. If the Worker Service is not running, the scan stays queued; tell your administrator.

While this happens you can keep working. When a background scan finishes (or fails) a **bell icon** in the top menu shows a count; open it to see which file finished, then click it to open the new scan, or dismiss it ("Mark all read" clears the list). The bell refreshes about every 30 seconds. A failed scan shows the reason; ask your administrator if it is not obvious.

Times shown for scan results use the working-day time zone configured by your administrator (**Config panel → Rule-based review engine → Engine Settings**), not raw UTC.

### Preview a file without creating a scan
Use **Scans → Preview** to inspect a file's structure and column mapping before committing to a full scan. This is a lightweight check only; it does not run the full rule engine, and nothing is saved to the database. You can export the previewed data to Excel from this page.

---

## 4. Reviewing Scan Findings

Open any scan from the **Scans** list or the Dashboard to reach its details page. Near the top, the original file name is a download link back to the exact bytes the scan was created from, unless the scan predates this feature, in which case it's shown as plain text with "(original file not available)". A collapsible **Engine Settings** panel shows the working-hours/weekend and minimum-username-length thresholds that were actually in effect when this scan was processed, which can differ from today's configuration if an administrator has changed it since.

The details page itself has four tabs:

- **Scan results**: every record in the file, exactly as parsed, whether or not it triggered a rule.
- **⚠ Potential findings**: the same records, pre-filtered to just the ones needing your attention: anything carrying a persisted **Finding** from the rule engine, plus a small number of checks (for example rule D2's minimum-username-length check) that are instead re-evaluated live against the *current* Engine Settings, so an old scan still reflects today's threshold even though nothing was re-saved.
- **AI analysis**: see §10.
- **✅ Reviewed & signed**: the scan's sign/reopen history (see §5).

Both **Scan results** and **Potential findings** are independently searchable and paginated. Each flagged row's **Finding** is prefixed with the rule code that caught it (e.g. `[A1] Missing mandatory fields: Timestamp`); a live D2 check, if any, is shown underneath it. From either tab, for each flagged row:

1. Choose a review outcome:
   - **False Positive**: the rule fired but there's no real issue.
   - **Investigation Required**: needs follow-up before you're confident in the outcome.
   - **Deviation**: a genuine issue that should be tracked.
2. Add an optional comment explaining your decision.
3. Save. The system records your username and a timestamp automatically.

Findings without a review are shown as unreviewed until you act on them. Once the scan is closed and signed, the review controls disappear and both tabs become read-only.

### Reviewing findings in bulk

When a scan has many findings that call for the same decision (for example hundreds of `[E3]` findings caused by one service account), you can review them together. The **Potential findings** tab has two sub-tabs: **Potential findings** (the list of findings described above) and **Bulk review**, whose badge shows how many findings are still unreviewed. When many findings are unreviewed, the list also offers an **Open Bulk review** shortcut. The **Bulk review** sub-tab is not shown once the scan is closed and signed.

On the **Bulk review** sub-tab:

- **By rule** lists every rule that still has unreviewed findings in this scan, with how many there are and how many of them have that rule as the row's **only** finding. Click **Review all…** next to a rule.
- **By search** (next to **By rule** on a wide screen) lets you search the findings, for example by user ID, and shows how many match. It uses the same search as the Potential findings list, so you can click **Check the matches in the list**, then come back and click **Review all unreviewed…**.

In the pop-up:

1. Check the selection and the number of findings it covers. For a rule, **Only rows where this rule is the only finding** is ticked by default: a row that also has a finding from another rule is left out, so your decision on one rule doesn't quietly cover a different finding. Untick it to include those rows as well.
2. Choose the review outcome and enter a comment. The comment is **required** for a bulk review.
3. Tick the confirmation and click **Apply to N findings**.

A bulk review only changes findings that have **no review yet**; decisions already recorded are never overwritten. If the number of matching findings changes before you apply (for example because a colleague reviewed some of them), nothing is saved and you are asked to open the bulk review again. One bulk review can change at most 10,000 findings; narrow the selection with a search if there are more. Each changed finding gets its own entry in the scan's audit trail, with a reason naming the bulk selection, plus one summary entry for the whole action.

---

## 5. Closing and Signing a Scan

Once you're done reviewing, use **Close review & sign** on the scan's details page.

- The button is disabled while any finding on the scan still has no review outcome; review everything first.
- This is also enforced on the server, not just by the disabled button: a request to close a scan with unreviewed findings is rejected with an error, so there's no way around it from the interface.
- Clicking the button opens a signing pop-up. Your **Login** is already filled in with the account you are signed in with and can't be changed: you can only sign as yourself. Enter your **Password** and click **Sign and close**. Re-entering your password is what makes this an electronic signature.
- If the password is wrong, nothing is signed and the pop-up opens again with an error. Wrong passwords here count toward account lockout exactly like failed logins: after too many (5 by default) your account is deactivated, you are signed out, and an administrator has to reactivate it. Each attempt, failed or successful, appears in **Logs → User login**.
- Signing records who signed, their role(s), and when, on the **Reviewed & signed** tab, and marks the scan **Reviewed & Signed**.

### Reopening a scan
A signed scan can be reopened from its details page using **Reopen review** -- visible, and usable, only to an **Admin** or **Super User** account; a **User** account does not see the button, and the server rejects the request even if it's called directly. Reopening is itself a signature, so it opens the same signing pop-up as closing: your login is prefilled, you enter your password and click **Sign and reopen**, and the same lockout rule applies to wrong passwords. This returns the scan to the active review state and adds a "Reopen review" entry to the **Reviewed & signed** tab, so the scan's full sign/reopen history stays visible. Use this only when there's a legitimate reason to revisit a closed scan; your organization's SOPs should still define when reopening is appropriate.

---

## 6. Working with the Scan List

**Scans → All Scans** shows every scan with its system, date, file type, and sign-off status.

### Assign or reassign a system
If a scan wasn't linked to a system at upload time (or was linked to the wrong one), use the **Assign system** action (available both from the scan list and from a scan's details page) to pick the correct system. Assigning a system from the scan list also records you as the scan's user (the same user shown for scans you upload yourself), replacing any user previously recorded on that scan.

### Export results
From a scan's details page, use **Export to Excel** to download a workbook with a "Scan Results" sheet (all rows, findings, and review decisions) and an "AI Analysis" sheet (insights and recommendations, if AI analysis ran for that scan).

### Deleting a scan
A scan that hasn't been signed yet can be permanently deleted -- along with its scan results and any AI analysis -- using the delete (🗑) icon in the scan list or on a system's details page. Unlike reopening, this cannot be undone. It requires an **Admin** or **Super User** account; a **User** account does not see the icon, and the server rejects the request even if it's called directly. A signed/closed scan cannot be deleted at all, by any role -- reopen it first if it genuinely needs to go.

---

## 7. Managing System Types

**Config panel → System type** lets you create, edit, view, and delete the classification types available when creating or editing a system (LIMS, MES, CDS, ERP, QMS, SCADA, DMS, ELN by default). Add your own types here if your organization uses different classifications.

---

## 8. Configuring the Rule-Based Review Engine

**Config panel → Rule-based review engine** has two tabs:

### Rules
Enable or disable any of the 19 rules, grouped into 5 categories:

| Category | Covers |
|---|---|
| **A: Data Integrity** | Mandatory fields, record-chain continuity, duplicate records. |
| **B: Action Consistency** | Create/Update/Delete records having the right combination of old value, new value, and reason; the same person not both making and approving/reviewing a change. |
| **C: Temporal Checks** | Chronological order, no future-dated entries, changes outside normal working hours or on weekends. |
| **D: User Validation** | Minimum username length, usernames matching a known shared/generic/system account name or naming convention. |
| **E: Anomaly Detection** | Bursts of 10+ changes within one second; a value that keeps flipping back and forth between the same two states; a real value wiped out to blank or zero. |

Each rule also shows what backs it, in one of three states:

- A **grey badge** is a **binding regulatory clause** the rule directly enforces, from 21 CFR Part 11, Part 211
  (cGMP), Part 58 (GLP), Part 312 / Part 812 (GCP and investigational devices), or EudraLex Vol. 4 Annex 11.
  These are context-specific (211.x binds GMP finished pharmaceuticals, 58.130(e) nonclinical (GLP) studies,
  Annex 11 EU GMP computerised systems), so read each against the framework governing the system you are auditing.
- A **light badge under "Best practice"** means no binding clause requires the check, but internationally
  harmonized guidance expects it: PIC/S PI 041-1, WHO TRS 996 Annex 5, ICH E6(R3), or OECD GLP Advisory Document
  No. 17. A finding from one of these means "contrary to good practice", not "violates a regulation".
- **"Neither cited nor guidance-backed"** means no source of either kind was found. The rule is a local design
  choice: possibly sound internal policy, but it should not be presented to an auditor as an external requirement.

Changes take effect for scans processed **after** you save; scans already on file are not re-evaluated (though a handful of checks, like D2's username length, are re-applied live when you view an old scan; see §4).

### Engine Settings
Adjust the thresholds the rules use:
- Working-day time zone (blank means UTC). Working-hours checks evaluate against this zone, and scan results are displayed in it.
- Working-hours start/end time and whether weekends should be treated as normal working days.
- Minimum username length.
- Additional system-account name patterns to recognize (comma- or newline-separated), on top of the built-in list.
- The mass-change rule's thresholds: how many changes, within how many seconds, count as a mass change.

---

## 9. Application Settings

**Admin panel → Application settings** (Admin only) is a read-only overview of the installation's non-rule configuration, with three editable sub-pages reached from its own buttons:

- **Edit Security Policy**: the Identity password policy (minimum length, required character classes, minimum unique characters) and account lockout/sign-in rules (failed attempts before lockout, lockout duration, whether lockout applies to new users, whether an account must be confirmed to sign in). Saving takes effect immediately, on the very next sign-in/sign-up/password change; no restart needed. The same tab sets the **session timeout**: the idle timeout (minutes without activity before a user is signed out, default 20), the maximum session length (hours, default 10, after which a user must sign in again even while active) and how many minutes before the idle timeout a "You'll be signed out in …" warning appears (default 2). The warning offers "Stay signed in"; otherwise the user is signed out automatically and taken to the Login page. There is no "Remember me" option, and a password change, role change or account deactivation ends the user's other open sessions within about a minute.
- **Edit Email Settings**: the outgoing SMTP host, port, SSL flag, and sender name/address used for things like **Send Credentials** (§13). The SMTP username and password are never shown or editable here; they stay in the server's own configuration. Saving takes effect on the next email sent; no restart needed.
- **Edit AI Settings**: the OpenAI call parameters used for scan AI analysis, namely temperature, max output tokens (blank = no limit), request timeout, and the system message that sets the AI's persona/expertise. The model is fixed to `gpt-4o-mini` and the API key stays in server configuration; neither is editable here. To turn AI analysis on or off entirely, or to view the full read-only prompt template, use **AI Analysis** instead (§10). Saving takes effect on the next AI analysis run; no restart needed.

The overview page also shows (read-only) whether email and AI are configured, and the application's default logging level.

---

## 10. AI-Assisted Analysis

For scans uploaded manually through the web interface, the application automatically sends the rule-evaluation results to an AI service for a supplementary GxP/21 CFR Part 11-focused review, once the rule-based scan has already been saved. The result appears in the **AI Analysis** tab on the scan's details page (which updates itself automatically once the result is ready, with no need to refresh the page) and in the Excel export.

Things to know:
- This requires your administrator to have configured an AI service API key for the installation and to have left AI analysis switched on (**Admin panel → AI Analysis**, Admin only). The AI step runs in the background after the core scan is already saved, so a missing key, an invalid key or an AI outage never stops a scan from being created; the AI Analysis tab just stays empty.
- Scans brought in through the Integration Hub (FTP or API; see §12) do **not** get an AI analysis; that tab will be empty for those scans.
- Temperature, token limit, timeout, and the AI's system-message persona are configured under **Application Settings** (§9); the on/off switch and the read-only prompt template live under **AI Analysis**.
- Treat AI output as a supplementary aid, not a substitute for your own review judgment.

---

## 11. Viewing Logs

**Logs** in the main menu (shown to Admin and Super User only; a plain User doesn't see it and can't open the pages) offers two views, both searchable, paginated, and exportable to Excel (the export always includes every matching entry, not just the current page):

- **User Login Log**: every sign-in and sign-out, with the user, IP address, browser/user agent, timestamp, and whether it succeeded.
- **Audit Trail Log**: the application's own internal change history, field-by-field (old value → new value) where the action has one. It covers: System and System type creates/edits/deletes; User and Role creates/edits/deletes; Rule enable/disable and Engine Settings changes; the AI analysis on/off toggle; Security, Email, and AI settings changes under Application Settings; Docs-chatbot reindex runs; and, per scan, finding reviews, system reassignment, original-file downloads, and sign/reopen actions. Use the search box to filter by user, operation, object, field, value, or reason.

---

## 12. Automated Ingestion (Integration Hub)

Besides manual upload, files can reach the system two other ways, both landing in the same database you see in the web app:

- **FTP polling (Worker Service):** an external system drops a file onto a monitored FTP folder; a background service checks it periodically and processes any new file automatically.
- **API / console client:** external systems (or the bundled command-line tool) can submit a file directly to a secured HTTP endpoint. The endpoint accepts and stores the file straight away and answers with a job number; the Worker Service then scans it, and the caller checks the job's status (the command-line tool does this by itself and prints the scan number or the error). The Worker Service must be running for these scans to complete.

Neither path runs the AI-analysis step described in §10. After the Worker Service has handled an FTP file it moves it into a `Processed` folder (or a `Failed` folder if it gave up after three attempts) on the FTP server, so the drop folder only shows files still waiting. Setting these up is an administrator/IT task. As a reviewer, you'll simply see these scans appear in your **Scans** list like any other, ready to review.

> **Duplicates:** the Worker Service records every FTP file it picks up in the database before processing it, so a restart does not scan the same file twice. If you still see what looks like a duplicate scan from an automated source (for example the same file dropped twice under different names), check with your administrator before assuming it's a data error.

---

## 13. Administering Users and Roles (Admin only)

**AdminPanel → Users**
- Create a new user with an email, password, and one role (Admin, Super User, or User).
- Use **Send Credentials** on an existing user to email them a password-reset link (requires your administrator to have configured outgoing email for the installation; see **Application Settings → Edit Email Settings**, §9).

**AdminPanel → Roles**
- Create, view, edit, or delete roles.

These two areas, and the rest of Admin Panel (Application settings, AI Analysis, Docs chatbot), require the Admin role -- Super User cannot reach any of them. Super User's own configuration access is limited to Config Panel's Rule-based review engine and System type. Reopening a signed scan (§5), deleting a scan (§6), and deleting a system (§2) require Admin or Super User (see the note in §1).

---

## 14. Scan and Finding Status Reference

**Scan status**
- **Open / Under review**: not yet closed.
- **Reviewed & Signed**: closed, with a signature entry recorded.
- A scan can move between these states any number of times via Close-and-Sign and Reopen; the Reviewed & signed tab keeps the full history.

**Finding review status**
- **Unreviewed**: no decision recorded yet.
- **False Positive**
- **Investigation Required**
- **Deviation**

---

## 15. Glossary

| Term | Meaning |
|---|---|
| **Scan** | One processing run of an uploaded or ingested audit trail file, with its own results, findings, and sign-off history. |
| **Finding** | An issue flagged by one of the 19 rules on a specific record within a scan. |
| **Potential finding** | A scan's flagged records view: persisted Findings plus any live-reevaluated checks (e.g. rule D2) that reflect the current Engine Settings rather than what was stored at scan time. |
| **System** | A registered source application whose audit trail is being reviewed. |
| **System type** | A classification tag for a system (LIMS, MES, QMS, etc.). |
| **Rule** | One of 19 built-in checks (codes A1–A3, B1–B8, C1–C3, D1–D2, E1–E3) applied to every record. |
| **Engine settings** | The configurable thresholds the rules use (reason length, working hours, username length, etc.). |
| **Application settings** | The installation's security policy, email (SMTP), and AI call-parameter configuration, separate from the rule engine's own settings. |
| **Integration Hub** | The collective name for the FTP-polling Worker Service and the HTTP API used for automated/scripted ingestion. |
| **Sign-off / Close and Sign** | Marking a scan as reviewed and recording a signature entry. |
| **Reopen** | Returning a signed scan to active review. |
