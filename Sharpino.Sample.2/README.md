Example of seat bookings in event sourcing
 1. Booking seats among multiple rows (where those rows are aggregates) in an event-sourcing way.
 2. Booking seats in a single row by two concurrent commands that singularly do not violate any invariant rule and yet the final state is potentially invalid.

 ## Problem 1

 I have two rows of seats related to two different streams of events. Each row has 5 seats. I want to book a seat in row 1 and a seat in row 2. I want to do this in a single transaction so that if just one of the claimed seats is already booked then the entire multiple-row transaction fails and no seats are booked at all.
 ### Questions: 
 1) can you do this in a transactional way?
 Answer: yes because in-memory and Postgres event-store implementation as single sources of truth are transactional. The runTwoCommands in the Command Handler is transactional.
 2) Can it handle more rows?
 up to tre. (runThreeCommands in the Command Handler)
 3) Is feasible to scale to thousands of seats/hundreds of rows (even though we know that few rows will be actually involved in a single booking operation)?
 Not yet.
 3) Is Apache Kafka integration included in this example?
 No.
 4) Is EventStoreDb integration included in this example?
 Not yet (it will show the "undo" feature of commands to do rollback commands on multiple streams of events).

 ## Problem 2
 There is an invariant rule that says that no booking can end up in leaving the only middle seat free in a row. 
 This invariant rule must be preserved even if two concurrent transactions try to book the two left seats and the two right seats independently so violating (together) this invariant.

 ### Questions:
 1) can you just use a lock on any row to solve this problem?
 Answer: yes by setting PessimisticLocking to true in appSettings.json that will force single-threaded execution of any command involving the same "aggregate" 
 2) can you solve this problem without using locks?
 Answer: yes. if I set PessimisticLocking to false then parallel command processing is allowed and invalid events can be stored in the eventstore. However they will be skipped by the "evolve" function anyway.
 3) Where are more info about how to test this behavior?
 Answer: See the testList called hackingEventInStorageTest. 
 It will simply add invalid events and show that the current state is not affected by them.
 4) You also need to give timely feedback to the user. How can you achieve that if you satisfy invariants by skipping the events?
 Answer: you can't. The user may need to do some refresh or wait a confirmation (open a link that is not immediately generated). There is no immediate consistency in this case.



 ## Installation

 Just clone the project and run the tests (dotnet test)

 ## More info:
 [Sharpino (event sourcing library used in this example)](https://github.com/tonyx/Sharpino)
