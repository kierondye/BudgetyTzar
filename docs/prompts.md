# Example Prompts

## Implement Issue

Implement [ISSUE_URL](ISSUE_URL), then push the branch
and create a pull request.

Before changing code, read AGENTS.md, SPECIFICATION.md, CONTRIBUTING.md,
docs/architecture.md, and README.md from the base branch. Treat those documents as the
source of truth for product behaviour, architecture boundaries, coding style, and test
strategy.

After reading them and before implementing, write down a brief implementation note that
identifies:
- the externally observable behaviour required by the issue
- the repository principles and specific guidelines that apply to this change
- the boundaries that own the behaviour
- the public API behaviours that need tests
- any production defaults, identity/authentication, ownership, or persistence concerns
  the implementation must avoid

Then implement the smallest coherent change that satisfies the issue and those notes.
If the issue appears to require behaviour or structure that conflicts with the docs,
stop and report the conflict instead of guessing.

Before opening the PR, self-review the diff against the same documents. In the PR body,
include:
- a summary of the behaviour implemented
- the applicable doc guidance you followed
- any specification or architecture updates made
- tests run

## Review Pull Request

Review [OWNER/REPO#PR_NUMBER](PR_URL).

Before inspecting the diff, read AGENTS.md, SPECIFICATION.md, CONTRIBUTING.md,
docs/architecture.md, and README.md from the PR head branch. Treat those documents as
the source of truth for product behaviour, architecture boundaries, coding style, and
test strategy.

Review in three passes:

1. Standard code-review pass

Look for bugs, security/privacy risks, behavioral regressions, incorrect API behavior,
missing error handling, unsafe defaults, production-readiness issues, and missing tests.
Do this even if no repository document explicitly calls out the issue.

2. Repository-guidance pass

Extract the applicable requirements, principles, examples, and boundary rules from the
documents you read. Check the changed code against those extracted items.

Do not simply say the documents were reviewed. Apply the relevant guidance to each
changed construct. If normal engineering judgment and the repository documents appear
to conflict, report the conflict.

3. Changed-construct audit pass

For every changed executable or configuration construct, identify:
- what behavior it changes or enables
- what inputs, configuration, state, external systems, or trust boundaries it relies on
- what can fail during normal runtime
- what defaults, fallback behavior, or construction paths it introduces
- which extracted repository-doc rules apply
- whether the construct satisfies, violates, or is unclear against those rules

A construct can be a class, record, struct, property, constructor, method, endpoint
group, dependency-injection registration, configuration block, repository operation,
helper, test support construct, or documentation section. Prefer member-level
granularity over file-level granularity when the member has its own behavior.

Use all three passes to produce findings. Do not drop findings from the standard review
just because they are not the focus of a repository-doc rule. Do not mark a rule or
construct as checked merely because another construct in the same file satisfies it.

If a construct is named in the coverage summary, its trust/default/failure/construction
assessment must appear explicitly in the Construct Audit table. "Assessed" is not enough.

Final response:
- Findings first, ordered by severity, with file/line references.
- Then "Construct Audit", as a table.
- Include every changed construct that has any changed trust boundary, normal-runtime
  failure mode, default/fallback, construction behavior, identity, authorization,
  persistence, configuration, validation, or test-support behavior.
- The table columns must be:
  Construct | Changed behavior | Trusts/depends on | Normal-runtime failure modes |
  Defaults/fallbacks/construction paths | Applicable repo-doc rules | Assessment
- Do not replace the table with a prose summary.
- Then tests run.
