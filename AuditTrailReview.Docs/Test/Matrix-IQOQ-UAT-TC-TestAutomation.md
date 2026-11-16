# Matrix-IQOQ-UAT-TC-TestAutomation

Maps every test case in the [Configuration and Functional Testing - IQOQ](../../Validation%20Package/5%20Configuration%20and%20Functional%20Testing%20-%20IQOQ.md) protocol (IT-/CT-/FT-) and the [User Acceptance Testing - UAT](../../Validation%20Package/6%20User%20Acceptance%20Testing%20-%20UAT.md) protocol (UAT-) to the automated test case that connects to it, as documented in [Matrix-TestCases-Tests.md](Matrix-TestCases-Tests.md). Where no automated test case covers it, the connection column reads "Empty or Manual Test".

## Installation Testing (IQ)

| Test ID | Description | Connected Automated Test |
|---|---|---|
| IT-01 | Prerequisite Verification | Empty or Manual Test |
| IT-02 | Database Schema Installation | Empty or Manual Test |
| IT-03 | WebApp First Run and Automatic Seeding | Empty or Manual Test |
| IT-04 | Seed Administrator Login | Empty or Manual Test |
| IT-05 | Re-run Against an Already-Seeded Database | Empty or Manual Test |
| IT-06 | Integration Hub API Installation *(if configured)* | Empty or Manual Test |
| IT-07 | Worker Service Installation (Windows Service) *(if configured)* | Empty or Manual Test |
| IT-08 | FTP Connectivity and Permissions *(if configured)* | Empty or Manual Test |

## Configuration Testing (IQ)

| Test ID | Description | Connected Automated Test |
|---|---|---|
| CT-01 | Rule Enable / Disable | [TC-752](TestCases/WebApp%20Config%20Panel/TC-752-ConfigPanelRuleBasedReviewEngine.md), [TC-801](TestCases/Application%20Services/TC-801-AuditTrailLoggingService.md) |
| CT-02 | Working-Hours / Weekend Settings (Rule C3) | [TC-752](TestCases/WebApp%20Config%20Panel/TC-752-ConfigPanelRuleBasedReviewEngine.md), [TC-103](TestCases/Rule%20Engine/TC-103-RuleEngineTemporalChecks.md) |
| CT-03 | Username Validation Settings (Rules D1-D2) | [TC-752](TestCases/WebApp%20Config%20Panel/TC-752-ConfigPanelRuleBasedReviewEngine.md), [TC-104](TestCases/Rule%20Engine/TC-104-RuleEngineUserValidation.md), [TC-112](TestCases/Rule%20Engine/TC-112-RuleEngineHeuristics.md) |
| CT-04 | Mass-Change Anomaly Thresholds (Rule E1) | [TC-752](TestCases/WebApp%20Config%20Panel/TC-752-ConfigPanelRuleBasedReviewEngine.md), [TC-105](TestCases/Rule%20Engine/TC-105-RuleEngineAnomalyDetection.md) |
| CT-05 | AI Analysis Enable / Disable | [TC-703](TestCases/WebApp%20Admin%20Panel/TC-703-AdminPanelSettingsAuditTrail.md) (AI-assisted scan review toggle section, absorbed from the now-deleted TC-753) |
| CT-06 | Security Settings (Password Policy and Lockout) | [TC-703](TestCases/WebApp%20Admin%20Panel/TC-703-AdminPanelSettingsAuditTrail.md) (Audit trail tab, plus the save handler's immediate `SecurityOptionsState` reload and the session timeout settings), [TC-811](TestCases/Application%20Services/TC-811-SessionTimeoutCookiePolicy.md) (session timeout cookie policy;  the end-to-end no-restart enforcement by ASP.NET Core Identity itself is still Manual Test, per IQOQ CT-06 steps 4-6) |
| CT-07 | Email Settings | [TC-703](TestCases/WebApp%20Admin%20Panel/TC-703-AdminPanelSettingsAuditTrail.md) (Audit trail tab), [TC-706](TestCases/WebApp%20Admin%20Panel/TC-706-AdminPanelFtpAndEmailCredentials.md) (SMTP password and user name handling; the Test connection button and a real send are still manual) |
| CT-08 | AI Settings | [TC-703](TestCases/WebApp%20Admin%20Panel/TC-703-AdminPanelSettingsAuditTrail.md) (Audit trail tab, and `OnPostSaveAiAsync` -- encrypted API key, restricted model), [TC-806](TestCases/Application%20Services/TC-806-AiApiKeyEncryption.md), [TC-814](TestCases/Application%20Services/TC-814-AiProviderEndpointResolution.md) (provider endpoint resolution) |
| CT-09 | System Type Management | [TC-751](TestCases/WebApp%20Config%20Panel/TC-751-ConfigPanelSystemTypeManagement.md) |
| CT-10 | General Settings (Date/Time Format and Display Time Zone) | [TC-804](TestCases/Application%20Services/TC-804-DateTimeDisplayFormatting.md), [TC-703](TestCases/WebApp%20Admin%20Panel/TC-703-AdminPanelSettingsAuditTrail.md) (Audit trail tab only) |

## Functional Testing (OQ)

| Test ID | Description | Connected Automated Test |
|---|---|---|
| FT-01 | Data Integrity Rules (A1, A3) | [TC-101](TestCases/Rule%20Engine/TC-101-RuleEngineDataIntegrity.md) |
| FT-02 | Record-Chain Continuity (A2) | [TC-109](TestCases/Rule%20Engine/TC-109-RuleEngineRecordChainContinuity.md) |
| FT-03 | Action Consistency (B1-B7) | [TC-102](TestCases/Rule%20Engine/TC-102-RuleEngineActionConsistency.md) |
| FT-04 | Segregation of Duties (B8) | [TC-108](TestCases/Rule%20Engine/TC-108-RuleEngineSegregationOfDuties.md) |
| FT-05 | Temporal Checks (C1-C3) | [TC-103](TestCases/Rule%20Engine/TC-103-RuleEngineTemporalChecks.md) |
| FT-06 | User Validation (D1-D2) | [TC-104](TestCases/Rule%20Engine/TC-104-RuleEngineUserValidation.md), [TC-112](TestCases/Rule%20Engine/TC-112-RuleEngineHeuristics.md) |
| FT-07 | Anomaly Detection: Mass Change (E1) | [TC-105](TestCases/Rule%20Engine/TC-105-RuleEngineAnomalyDetection.md) |
| FT-08 | Anomaly Detection: Oscillating Value (E2) | [TC-110](TestCases/Rule%20Engine/TC-110-RuleEngineOscillatingValuePattern.md) |
| FT-09 | Anomaly Detection: Nullifying Data (E3) | [TC-111](TestCases/Rule%20Engine/TC-111-RuleEngineNullifyingData.md) |
| FT-10 | AI-Assisted Analysis *(if enabled)* | [TC-107](TestCases/Rule%20Engine/TC-107-ScanResultAIAnalysisHelpers.md), [TC-510](TestCases/AI%20Scan%20Analysis/TC-510-AIScanAnalysisPromptBuilding.md), [TC-703](TestCases/WebApp%20Admin%20Panel/TC-703-AdminPanelSettingsAuditTrail.md) |
| FT-11 | Integration Hub API Ingestion *(if configured)* | [TC-201](TestCases/Integration%20Hub%20API/TC-201-IntegrationHubApiRejectsInvalidUpload.md), [TC-202](TestCases/Integration%20Hub%20API/TC-202-IntegrationHubApiAcceptsValidUpload.md) |
| FT-12 | Worker Service FTP Ingestion *(if configured)* | [TC-401](TestCases/Worker%20Service/TC-401-WorkerServiceFtpPolling.md), [TC-813](TestCases/Application%20Services/TC-813-FtpSettingsResolution.md) (settings resolution and certificate rules only), [TC-706](TestCases/WebApp%20Admin%20Panel/TC-706-AdminPanelFtpAndEmailCredentials.md) (FTP tab only) |
| FT-13 | In-App Help | [TC-UI-008](TestCases/UI/TC-UI-008-HelpPagesRender.md) |
| FT-14 | Home Page, Login, and Invalid Login | [TC-601](TestCases/UI/TC-601-HomePageVisible.md), [TC-602](TestCases/UI/TC-602-DashboardAfterLogin.md), [TC-604](TestCases/UI/TC-604-LoginWithInvalidCredentials.md) |
| FT-15 | Role-Based Navigation | [TC-603](TestCases/UI/TC-603-MenuNavigation.md), [TC-810](TestCases/Application%20Services/TC-810-LogPagesRoleRestriction.md) (Logs pages only) |
| FT-16 | Unauthenticated Redirect | [TC-791](TestCases/WebApp%20Identity/TC-791-DisabledIdentityPages.md) (Identity area only: `/Account/Manage` requires sign-in, self-registration disabled; the redirect for the main `Pages/` tree is still Empty or Manual Test) |
| FT-17 | Manual Scan Upload | [TC-782](TestCases/WebApp%20Scan%20Management/TC-782-ScanUploadAndPreview.md) |
| FT-18 | Reviewer Records a Finding Decision | [TC-783](TestCases/WebApp%20Scan%20Management/TC-783-ScanReviewSignOffAndLifecycle.md) |
| FT-19 | Close and Sign a Scan | [TC-783](TestCases/WebApp%20Scan%20Management/TC-783-ScanReviewSignOffAndLifecycle.md) |
| FT-20 | Close and Sign Is Blocked Until Reviewed | [TC-783](TestCases/WebApp%20Scan%20Management/TC-783-ScanReviewSignOffAndLifecycle.md) |
| FT-21 | Reopen a Closed Scan | [TC-783](TestCases/WebApp%20Scan%20Management/TC-783-ScanReviewSignOffAndLifecycle.md) |
| FT-22 | Copy a Direct Scan Link | Empty or Manual Test |
| FT-23 | User Administration | [TC-702](TestCases/WebApp%20Admin%20Panel/TC-702-AdminPanelUserManagement.md), [TC-803](TestCases/Application%20Services/TC-803-EmailNotifications.md) (credentials e-mail template only; actual delivery is still Manual Test, see CT-07) |
| FT-24 | Role Administration | [TC-701](TestCases/WebApp%20Admin%20Panel/TC-701-AdminPanelRoleManagement.md) |
| FT-25 | System Registry | [TC-771](TestCases/WebApp%20System%20Management/TC-771-SystemManagement.md) |
| FT-26 | File Parsing and Vendor Header Mapping | [TC-301](TestCases/Shared%20Ingestion%20Pipeline/TC-301-SharedIngestionFileParsing.md), [TC-304](TestCases/Shared%20Ingestion%20Pipeline/TC-304-FileParserUnitTests.md), [TC-305](TestCases/Shared%20Ingestion%20Pipeline/TC-305-HeaderMappingAndValueParsing.md) |
| FT-27 | System Validation on Scan | [TC-302](TestCases/Shared%20Ingestion%20Pipeline/TC-302-SharedIngestionSystemValidation.md), [TC-201](TestCases/Integration%20Hub%20API/TC-201-IntegrationHubApiRejectsInvalidUpload.md), [TC-202](TestCases/Integration%20Hub%20API/TC-202-IntegrationHubApiAcceptsValidUpload.md) |
| FT-28 | Scan Persistence, List Paging/Search, and System Assignment | [TC-781](TestCases/WebApp%20Scan%20Management/TC-781-ScanListFilteringAndAssignment.md), [TC-784](TestCases/WebApp%20Scan%20Management/TC-784-ScanEditingAndDeletion.md), [TC-303](TestCases/Shared%20Ingestion%20Pipeline/TC-303-SharedIngestionRecordIntegrity.md) |
| FT-29 | Preview and Excel Export | [TC-782](TestCases/WebApp%20Scan%20Management/TC-782-ScanUploadAndPreview.md), [TC-783](TestCases/WebApp%20Scan%20Management/TC-783-ScanReviewSignOffAndLifecycle.md) (scan Excel export and original-file download) |
| FT-30 | Rule Execution and Findings | [TC-106](TestCases/Rule%20Engine/TC-106-RuleEngineCrossCuttingBehavior.md) |
| FT-31 | Review and Signature Evidence | [TC-783](TestCases/WebApp%20Scan%20Management/TC-783-ScanReviewSignOffAndLifecycle.md), [TC-801](TestCases/Application%20Services/TC-801-AuditTrailLoggingService.md) |
| FT-32 | User Login Log | [TC-802](TestCases/Application%20Services/TC-802-UserActivityLogging.md) |
| FT-33 | Application Audit Trail Log | [TC-801](TestCases/Application%20Services/TC-801-AuditTrailLoggingService.md), [TC-701](TestCases/WebApp%20Admin%20Panel/TC-701-AdminPanelRoleManagement.md), [TC-702](TestCases/WebApp%20Admin%20Panel/TC-702-AdminPanelUserManagement.md), [TC-751](TestCases/WebApp%20Config%20Panel/TC-751-ConfigPanelSystemTypeManagement.md), [TC-752](TestCases/WebApp%20Config%20Panel/TC-752-ConfigPanelRuleBasedReviewEngine.md), [TC-771](TestCases/WebApp%20System%20Management/TC-771-SystemManagement.md), [TC-783](TestCases/WebApp%20Scan%20Management/TC-783-ScanReviewSignOffAndLifecycle.md) |
| FT-34 | UTC Timestamp Consistency | [TC-801](TestCases/Application%20Services/TC-801-AuditTrailLoggingService.md), [TC-802](TestCases/Application%20Services/TC-802-UserActivityLogging.md), [TC-303](TestCases/Shared%20Ingestion%20Pipeline/TC-303-SharedIngestionRecordIntegrity.md), [TC-804](TestCases/Application%20Services/TC-804-DateTimeDisplayFormatting.md) |
| FT-35 | Authentication and Role-Based Access to Administrative Functions | [TC-810](TestCases/Application%20Services/TC-810-LogPagesRoleRestriction.md) (Logs pages only), [TC-791](TestCases/WebApp%20Identity/TC-791-DisabledIdentityPages.md) (Identity area only); the Admin panel / Config panel role checks are still Manual Test, [TC-816](TestCases/Application%20Services/TC-816-PagesRequireAuthorizationByDefault.md) (every page declares authorization), [TC-815](TestCases/Application%20Services/TC-815-SecurityHeadersAndCookieHardening.md) (security headers and cookies) |
| FT-36 | Large Upload Queue | [TC-402](TestCases/Worker%20Service/TC-402-ScanUploadJobQueueProcessing.md) (tracked fallback only; the SQL Server/PostgreSQL set-based path and the live stream read are Manual Test, see its Notes) |
| FT-37 | MCP Server Access *(if configured)* | [TC-705](TestCases/WebApp%20Admin%20Panel/TC-705-AdminPanelMcpKeyManagement.md), [TC-807](TestCases/Application%20Services/TC-807-McpApiKeyAuthentication.md), [TC-812](TestCases/Application%20Services/TC-812-McpQueryTools.md) |
| FT-38 | Finding Messages Explain the Detected Issue | [TC-108](TestCases/Rule%20Engine/TC-108-RuleEngineSegregationOfDuties.md), [TC-109](TestCases/Rule%20Engine/TC-109-RuleEngineRecordChainContinuity.md), [TC-110](TestCases/Rule%20Engine/TC-110-RuleEngineOscillatingValuePattern.md), [TC-111](TestCases/Rule%20Engine/TC-111-RuleEngineNullifyingData.md) (B8, A2, E2, E3 messages only) |

## User Acceptance Testing (UAT)

| Test ID | Description | Connected Automated Test |
|---|---|---|
| UAT-01 | Core Lifecycle: Upload Through Review to Sign-Off | Empty or Manual Test |
| UAT-02 | API-Based Ingestion End-to-End *(if the Integration Hub API is deployed)* | Empty or Manual Test |
| UAT-03 | FTP-Based Ingestion End-to-End, Including a Service Restart *(if the Worker Service/FTP is deployed)* | Empty or Manual Test |
| UAT-04 | Multi-Format Ingestion | Empty or Manual Test |
| UAT-05 | Full Disposition, Close, Reopen, and Re-Sign Workflow | Empty or Manual Test |
| UAT-06 | New System Onboarding ("Day One" Business Flow) | Empty or Manual Test |
| UAT-07 | Auditor/Inspector Traceability Walk-Through | Empty or Manual Test |
| UAT-08 | Ingestion Failure from the Business User's Perspective | Empty or Manual Test |
| UAT-09 | Representative Day-to-Day Volume | [TC-P-001](TestCases/Performance/TC-P-001%20Uploading%20Files.md), [TC-P-002](TestCases/Performance/TC-P-002%20Scan%20Details.md) |
| UAT-10 | Role-Based Business Access | Empty or Manual Test |
| UAT-11 | Configuration Change Visibility | Empty or Manual Test |
