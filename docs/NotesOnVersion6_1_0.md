## Extending the decision boundary

In Sharpino, the flow of executing one or more commands has always been based on specifying any eventual "extended" decision boundary prior to executing the commands.  In relation to the consistency respect to the "extended" decision boundary, this works fine on a statistical ground but may have still issues which are the fact that the extended condition may change when the command is executed, no matter how unlikely they can be.
A possibility to mitigate this issue is by using multiple aggregates and commands in a single execution flow, so that the condition can be evaluated on the combined state of multiple aggregates. 
Still, this does not fully resolve the issue because any decision evaluated by any command is local to the involved aggregate and cannot be a function of all the states of all the aggregates involved as a whole and therefore it will be possible to imagine some logical expressions that define a constraints in a way that cannot be expressed as a conjunction of local constraints (example may come later).

The "extending the decision boundary" feature is now available in Sharpino 6.1.0. This feature allows you to include, using a particular lambda expression, an arbitrary extended decision boundary that includes extra aggregates and enforces consistency in a wider sense. The optimistic lock control is able to check the extended scope as well, without limiting it to the event ID-based optimistic lock control associated with the passed aggregates. This ensure that the command succeeds only if the extended condition holds true. 

A form of the "lambda" expression that evaluates the extended scope is given by the following example in the Sharpino.Sample.9 code:

```FSharp

let crossAggregatesConstraint =
    fun () ->
        result
            {
                let! (jackEvId, jack) = studentViewer jack.Id
                let extraStreamsLocks =
                    [((jack.Id, Student.Version + Student.StorageName), jackEvId)] |> Map.ofList

                let! constraintToBeMet =
                    (not 
                            (jack.Courses |> List.exists (fun c -> c = literature.Id)
                            && (jack.Courses |> List.exists (fun c -> c = math.Id)))
                    )
                    |> Result.ofBool "constraint not met"
                return extraStreamsLocks
            }

```
the test that contains this expression is named
"Teacher John will be able to teach Math if student Jack is enrolled in that course and not in literature"

That means that despite only the courses and teachers streams are involved in the command execution, the student stream is also involved in the "wider sense" of the decision boundary, because the constraint depends o a particular combination of the student's enrollment.

As we can see the returned value is a map of the event ID and the aggregate ID, related to the student stream. 

The commandHanlder may evaluate this lambda at any time and it will send to the event store any extended scope information so that the optimistic lock check is extended to those extra-streams.


## A recap of the "old way"

1. If a particular condition is met, the application executes a command by sending it to the CommandHandler component.
2. The CommandHandler retrieves the current event ID (version) of the object/aggregate.
3. It executes the command on that state, producing a new state and a list of events if the decision function determines the new state is consistent (i.e., the execution of the command returns an `Ok`).
4. It passes the event ID and the events to the event store.
5. The event store stores those events if the last event ID on the stream for that aggregate corresponds to the event ID passed.
6. If the previous step succeeds, it feeds the cache with the new state.
7. It then activates a fire-and-forget task to send the events to a message bus.
8. Finally, it refreshes, also in a fire-and-forget way, any cached details that depend on this particular aggregate.

In this previous approach, there is no further check based on an optimistic lock check involving the extended boundary to verify that the condition applied at step 1 still holds after the events are run.

## An example
Suppose a business rule tries to preserve seat fragmentation and filters reservations accordingly. For instance, a rule could be: "this row should be fully available for new reservations only if the nearest rows are not also fully reserved."
The application logic, in the old way, might encode this rule in the condition that has to be met before deciding to execute the command to reserve all the seats in a specific row (which is an aggregate). So before issuing the command, the application evaluates the state of the rows that are close to the row that needs to be reserved.

## The "multiple commands at once" alternative
An alternative is running multiple commands at once on different aggregates, where only one command actually produces meaningful events, and the other commands are just used to evaluate constraints (producing "do-nothing" events, or an empty list of events).

The previous approach of using multiple commands may solve the problem as long as the decision is always a conjunction of local decisions for each extra-aggregate involved. However, a more complex decision function over the combined state of all involved extra-aggregates may not be easy to apply and manage.
In this case, the whole is more than the sum of its parts. Regarding the previous example of keeping some fragmentation of seats in nearby rows, we might have a condition like: "the sum of the existing reservations in neighboring rows cannot exceed 80%" to allow fully reserving a specific row. This would be difficult to implement using multiple commands (and thus multiple local decision functions) executed at once.

__Note__: here I am deliberately avoiding introducing any "process manager" or "saga" based solution. Anyone who endorses those patterns will not find anything about them here. This does not mean I dismiss or dislike those approaches; I simply want to focus on alternative ways.

The new "decision boundary" feature tries to address the problem of keeping consistency in complex scenarios by allowing you to include an arbitrary extended decision boundary as part of the command execution flow.

Here is the new flow, which serves as an alternative to the previous one:

1. Express the particular condition to be met, involving some aggregates that are part of the decision boundary (but not the aggregate that will be hit by the command and the related events) as a specific lambda expression. If it succeeds, this lambda will also return a map of aggregate IDs, the last event ID, and the stream names of the aggregates that matter for that decision. If the lambda returns successfully, it means that the condition related to that extended boundary applies and, moreover, the optimistic lock-related check information for this boundary is available.
2. The CommandHandler retrieves the current event ID (version) of the target aggregate.
3. It executes the command on that state, producing a new state and a list of events.
4. It evaluates the lambda related to the decision boundary, verifying that it returns an `Ok` with triples `(eventId, aggregateId, streamName)`.
5. It passes the event ID to the event store and the events, and also the triples `(eventId, aggregateId, streamName)`.

The event store stores those events if the last event ID on the event store corresponds to the event ID passed, and also if the triples `(eventId, aggregateId, streamName)` related to the extended decision boundary context match.
If all the previous storing operations succeed, it means that the event store will produce a state that is consistent with all the mentioned constraints, which include the aggregate and the extended context. It will enforce the rule that the event store will contain events that are consistent with the whole set of applied constraints. This is a stronger consistency than the probabilistic consistency related to the statistical approach of running multiple commands at once.

# Recap
When running a command or multiple commands, for certain classes of `runCommand`, alternative implementations allow sending an extended-scope decision function that will be verified as part of the command execution flow, maintaining the consistency of the event store with respect to those extended constraints.



