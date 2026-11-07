module Sharpino.Lib.Test.EvolveSkippingErrorsTests

open System
open Expecto
open Sharpino.Core

// ---------------------------------------------------------------------------
// Test Domain Model: Counter
// ---------------------------------------------------------------------------
type CounterState = {
    Value: int
    History: string list
} with
    static member Zero = { Value = 0; History = [] }

type CounterEvent =
    | Add of int
    | Sub of int // Invariant: Value cannot drop below 0
    | SetPositive of int // Invariant: Value must be strictly > 0
    | FailAlways of string
    interface Event<CounterState> with
        member this.Process(state: CounterState) =
            match this with
            | Add n ->
                Ok { Value = state.Value + n; History = state.History @ [ sprintf "Add %d" n ] }
            | Sub n ->
                if state.Value - n < 0 then
                    Error (sprintf "Underflow: cannot subtract %d from %d" n state.Value)
                else
                    Ok { Value = state.Value - n; History = state.History @ [ sprintf "Sub %d" n ] }
            | SetPositive n ->
                if n <= 0 then
                    Error (sprintf "Invalid: %d is not positive" n)
                else
                    Ok { Value = n; History = state.History @ [ sprintf "Set %d" n ] }
            | FailAlways msg ->
                Error (sprintf "FailAlways: %s" msg)

// ---------------------------------------------------------------------------
// Test Domain Model: Bank Account (Causal Dependencies)
// ---------------------------------------------------------------------------
type AccountState = {
    Balance: decimal
    IsOpen: bool
    AuditLog: string list
} with
    static member Initial = { Balance = 0m; IsOpen = false; AuditLog = [] }

type AccountEvent =
    | AccountOpened of initialDeposit: decimal
    | MoneyDeposited of amount: decimal
    | MoneyWithdrawn of amount: decimal
    | AccountClosed
    interface Event<AccountState> with
        member this.Process(state: AccountState) =
            match this with
            | AccountOpened initialDeposit ->
                if state.IsOpen then
                    Error "Account is already open"
                elif initialDeposit < 0m then
                    Error "Initial deposit cannot be negative"
                else
                    Ok { Balance = initialDeposit; IsOpen = true; AuditLog = [ "Opened" ] }
            | MoneyDeposited amount ->
                if not state.IsOpen then
                    Error "Cannot deposit to closed or uninitialized account"
                elif amount <= 0m then
                    Error "Deposit amount must be positive"
                else
                    Ok { state with Balance = state.Balance + amount; AuditLog = state.AuditLog @ [ sprintf "Deposited %M" amount ] }
            | MoneyWithdrawn amount ->
                if not state.IsOpen then
                    Error "Cannot withdraw from closed or uninitialized account"
                elif amount <= 0m then
                    Error "Withdrawal amount must be positive"
                elif state.Balance < amount then
                    Error (sprintf "Insufficient funds: balance %M < amount %M" state.Balance amount)
                else
                    Ok { state with Balance = state.Balance - amount; AuditLog = state.AuditLog @ [ sprintf "Withdrew %M" amount ] }
            | AccountClosed ->
                if not state.IsOpen then
                    Error "Account is not open"
                elif state.Balance <> 0m then
                    Error (sprintf "Cannot close account with non-zero balance: %M" state.Balance)
                else
                    Ok { state with IsOpen = false; AuditLog = state.AuditLog @ [ "Closed" ] }

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
[<Tests>]
let tests =
    testList "Core evolveSkippingErrors Verification Tests" [

        testCase "Empty event list returns initial state wrapped in Ok" <| fun () ->
            let initial = CounterState.Zero
            let result = evolveSkippingErrors (Ok initial) [] initial
            Expect.equal result (Ok initial) "Empty events should yield initial state"

        testCase "Pure valid stream matches evolveUNforgivingErrors exactly" <| fun () ->
            let initial = CounterState.Zero
            let events: CounterEvent list = [ Add 10; Add 20; Sub 5; SetPositive 40; Add 2 ]
            let resSkipping = evolveSkippingErrors (Ok initial) events initial
            let resUnforgiving = evolveUNforgivingErrors initial events
            Expect.equal resSkipping (Ok { Value = 42; History = [ "Add 10"; "Add 20"; "Sub 5"; "Set 40"; "Add 2" ] }) "Should match expected evolved state"
            Expect.equal resSkipping resUnforgiving "evolveSkippingErrors must match evolveUNforgivingErrors on error-free streams"

        testCase "Stream with errors in middle skips failing events and applies subsequent valid events" <| fun () ->
            let initial = CounterState.Zero
            // 0 + 10 = 10; Sub 25 fails (skipped); + 5 = 15; SetPositive -1 fails (skipped); + 3 = 18
            let events: CounterEvent list = [
                Add 10
                Sub 25 // Error: 10 - 25 < 0
                Add 5
                SetPositive -1 // Error: <= 0
                Add 3
            ]
            let resUnforgiving = evolveUNforgivingErrors initial events
            Expect.isError resUnforgiving "evolveUNforgivingErrors should fail at Sub 25"

            let resSkipping = evolveSkippingErrors (Ok initial) events initial
            Expect.isOk resSkipping "evolveSkippingErrors should succeed"
            let state = resSkipping |> Result.get
            Expect.equal state.Value 18 "Value should be 10 + 5 + 3 = 18"
            Expect.equal state.History [ "Add 10"; "Add 5"; "Add 3" ] "History must contain only successfully applied events"

        testCase "Stream with all erroneous events returns original guard unchanged" <| fun () ->
            let initial = { Value = 100; History = [ "Initial" ] }
            let events: CounterEvent list = [
                Sub 200
                SetPositive -10
                FailAlways "corrupted record"
            ]
            let resSkipping = evolveSkippingErrors (Ok initial) events initial
            Expect.equal resSkipping (Ok initial) "All failing events should preserve initial state"

        testCase "evolve function strictly calls unforgiving evolve and halts on error" <| fun () ->
            let initial = CounterState.Zero
            let events: CounterEvent list = [ Add 50; Sub 100; Add 20 ]
            let result = evolve initial events
            Expect.isError result "evolve should strictly fail on bad event"

        testCase "Deep event stream verifies tail recursion without stack overflow" <| fun () ->
            let initial = CounterState.Zero
            // 2,000 events: alternating valid Add 1 and invalid FailAlways
            let events: CounterEvent list =
                [ 1 .. 2000 ]
                |> List.collect (fun _ -> [ Add 1; FailAlways "skip me" ])
            let res = evolveSkippingErrors (Ok initial) events initial
            Expect.isOk res "Tail call must handle large stream without stack overflow"
            let state = res |> Result.get
            Expect.equal state.Value 2000 "All 2,000 Add 1 events should be applied"

        // -------------------------------------------------------------------
        // Implementation Anomaly: The 'Error err, _::es' branch discards an unprocessed event
        // -------------------------------------------------------------------
        testCase "Passing Error accumulator drops the head event without processing it (Dead-code / API flaw demonstration)" <| fun () ->
            let initial = CounterState.Zero
            let headEvent = Add 100
            let tailEvents = [ Add 5 ]
            let events: CounterEvent list = headEvent :: tailEvents

            // If a caller passes (Error "previous error") as acc:
            // The function matches `| Error err, _::es -> evolveSkippingErrors (guard |> Ok) es guard`
            // Notice: `_` is headEvent (Add 100), which is completely valid!
            // But headEvent is dropped without its Process method EVER being executed!
            let res = evolveSkippingErrors (Error "caller error") events initial
            Expect.isOk res "Should return Ok"
            let state = res |> Result.get
            // If headEvent was processed, Value would be 105. Because it was dropped, Value is only 5!
            Expect.equal state.Value 5 "Head event was dropped unprocessed because acc was Error!"

        // -------------------------------------------------------------------
        // Semantic Vulnerability 1: Cascading Failures due to causal dependencies
        // -------------------------------------------------------------------
        testCase "Semantic issue: Skipping an event causes downstream causally dependent events to fail" <| fun () ->
            // In an event-sourced bank account:
            // Event 1: OpenAccount with 100
            // Event 2: (Corrupted) Deposit 50 -> fails
            // Event 3: Withdraw 120 -> depends on balance being 100 + 50 = 150!
            let initial = AccountState.Initial
            let events: AccountEvent list = [
                AccountOpened 100m
                MoneyDeposited -50m // Invalid event: negative deposit fails
                MoneyWithdrawn 120m // Valid in the sender's timeline (assumed balance 150), but fails because balance is only 100!
            ]
            let res = evolveSkippingErrors (Ok initial) events initial
            Expect.isOk res "evolveSkippingErrors returns Ok"
            let state = res |> Result.get
            // MoneyWithdrawn 120 was ALSO skipped because MoneyDeposited failed!
            Expect.equal state.Balance 100m "Balance remained at 100m because subsequent dependent withdrawal failed"
            Expect.equal state.AuditLog [ "Opened" ] "Withdrawal was silently lost"

        // -------------------------------------------------------------------
        // Semantic Vulnerability 2: Invariant Violation due to Partial Replay
        // -------------------------------------------------------------------
        testCase "Semantic issue: Skipping an event can allow later events to violate high-level domain invariants" <| fun () ->
            // Domain rule: You cannot close an account if balance is > 0, and you cannot deposit to a closed account.
            // Timeline intended:
            // 1. AccountOpened 100
            // 2. MoneyWithdrawn 100 (balance becomes 0)
            // 3. AccountClosed (valid because balance is 0)
            // 4. MoneyDeposited 50 (should FAIL because account is closed!)
            //
            // But if Event 2 (MoneyWithdrawn 100) errors out (e.g. temporary schema/format error):
            // Event 2 is skipped -> Balance remains 100.
            // Event 3 (AccountClosed) fails because Balance is 100 <> 0 -> Event 3 is skipped!
            // Event 4 (MoneyDeposited 50) is evaluated against an account that is STILL OPEN!
            // So Event 4 SUCCEEDS! The account was supposed to be closed, but is now open with balance 150!
            let initial = AccountState.Initial
            let eventsWithFaultyWithdrawal: AccountEvent list = [
                AccountOpened 100m
                MoneyWithdrawn -100m // Corrupted event (negative withdrawal fails)
                AccountClosed        // Fails because balance is 100m, not 0m
                MoneyDeposited 50m   // Succeeds because account was never closed!
            ]
            let res = evolveSkippingErrors (Ok initial) eventsWithFaultyWithdrawal initial
            let state = res |> Result.get
            Expect.equal state.IsOpen true "Account remained OPEN despite AccountClosed command having been stored"
            Expect.equal state.Balance 150m "Balance is 150m instead of closed"

        // -------------------------------------------------------------------
        // Semantic Vulnerability 3: Divergence between Cached/Snapshot state and Replay state
        // -------------------------------------------------------------------
        testCase "Semantic issue: Snapshot state diverges from Event Replay state when an event fails on replay" <| fun () ->
            // Suppose a node processed Event 1 and Event 2 in memory when code version allowed it,
            // and saved a snapshot at Event 2 with State = { Value = 50 }.
            let snapshotState = { Value = 50; History = [ "Snapshot" ] }
            let event3 = Add 10

            // Replay from snapshot:
            let stateFromSnapshot =
                match evolveSkippingErrors (Ok snapshotState) [ event3 ] snapshotState with
                | Ok s -> s
                | Error e -> failwith e

            // Full replay from scratch with Event 2 failing (e.g. after schema migration or bug fix):
            let event1 = Add 50
            let event2Corrupted = FailAlways "No longer valid under new business rules"
            let stateFromFullReplay =
                match evolveSkippingErrors (Ok CounterState.Zero) [ event1; event2Corrupted; event3 ] CounterState.Zero with
                | Ok s -> s
                | Error e -> failwith e

            // In this specific case, both happen to end at 60, but their histories and intermediate assumptions differ.
            // More critically, if Event 2 modified Value to 200:
            let event2Valid = Add 150
            let snapshotAt2 = { Value = 200; History = [ "Add 50"; "Add 150" ] }
            let stateFromSnapshot2 =
                match evolveSkippingErrors (Ok snapshotAt2) [ event3 ] snapshotAt2 with
                | Ok s -> s
                | Error e -> failwith e

            let stateFromReplaySkipping2 =
                match evolveSkippingErrors (Ok CounterState.Zero) [ event1; event2Corrupted; event3 ] CounterState.Zero with
                | Ok s -> s
                | Error e -> failwith e

            Expect.notEqual stateFromSnapshot2.Value stateFromReplaySkipping2.Value
                "Snapshot state (210) diverges from Event-replay state (60) because Event 2 was skipped during replay!"

        // -------------------------------------------------------------------
        // Semantic Vulnerability 4: Non-idempotent duplicate events are NOT skipped unless Process errors
        // -------------------------------------------------------------------
        testCase "Duplicates: evolveSkippingErrors does NOT skip duplicate events unless Process explicitly returns Error" <| fun () ->
            let initial = CounterState.Zero
            // If the same non-idempotent event is inserted twice into the event store (e.g. network retry):
            let duplicateEvent = Add 10
            let events = [ duplicateEvent; duplicateEvent ]
            let res = evolveSkippingErrors (Ok initial) events initial
            let state = res |> Result.get
            // Since Add 10 does not return Error, both are processed!
            Expect.equal state.Value 20 "Both duplicate events are processed, resulting in 20 instead of 10!"

        // -------------------------------------------------------------------
        // Semantic Vulnerability 5: Non-commutativity and Race Conditions (First-to-Replay-Wins)
        // -------------------------------------------------------------------
        testCase "Concurrency/Ordering: First-to-replay-wins creates non-deterministic final state depending on insertion order" <| fun () ->
            // Two concurrent users try to claim the same single seat (ID 1):
            let initial = CounterState.Zero // 0 means unassigned
            let userAClaims = SetPositive 100 // User 100 claims seat
            let userBClaims = SetPositive 200 // User 200 claims seat

            // Stream order [A; B]:
            // Suppose Process allows claiming only once (simulate by custom event):
            let claimSeat (userId: int) =
                { new Event<int> with
                    member _.Process(currentSeatOwner: int) =
                        if currentSeatOwner <> 0 then
                            Error (sprintf "Seat already booked by user %d" currentSeatOwner)
                        else
                            Ok userId
                }

            let resAB = evolveSkippingErrors (Ok 0) [ claimSeat 100; claimSeat 200 ] 0
            let resBA = evolveSkippingErrors (Ok 0) [ claimSeat 200; claimSeat 100 ] 0

            Expect.equal (resAB |> Result.get) 100 "Order [A; B] gives seat to user 100 (B skipped)"
            Expect.equal (resBA |> Result.get) 200 "Order [B; A] gives seat to user 200 (A skipped)"
            Expect.notEqual (resAB |> Result.get) (resBA |> Result.get) "Final state is fundamentally order-dependent"
    ]
