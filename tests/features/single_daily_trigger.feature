Feature: Single daily trigger
  Ensure the worker processes at most once per day for the same trigger event

  Background:
    Given a Worker configuration exists with target files and Mongo lease/progress stores

  Scenario: Single daily trigger
    Given a Kafka "daily-trigger" event arrives for date "2025-11-29"
    And there has been no prior successful run for that worker today
    When the event is consumed by the worker
    Then processing for the current day starts
    And subsequent "daily-trigger" events arriving on the same date are ignored
