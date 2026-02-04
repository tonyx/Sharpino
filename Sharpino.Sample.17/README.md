Work in progress

Proof of concept, Inspired by the example 16 with a difference:

This time it tries to establish a mechanism so that events causes commands (i.e. more like an actual process-manager)

So in the example 16 the "Failed" fires more commands at once (WorkOrder.Failed and MaterialCommand.Add)
In this example, instead, only the Filed command is fired, and by using the "messageSenders" if the related events are stored then message senders are used to invoke new Commands.

So in essence:
Example 16: FailWorkingItem = commands that says the some workingItems are failed and the related materials readd command are executed together.
Example 17: FailworkingItem is a single command that if generates the WorkOrderEvents.Failed, then as a consequene some AddMaterials events are executed

Note: serialization and deserialization is necessary only because of the way "optionallySendAggregateEventsAsync" works (meant to send message
through the network). It will change, probably.

A fix and a more complete example (for a "long running" sequence of commands) may come next.

Another point is that it would be more interesting when there is a more long running sequence of actions involved (i.e. notifyMaterialsToBeRestored and after a while,
because of human intervention, materials will be actually restored).



