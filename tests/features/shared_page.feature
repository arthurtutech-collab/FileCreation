Feature: Shared SQL page usage
  Given a single SQL page fetch should be reused for multiple target files

  Background:
    Given a Worker configuration exists with target files A, B and C

  Scenario: Shared page for multiple files
    Given SQL page 5 contains rows R (stable ordering)
    When the worker fetches page 5 from SQL
    Then the worker uses the same fetched rows R to translate and write to files A, B and C
    And no additional SQL read for page 5 is performed while writing those target files
