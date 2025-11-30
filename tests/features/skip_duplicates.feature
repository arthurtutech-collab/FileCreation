Feature: Skip duplicate writes
  Files with headers indicating progress beyond the current page should be skipped

  Background:
    Given a worker and a target file Loan0.csv

  Scenario: Skip duplicates
    Given file "Loan0.csv" header indicates last processed page = 10
    And the worker is about to process page 9 (older page)
    When processing attempts to write page 9 to "Loan0.csv"
    Then the worker detects the header progress is beyond page 9
    And the worker skips writing to "Loan0.csv" for page 9
