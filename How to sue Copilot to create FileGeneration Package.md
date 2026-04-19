

Create file generation service package
as a C# software professional engineer, I would like to create a hosted service worker running on OCP, and deploy to 4 different data center. The worker will read Loan related data from 4 tables in SQL, we can create a view for that. Translate the data write to Loan0, Loan1, Loan2 and Loan3 files.  


1. when the worker consume the data loaded kafka event then start the work, the event type in the kafka topics is configurable for different worker.read pagination data from SQL- using Dapper

If worker consume the kafka event to trigger the process, there is no more kafka event to trigger again in the same day


2. The same page of data can be provided to create those 4 files, so the worker doesn't need to keep reading data from SQL for each file. 


3. after worker read the page of data, the worker will process the data by translating logic of each file, so each file could have its own translating rule


4. when the file generation is starting, write a status-start on file level to MongoDB


5. when one pagination data is processed, write to the file but also write the number of page and rows that file has in the first line of the file


6. The component will be deployed to 4 PODS at the beginning and all are running up. The other POD instance worker will automatically take over and keep working if the original POD instance is dead or hangs there.

the other POD instance can’t be triggered by kafka event,  because there is only one event trigger everyday- check the MongoDB, if the status is start, then read the first line of 4 files, pick up the smallest number of pagination and rows and keep reading the data from SQL, so the worker doesn't need to restart from the first page of SQL. 

Make there is no deadlock in file creation and file status check in MongoDB 


7. if the number of the page and the rows are greater than the current page from SQL, worker will skip and doesn't need to write the data to the file again. 

So if a file is completed, it doesn’t need to re-generate.


8. after reading the last page of data, and writing data to those 4 files, remove the first line of each file and close the file. 


9. Update the status-completed on file level to MongoDB. And publish a kafka event with the event type when a file is generated.


10. Define interface or configuration and wrap this code as a package, so other worker such as customer worker with ID "CustomerWorker" can reference this package to do this similar thing to create customer0 and customer2 files. When other workers use this package, they can configure their SQL statement, how many files will be created and etc. 



11. write logs to splunk so the splunk dashboard of worker can be created


12. there are 4 instances in different PODs, but make sure only one instance will create files.

 

13. Create the File generation package to a project, describe how does the other POD can take over when the original POD is dead or hangs

Write two examples, create LoanWorker as HostedService and reference the File generation package to create 4 files- Loan0, Loan1, Loan2 and Loan3 in a project.

Create CustomerWorker as HostedService and reference the File generation package to create 2 files - CFM and CFK in one project

add ocp readiness and liveness healthcheck


14. cerate unit testing for the package and the LoanWorker, ID is LoanWorker

15. create BDD testing too



Refine the sentences and organize






Here’s a refined and organized specification for your file generation service package, structured as a professional engineering design document.

📦 File Generation Service Package – Design Specification
🎯 Goal
Create a reusable C# package that enables hosted service workers to generate files from SQL data, triggered by Kafka events, with resilience across multiple OpenShift (OCP) pods and data centers.

🏗️ Architecture Overview
Trigger: Kafka event (configurable per worker, only once per day).

Data Source: SQL view combining 4 loan-related tables, accessed via Dapper with pagination.

File Outputs: Loan0, Loan1, Loan2, Loan3 (each with independent translation logic).

Status Tracking: MongoDB for file-level status (start, progress, completed).

Failover: 4 pods deployed; only one active writer. Standby pods resume work if the leader fails.

Logging: Splunk integration for monitoring dashboards.

Health Checks: OCP readiness and liveness endpoints.

⚙️ Workflow Steps
Kafka Trigger


Worker consumes a Kafka event (configurable type).

Only one event per day triggers the process.

SQL Pagination


Read paginated data from SQL using Dapper.

One page of data is shared across all file outputs.

Translation Logic


Each file applies its own translation rules to the same page of data.

Start Status


When file generation begins, write status=start to MongoDB for each file.

Progress Header


Each file’s first line contains the current page number and row count.

Failover Handling


4 pods deployed across data centers.

Only one pod processes files at a time.

Standby pods check MongoDB:

If status=start, read headers of all files.

Resume from the smallest (page, rows) value.

Avoid restarting from page 1.

Deadlock prevention via distributed lock in MongoDB.

Skip Logic


If header (page, rows) is greater than current SQL page, skip writing.

Completed files are not regenerated.

Finalization


After last page:

Remove header line.

Close file.

Update status=completed in MongoDB.

Publish Kafka completion event.

Reusable Package


Define interfaces and configuration.

Other workers (e.g., CustomerWorker) can configure:

SQL query/view.

Number of files.

Translation rules.

Logging


Structured logs to Splunk for dashboards.

Single Writer Guarantee


Leader election ensures only one pod writes files.

🧩 Package Interfaces
IEventTrigger – Kafka event handling.

IDataPager<T> – SQL pagination via Dapper.

IFileTranslator<T> – Per-file translation logic.

IFileSink – File writing and header management.

IStatusStore – MongoDB status tracking.

ILeaderElection – Distributed lock for single writer.

🖥️ Example Hosted Services
LoanWorker
Files: Loan0, Loan1, Loan2, Loan3.

SQL View: Combines 4 loan tables.

Translation: Each file has unique formatting logic.

Deployment: 4 pods across data centers.

CustomerWorker
Files: CFM, CFK.

SQL View: Customer-related data.

Translation: Custom rules for each file.

Deployment: Same failover and logging strategy.

🩺 Health Checks
Readiness: SQL, MongoDB, Kafka connectivity.

Liveness: Worker process responsiveness.

Endpoints:

/health/ready

/health/live

🧪 Testing Strategy
Unit Tests
LoanWorker (ID=LoanWorker):

SQL pagination correctness.

Translation logic per file.

Status transitions in MongoDB.

Skip logic validation.

BDD Tests
Scenario 1: Single Kafka trigger → all files generated once.

Scenario 2: Failover → standby pod resumes from smallest header.

Scenario 3: Skip logic → completed files not regenerated.

Scenario 4: Finalization → headers removed, status updated, Kafka completion event published.

📌 Summary
This package provides:

A reusable framework for file generation workers.

Resilient failover across pods and data centers.

Configurable translation and SQL logic for different workers.

Observability via Splunk logs and OCP health checks.

Testing coverage with unit and BDD scenarios.

Would you like me to draft the actual C# project skeleton code (interfaces, base classes, and example HostedService implementations) so you can directly plug it into your solution?


