# Sharpino Caching Architecture Diagrams

These diagrams visualize the key concepts of the caching architecture in Sharpino.

## 1. Unidirectional Design vs Bidirectional

```mermaid
graph TD
    subgraph Bidirectional
        A[Student Aggregate] <--> B[Course Aggregate]
        style A stroke:#f66,stroke-width:2px;
        style B stroke:#f66,stroke-width:2px;
    end

    subgraph Unidirectional
        C[Enrollment Aggregate] --> D[Student Aggregate]
        C --> E[Course Aggregate]
        
        F[Student Courses Detail] -. "Materialized View" .-> C
    end
```

## 2. Refreshable Details Flow

This diagram shows how emitting an event triggers the dependent details to refresh.

```mermaid
sequenceDiagram
    participant App as Application
    participant AC as AggregateCache3
    participant EventStore as Event Store
    participant DC as DetailsCache
    participant L1 as statesDetails L1 Cache

    App->>AC: Command modifies Aggregate
    AC->>EventStore: Append Event
    EventStore-->>AC: Success
    AC->>AC: Clean(AggregateId) / Cache new State
    note over AC: Triggers local Backplane Message
    
    AC->>DC: RefreshDependentDetails(AggregateId)
    DC->>DC: Look up 'objectDetailsAssociations'
    
    loop For each related DetailsCacheKey
        DC->>L1: Get Refreshable<'A>
        L1-->>DC: Refreshable instance
        DC->>L1: Invoke Refresh()
        L1-->>DC: Refreshed Data
        DC->>L1: Update L1 Cache
    end
```

## 3. Distributed Cache & Backplane Synchronization

This flow illustrates what happens across two distributed application nodes when an aggregate is updated.

```mermaid
graph TD
    subgraph Node_A_Writer
        A_App[App Command] --> A_AC[AggregateCache3]
        A_AC --> A_EventStore[Database / EventStore]
        A_AC -. "1. Update L1 (BoxedState)" .-> A_L1[L1 Memory Cache]
        A_AC -. "2. Serialize Entry" .-> L2[(L2 Distributed Cache<br/>Postgres / Redis / SQL Server)]
        A_AC -- "3. Publish Message" --> BP((Backplane<br/>PG Notify / Redis / Service Bus))
    end

    subgraph Node_B_Reader
        BP -- "4. Receive Message" --> B_AC[AggregateCache3]
        B_AC -. "5. Invalidate L1 Only" .-> B_L1_Agg[Aggregate L1 Cache]
        B_AC -- "6. Trigger Refresh" --> B_DC[DetailsCache]
        B_DC -. "7. Recompute View" .-> B_L1_Det[statesDetails L1 Cache]
        B_AC -. "8. Rehydrate from L2" .-> L2
    end
```
