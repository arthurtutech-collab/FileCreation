Feature: File generation workflow
  As a File Generation service
  I want predictable behavior for triggers, shared page processing, leader takeover, duplicate skipping and completion
  So that file outputs are consistent and resumable across pod failures

  Background:
    Given a Worker configuration exists with target files and Mongo lease/progress stores

  Scenario: Single daily trigger
    Given a Kafka "daily-trigger" event arrives for date "2025-11-29"
    And there has been no prior successful run for that worker today
    When the event is consumed by the worker
    Then processing for the current day starts
    And subsequent "daily-trigger" events arriving on the same date are ignored

  Scenario: Shared page for multiple files
    Given SQL page 5 contains rows R (stable ordering)
    And target files A, B and C are configured for this worker
    When the worker fetches page 5 from SQL
    Then the worker uses the same fetched rows R to translate and write to files A, B and C
    And no additional SQL read for page 5 is performed while writing those target files

  Scenario: Crash-resume takeover
    Given a leader instance started processing and wrote file headers marking status "Started"
    And the leader dies mid-run without finalizing files
    And the leader's Mongo lease has expired
    When another pod detects leases expired and attempts to acquire leadership
    And the pod successfully acquires the Mongo lease
    Then the new leader reads file progress for all files
    And resumes processing from the smallest outstanding page across all target files

  Scenario: Skip duplicates
    Given file "Loan0.csv" header indicates last processed page = 10
    And the worker is about to process page 9 (older page)
    When processing attempts to write page 9 to "Loan0.csv"
    Then the worker detects the header progress is beyond page 9
    And the worker skips writing to "Loan0.csv" for page 9

  Scenario: Completion flow
    Given the worker has processed the final page N for file "Loan0.csv"
    When finalization is triggered for file "Loan0.csv"
    Then the worker removes or clears the progress header for that file
    And the worker updates the file status to "Completed" in Mongo progress store
    And the worker publishes a completion Kafka event containing at least:
      | workerId | fileId  | totalRows | completedAt          |
      | LoanWorker | Loan0 | <totalRows> | <ISO8601-timestamp> |

  # Edge case: Partial page results and idempotency
  Scenario: Idempotent re-run of the same page
    Given page 7 was partially processed previously and header indicates page=6
    When the leader resumes and re-processes page 7
    Then writes are idempotent (no duplicate rows appended) or the writer skips already-written rows based on progress

  # Observability criteria (non-functional, included for completeness)
  Scenario: Observability on takeover and completion
    Given a takeover or completion event occurs
    When the worker logs events
    Then logs include structured fields: WorkerId, FileId, EventType, Page, Rows, InstanceId, CorrelationId
