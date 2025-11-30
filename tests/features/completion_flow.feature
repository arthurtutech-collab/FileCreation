Feature: Completion flow
  Ensure finalization removes headers, marks status completed, and publishes an event

  Background:
    Given a worker processing file "Loan0.csv"

  Scenario: Completion flow
    Given the worker has processed the final page N for file "Loan0.csv"
    When finalization is triggered for file "Loan0.csv"
    Then the worker removes or clears the progress header for that file
    And the worker updates the file status to "Completed" in Mongo progress store
    And the worker publishes a completion Kafka event containing at least: workerId, fileId, totalRows, completedAt
