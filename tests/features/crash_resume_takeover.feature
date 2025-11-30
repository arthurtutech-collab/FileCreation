Feature: Crash-resume takeover
  Leader dies mid-run and another pod takes over and resumes processing from smallest outstanding page

  Background:
    Given a cluster of worker pods and a Mongo lease/progress store

  Scenario: Crash-resume takeover
    Given a leader instance started processing and wrote file headers marking status "Started"
    And the leader dies mid-run without finalizing files
    And the leader's Mongo lease has expired
    When another pod detects leases expired and attempts to acquire leadership
    And the pod successfully acquires the Mongo lease
    Then the new leader reads file progress for all files
    And resumes processing from the smallest outstanding page across all target files
