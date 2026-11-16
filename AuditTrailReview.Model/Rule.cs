using System;
using System.Collections.Generic;
using System.Text;

namespace AuditTrailReview.Model
{
    public class Rule
    {
        public int Id { get; set; }

        public string? RuleCode { get; set; }   // A1, B3, etc.
        public string? Description { get; set; }

        // Literal regulatory citation(s) that this rule's check directly enforces, "; "-separated
        // (e.g. "21 CFR 11.10(e); 21 CFR 58.130(e)"). Set only when the rule's failure condition
        // is essentially a restatement of specific regulatory text, not merely "inspired by" or
        // supported through the ALCOA+/data-integrity-guidance layer built on top of it
        // (MHRA/PIC/S/FDA Q&A). Frameworks checked in full: 21 CFR Part 11, 21 CFR Part 211
        // (§§211.68/211.100/211.180/211.194), 21 CFR Part 58 (§58.130), 21 CFR Part 312 (§312.62)
        // and Part 812 (§812.140), and EudraLex Vol. 4 Annex 11.
        //
        // Citations are context-specific: 211.x binds GMP finished pharmaceuticals, 58.130(e)
        // nonclinical (GLP) studies, Annex 11 EU GMP computerised systems, Part 11 FDA-regulated
        // electronic records generally -- a value here lists the clauses that apply in whichever
        // of those frameworks governs the audited system, not a claim that all bind at once.
        //
        // Left null when no such literal text was found -- see
        // AuditTrailReview.Docs/Reviews/Audit-Trail-Rule-Regulatory-Basis-Review.md for the
        // rule-by-rule research behind every populated (and every deliberately blank) value. A
        // null is a documentation finding in its own right: it means the rule is either an
        // ALCOA+/guidance-grounded data-integrity control or a purely heuristic/risk-based
        // anomaly signal, not something a specific clause requires.
        public string? RegulatoryBasis { get; set; }

        // Best-practice / international-harmonization backing for a rule that has NO binding
        // clause (i.e. RegulatoryBasis is null), "; "-separated. Sources: PIC/S PI 041-1,
        // WHO TRS 996 Annex 5, ICH E6(R3), and OECD GLP Advisory Document No. 17 (which
        // supersedes No. 10 for computerised systems). Populated only where RegulatoryBasis is
        // null -- when a binding clause exists, that clause is the answer and this stays null,
        // so the two fields are mutually exclusive by construction.
        //
        // These are recommendations and inspection expectations, not law: a finding from a rule
        // backed only by GuidanceBasis says "this is contrary to internationally harmonized good
        // practice", never "this violates a regulation". A rule with BOTH fields null has neither
        // -- which is a deliberate, actionable signal, not an oversight.
        public string? GuidanceBasis { get; set; }

        public bool IsEnabled { get; set; }    // <-- configuration stored here

        public int RuleCategoryId { get; set; }
        public RuleCategory? RuleCategory { get; set; }
    }


    public enum RulesEnum
    {
        // 1 was A1 (all records must have a unique identifier), removed as part of a deliberate
        // decision to reduce Category A to its three core checks (see the rule-management
        // decision recorded alongside the other retirements below). Unlike the earlier
        // retirements, this was not because the rule lacked backing -- it had guidance support
        // (WHO TRS 996 Annex 5 App. 1, Attributable; ICH E6(R3) §2.12.8) -- but a scope
        // reduction, not a regulatory-adequacy finding. The id is retired, never reused: rows in
        // ScanResults persisted before the removal still carry "[A1] Duplicate unique identifier
        // ..." in their Finding text, and those records are left exactly as they were written.
        // See Audit-Trail-Rule-Regulatory-Basis-Review.md.
        // 2 was D1 (username must not be a real personal name), removed in a fifth
        // rule-management round at the user's instruction -- a scope decision, not a
        // finding that it lacked backing: it had guidance support (PIC/S PI 041-1 §9.5;
        // WHO TRS 996 Annex 5 App. 1, Attributable; ICH E6(R3) §4.3.8), though this review
        // had already flagged it as the rule whose stated purpose ("not a personal name")
        // least matched what its guidance actually supports ("a real system-issued login")
        // -- see the "Rules worth a hard look" section of Audit-Trail-Rule-Regulatory-Basis-
        // Review.md. The underlying `LoginNameHeuristics.LooksLikePersonalName` heuristic is
        // NOT dead code: `Pages/Scan/Preview.cshtml.cs` calls it directly for its own Excel
        // export, independent of this rule engine, so it and its tests stay. The id is
        // retired, never reused: rows in ScanResults persisted before the removal still
        // carry "[D1] Username '...' appears to be a personal name, not a system login" in
        // their Finding text, and those records are left exactly as they were written.
        B1_modify_must_change_the_value_before_after = 3,
        C1_timestamps_in_logical_order_for_each_record = 4,
        A1_mandatory_fields_user_timestamp_action = 5,
        // 6 was A3 (timestamp must be in UTC), removed -- although it did carry guidance
        // backing (WHO TRS 996 Annex 5 §4.12 / Appendix 1's "contemporaneous" expectation,
        // read as requiring internally consistent/synchronized clocks, not literally UTC), that
        // link was judged too weak to justify keeping the rule: no regulation or guidance
        // actually mandates the UTC offset specifically, only that timestamps be trustworthy and
        // comparable. The id is retired, never reused: rows in ScanResults persisted before the
        // removal still carry "[A3] Timestamp is not UTC ..." in their Finding text, and those
        // records are left exactly as they were written. See
        // Audit-Trail-Rule-Regulatory-Basis-Review.md.
        A3_no_duplicate_records_user_timestamp_action_oldvalue_newvalue = 7,
        // 8 was A4 (comment field length check), removed in the same rule-management pass as id
        // 1, 19, 23 and 25 -- a scope reduction of Category A, not a finding that the rule lacked
        // backing (it had guidance support: PIC/S PI 041-1 §8.7/§9.6; ICH E6(R3) §2.12.6). The id
        // is retired, never reused: rows in ScanResults persisted before the removal still carry
        // "[A4] Reason field is too short ..." in their Finding text, and those records are left
        // exactly as they were written. See Audit-Trail-Rule-Regulatory-Basis-Review.md.
        B2_update_must_have_both_values_before_after = 9,
        B3_create_must_have_the_value_after = 10,
        B4_create_must_have_not_have_the_old_value = 11,
        // 12 was B5 (create action must have a reason for creation), removed -- of the nine
        // regulatory and guidance documents surveyed, every one attaches the "reason" requirement
        // to a CHANGE or a DELETION and none to creation (21 CFR 58.130(e), EU Annex 11 §9,
        // PIC/S PI 041-1 §8.7/§9.6, WHO TRS 996 Annex 5's audit-trail definition, OECD No. 17
        // §3.4, ICH E6(R3) §2.12.6). It was the only rule in the engine backed by neither a
        // binding clause nor harmonization guidance. The id is retired, never reused: results
        // persisted before the removal still carry "[B5] Create operation is missing a reason
        // ..." and are left exactly as written. See Audit-Trail-Rule-Regulatory-Basis-Review.md.
        B5_update_must_have_reason_for_update = 13,
        B6_delete_must_have_reason_for_deletion =14,
        B7_delete_must_retain_the_old_value =15,
        C2_no_timestamps_in_the_future = 16,
        C3_detect_deviations_from_normal_working_hours = 17,
        // 18 was A6 (timestamp in ISO 8601 compliant format), removed -- no regulation or
        // guidance requires a specific timestamp format, and the check ran only after the
        // timestamp had already parsed successfully. The id is retired, never reused: rows in
        // ScanResults persisted before the removal still carry "[A6] Timestamp is not in ISO
        // 8601 format ..." in their Finding text, and those records are left exactly as they
        // were written. See Audit-Trail-Rule-Regulatory-Basis-Review.md.
        // 19 was A5 (reason must contain at least one alphabetic character), removed in the same
        // rule-management pass as id 1, 8, 23 and 25 -- a scope reduction of Category A, not a
        // finding that the rule lacked backing (it had guidance support: PIC/S PI 041-1
        // §8.7/§9.6; ICH E6(R3) §2.12.6). The id is retired, never reused: rows in ScanResults
        // persisted before the removal still carry "[A5] Reason does not contain enough
        // alphabetic characters ..." in their Finding text, and those records are left exactly
        // as they were written. See Audit-Trail-Rule-Regulatory-Basis-Review.md.
        D2_user_must_contain_at_least_x_characters = 20,
        E_1_mass_changes_in_a_short_time =21,
        D1_shared_generic_service_account_usage = 22,
        // 23 was A8 (boilerplate reason detection), removed in the same rule-management pass as
        // id 1, 8, 19 and 25 -- a scope reduction of Category A, not a finding that the rule
        // lacked backing (it had guidance support: PIC/S PI 041-1 §8.7/§9.6; OECD No. 17 §3.4).
        // The id is retired, never reused: rows in ScanResults persisted before the removal
        // still carry "[A8] Reason ... is reused verbatim ..." in their Finding text, and those
        // records are left exactly as they were written. See
        // Audit-Trail-Rule-Regulatory-Basis-Review.md.
        A2_record_chain_continuity = 24,
        // 25 was A7 (sequence-gap detection), removed in the same rule-management pass as id 1,
        // 8, 19 and 23 -- a scope reduction of Category A, not a finding that the rule lacked
        // backing (it had guidance support: WHO TRS 996 Annex 5 App. 1, Complete; OECD No. 17
        // §3.2; ICH E6(R3) §2.12.2). The id is retired, never reused: rows in ScanResults
        // persisted before the removal still carry "[A7] Sequence gap detected ..." in their
        // Finding text, and those records are left exactly as they were written. See
        // Audit-Trail-Rule-Regulatory-Basis-Review.md.
        // 26 was C4 (time-gap detection), removed in a fourth rule-management round -- a
        // scope decision, not a regulatory-adequacy finding: it had guidance backing (PIC/S
        // PI 041-1 §5.6.2 / §9.8, the same risk-based-review-method support that backs its
        // surviving siblings C3 and E1). The id is retired, never reused: rows in ScanResults
        // persisted before the removal still carry "[C4] Time gap detected ..." in their
        // Finding text, and those records are left exactly as they were written. See
        // Audit-Trail-Rule-Regulatory-Basis-Review.md.

        // 27 is B8 (segregation of duties: the user who approved/reviewed an object must not
        // be the same user who created or modified it), added at the user's instruction --
        // this is an addition, not a removal or renumbering, so no other rule shifts. Backed
        // by 21 CFR 211.194(a)(8) (a second person must review the original records; "second
        // person" necessarily excludes the person who performed the work), corroborated by
        // PIC/S PI 041-1 §9.3 ("authorisation to generate data should be separate to
        // authorisation to verify data") and §6.6.4. Recognizes exactly "Create"/"Update" as
        // the create/modify step and exactly "Approve"/"Review" as the approval step (case-
        // insensitive literal matching, the same style B1-B7 already use for Create/Update/
        // Delete -- no fuzzy synonyms). See Audit-Trail-Rule-Regulatory-Basis-Review.md and
        // Audit-Trail-Rule-Engine-Review.md (originally proposed there as "D4").
        B8_approver_must_not_be_the_creator_or_modifier = 27,

        // 28 is E2 (oscillating value pattern detection: the same object/field's value
        // repeatedly flips back and forth between exactly two values -- A, B, A, B -- within
        // one scan), added at the user's instruction. This is an addition, not a removal or
        // renumbering, so no other rule shifts. A repeating flip-flop is a risk-based
        // anomaly/investigative signal, not a literal restatement of any regulatory text, so
        // -- like C3 and E1 -- it carries a GuidanceBasis rather than a RegulatoryBasis:
        // PIC/S PI 041-1 §5.6.2 / §9.8 (a risk-based sample of computerised system logs /
        // audit trails), the same review-method backing C3 and E1 already carry. Grouped by
        // ObjectType+Object+Field (the same key A2's record-chain-continuity check uses),
        // evaluated in actual chronological order within the scan; a Delete resets the
        // tracked value sequence for that object/field, the same reset A2 already applies.
        // Note: an earlier, different, never-implemented proposal in
        // Audit-Trail-Rule-Engine-Review.md ("off-pattern volume per user") had provisionally
        // used the code "E2" -- that proposal remains open and unbuilt, and would need the
        // next available Category E code (e.g. E3) if ever implemented, since this rule has
        // taken E2. See Audit-Trail-Rule-Regulatory-Basis-Review.md.
        E2_oscillating_value_pattern_detection = 28,

        // 29 is E3 (nullifying data: an Update changes a genuine, non-blank, non-zero prior
        // value to blank/NULL or the literal value "0"), added at the user's instruction. This
        // is an addition, not a removal or renumbering, so no other rule shifts. A value being
        // wiped out is not itself a violation -- a legitimate correction can genuinely zero out
        // an erroneous entry -- so, like C3/E1/E2, this carries a GuidanceBasis rather than a
        // RegulatoryBasis: PIC/S PI 041-1 §5.6.2 / §9.8 (a risk-based sample of computerised
        // system logs / audit trails), the same review-method backing C3, E1 and E2 already
        // carry. Only fires when OldValue was itself genuine (non-blank, not already "0");
        // compared trimmed, with "0" matched literally (not "0.0"/"0.00" or other numeric-zero
        // spellings). See Audit-Trail-Rule-Regulatory-Basis-Review.md.
        E3_nullifying_data_change_to_zero_or_blank = 29,

    }


}
